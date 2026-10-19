namespace Ranvier.Examples.CSharp.Support;

/// <summary>An accepted server ticket; Version is the optimistic concurrency token.</summary>
public sealed record TicketRecord(int Id, string Title, string State, string Notes, int Version)
{
    public override string ToString() => $"#{Id} · {State} · {Title}";
}

/// <summary>A captured edit against a specific server version.</summary>
public sealed record TicketUpdate(int Id, string Notes, string State, int ExpectedVersion);

/// <summary>Remote reads and writes consumed by the reactive workbench.</summary>
public interface ITicketService
{
    Task<TicketRecord[]> SearchAsync(string term, CancellationToken cancellationToken);
    Task<TicketRecord> GetAsync(int id, CancellationToken cancellationToken);
    Task<TicketRecord> SaveAsync(TicketUpdate update, CancellationToken cancellationToken);
}

/// <summary>The accepted record changed since the draft's baseline.</summary>
public sealed class TicketConflictException(string message) : Exception(message);
