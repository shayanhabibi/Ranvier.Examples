using System.Net;
using Ranvier.Examples.CSharp.Support;
using Xunit;

namespace Ranvier.Examples.CSharp.Tests;

public sealed class SupportApiTests
{
    [Fact]
    public async Task SavePersistsAcrossServerRestartAndRejectsStaleVersions()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "support-tests", Guid.NewGuid().ToString("N"));
        int nextVersion;
        await using (var server = await SupportApi.StartAsync(folder, TestContext.Current.CancellationToken))
        {
            using var client = new TicketHttpClient(server.BaseAddress);
            var original = await client.GetAsync(1, CancellationToken.None);
            var saved = await client.SaveAsync(new(1, "Investigated the failing worker", "Investigating", original.Version), CancellationToken.None);
            nextVersion = saved.Version;
            Assert.Equal(original.Version + 1, nextVersion);
            await Assert.ThrowsAsync<TicketConflictException>(() => client.SaveAsync(new(1, "Stale write", "Resolved", original.Version), CancellationToken.None));
        }
        await using (var restarted = await SupportApi.StartAsync(folder, TestContext.Current.CancellationToken))
        {
            using var client = new TicketHttpClient(restarted.BaseAddress);
            var saved = await client.GetAsync(1, CancellationToken.None);
            Assert.Equal("Investigated the failing worker", saved.Notes);
            Assert.Equal(nextVersion, saved.Version);
        }
        Directory.Delete(folder, true);
    }

    [Fact]
    public async Task SearchUnavailableRecoveryAndMissingRecordsUseRealHttp()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "support-tests", Guid.NewGuid().ToString("N"));
        await using (var server = await SupportApi.StartAsync(folder, TestContext.Current.CancellationToken))
        {
            using var client = new TicketHttpClient(server.BaseAddress);
            var matches = await client.SearchAsync("worker", CancellationToken.None);
            Assert.NotEmpty(matches);
            Assert.All(matches, ticket => Assert.Contains("worker", ticket.Title, StringComparison.OrdinalIgnoreCase));
            server.Available = false;
            var failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync("", CancellationToken.None));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
            server.Available = true;
            Assert.NotEmpty(await client.SearchAsync("", CancellationToken.None));
            var missing = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(99999, CancellationToken.None));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        Directory.Delete(folder, true);
    }
}
