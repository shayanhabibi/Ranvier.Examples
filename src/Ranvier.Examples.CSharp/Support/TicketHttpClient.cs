using System.Net;
using System.Net.Http.Json;

namespace Ranvier.Examples.CSharp.Support;

/// <summary>HTTP transport for versioned ticket operations.</summary>
public sealed class TicketHttpClient(Uri baseAddress) : ITicketService, IDisposable
{
    private readonly HttpClient http = new() { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(15) };

    public async Task<TicketRecord[]> SearchAsync(string term, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<TicketRecord[]>($"tickets?term={Uri.EscapeDataString(term)}", cancellationToken) ?? [];

    public async Task<TicketRecord> GetAsync(int id, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<TicketRecord>($"tickets/{id}", cancellationToken)
        ?? throw new InvalidDataException("The ticket response was empty.");

    public async Task<TicketRecord> SaveAsync(TicketUpdate update, CancellationToken cancellationToken)
    {
        using var response = await http.PutAsJsonAsync($"tickets/{update.Id}", update, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new TicketConflictException("The server has a newer version. Refresh, compare your draft, and accept the new baseline before saving.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TicketRecord>(cancellationToken)
            ?? throw new InvalidDataException("The save response was empty.");
    }

    public void Dispose() => http.Dispose();
}
