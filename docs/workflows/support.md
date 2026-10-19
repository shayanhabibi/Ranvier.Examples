# Support workbench

Start either desktop launcher and choose Support workbench. The app starts a loopback-only ASP.NET Core service on an automatically assigned port. Its versioned JSON file is persistent; the client uses real HTTP requests.

1. Search for `worker`; admission is debounced by 250 ms. Clear the search and select a ticket.
2. Type an investigation note. Apply a colleague update through the API. The accepted server note changes while your draft remains.
3. Choose **Keep draft / use new version** to intentionally rebase, then save. **Use server values** instead discards your local edits.
4. Edit again, save, then go Back immediately. The editor releases its detail lease while the session-owned save finishes and refreshes the queue.
5. Disconnect the API. A queue refresh fails with real HTTP 503; previously accepted data remains visible. Restore the API to retry.

[SupportDesk.fs](../../src/Ranvier.Examples.FSharp/SupportDesk.fs) separates the search key, search lease, detail lease and independent editable drafts. The search trigger reads the debounced term, then starts request work untracked so incidental queue state does not become a request dependency. Old requests cannot replace a newer selection.

Saving captures the ticket ID, expected version and draft values before navigation can change them. Query mutation reconciliation updates a loaded detail and invalidates search keys. Invalidation marks data stale; the explicit queue ensure performs the subsequent fetch. A reconciliation failure retains the successful receipt instead of repeating a remote write.

[SupportApi.cs](../../src/Ranvier.Examples.CSharp/Support/SupportApi.cs) serializes writes, checks expected versions, writes a replacement JSON file before publishing the accepted record, and holds an exclusive store lock. Another process cannot silently overwrite a cached copy of that store.

The console's default F# scenario exercises conflict detection, rebase, save after Back and service recovery without synthetic sleeps. Tests use controlled task completions to force response-order races.
