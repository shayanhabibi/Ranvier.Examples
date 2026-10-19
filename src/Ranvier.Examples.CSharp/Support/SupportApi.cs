using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ranvier.Examples.CSharp.Support;

/// <summary>A loopback-only HTTP service backed by a versioned JSON ticket store.</summary>
public sealed class SupportApi : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly FileStream storeLock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string file;
    private TicketRecord[] tickets;
    private volatile bool available = true;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private SupportApi(WebApplication app, string file, TicketRecord[] tickets, FileStream storeLock)
    {
        this.app = app; this.file = file; this.tickets = tickets; this.storeLock = storeLock;
    }

    public Uri BaseAddress { get; private set; } = null!;
    public bool Available { get => available; set => available = value; }

    public static async Task<SupportApi> StartAsync(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var storeLock = new FileStream(Path.Combine(directory, ".store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            string file = Path.Combine(directory, "tickets.json");
            TicketRecord[] records = File.Exists(file)
                ? JsonSerializer.Deserialize<TicketRecord[]>(await File.ReadAllTextAsync(file, cancellationToken), Json)
                    ?? throw new InvalidDataException("The ticket store contains no array.")
                : [
                    new(1, "Worker export fails for large reports", "Open", "Customer sees an incomplete export.", 1),
                new(2, "Search index is missing recent imports", "Investigating", "Rebuild finished, verification pending.", 1),
                new(3, "Worker cancellation leaves a temporary file", "Open", "Reproduce with a cancelled archive job.", 1),
                new(4, "CSV import rejects decimal commas", "Open", "Confirm the supplied locale and raw file.", 1),
                new(5, "Dashboard reconnect loses the selected device", "Resolved", "Selection now follows stable device IDs.", 1),
                new(6, "Invoice preview uses the previous address", "Open", "A slow response arrived after navigation.", 1)
                  ];
            if (!File.Exists(file)) await File.WriteAllTextAsync(file, JsonSerializer.Serialize(records, Json), cancellationToken);
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var app = builder.Build();
            var server = new SupportApi(app, file, records, storeLock);
            app.MapGet("/tickets", (string? term, CancellationToken token) => server.ReadAsync(term ?? "", null, token));
            app.MapGet("/tickets/{id:int}", (int id, HttpContext context) => server.ReadAsync("", id, context.RequestAborted));
            app.MapPut("/tickets/{id:int}", (int id, TicketUpdate update, HttpContext context) => server.SaveAsync(id, update, context.RequestAborted));
            try
            {
                await app.StartAsync(cancellationToken);
                var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
                server.BaseAddress = new Uri(addresses.Addresses.Single() + "/");
                return server;
            }
            catch { await app.DisposeAsync(); throw; }
        }
        catch { await storeLock.DisposeAsync(); throw; }
    }

    private async Task<IResult> ReadAsync(string term, int? id, CancellationToken token)
    {
        if (!available) return Results.StatusCode(503);
        await gate.WaitAsync(token);
        try
        {
            if (id is { } key)
            {
                var ticket = tickets.SingleOrDefault(t => t.Id == key);
                return ticket is null ? Results.NotFound() : Results.Ok(ticket);
            }
            return Results.Ok(tickets.Where(t => t.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || t.Notes.Contains(term, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.Id).ToArray());
        }
        finally { gate.Release(); }
    }

    private async Task<IResult> SaveAsync(int id, TicketUpdate update, CancellationToken token)
    {
        if (!available) return Results.StatusCode(503);
        if (id != update.Id || update.Notes is null || update.Notes.Length > 4000
            || update.State is not ("Open" or "Investigating" or "Resolved"))
            return Results.BadRequest("State must be Open, Investigating or Resolved; notes must have at most 4000 characters.");
        await gate.WaitAsync(token);
        try
        {
            int index = Array.FindIndex(tickets, t => t.Id == id);
            if (index < 0) return Results.NotFound();
            var current = tickets[index];
            if (current.Version != update.ExpectedVersion) return Results.Conflict();
            var saved = current with { Notes = update.Notes, State = update.State, Version = current.Version + 1 };
            var next = tickets.ToArray();
            next[index] = saved;
            string temporary = file + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(next, Json), token);
            File.Move(temporary, file, true);
            tickets = next;
            return Results.Ok(saved);
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
        gate.Dispose();
        await storeLock.DisposeAsync();
    }
}
