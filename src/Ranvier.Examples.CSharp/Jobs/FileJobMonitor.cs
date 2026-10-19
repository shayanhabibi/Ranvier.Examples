using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Ranvier;
using Ranvier.CSharp;
using static Ranvier.CSharp.Reactive;

namespace Ranvier.Examples.CSharp.Jobs;

/// <summary>An immutable processing snapshot with a stable key across progress updates and retries.</summary>
public sealed record FileJob(int Id, string Path, string State, long Bytes, long Length, string Digest, string Error, int Attempt)
{
    public override string ToString() => $"#{Id} · {State} · {System.IO.Path.GetFileName(Path)} · {Bytes:N0}/{Length:N0} bytes";
}

/// <summary>A terminal processing event retained independently of throttled progress presentation.</summary>
public sealed record JobTerminalEvent(int Id, string State, int Attempt);

/// <summary>Bounded read-only file hashing with graph-thread publication and keyed reactive views.</summary>
public sealed class FileJobMonitor : IDisposable
{
    private readonly Graph graph;
    private readonly Owner owner;
    private readonly SemaphoreSlim slots;
    private readonly Dictionary<int, CancellationTokenSource> cancellation = [];
    private readonly List<Task> tasks = [];
    private readonly List<JobTerminalEvent> terminal = [];
    private readonly ConcurrentDictionary<int, TaskCompletionSource> publications = [];
    private readonly Signal<string> filter;
    private readonly Signal<int> selection;
    private readonly Memo<int> succeeded, failed, cancelled, running;
    private readonly Memo<long> bytes;
    private readonly Memo<string> detail;
    private readonly Timed<string> progress;
    private int nextId, publicationId, activeWorkers, peakWorkers;
    private volatile bool disposed;

    public FileJobMonitor(Graph graph, int concurrency = 2)
    {
        if (concurrency < 1 || concurrency > 16) throw new ArgumentOutOfRangeException(nameof(concurrency));
        this.graph = graph;
        slots = new(concurrency, concurrency);
        using var active = graph.Activate();
        Signal<string> localFilter = null!;
        Signal<int> localSelection = null!;
        Memo<int> localSucceeded = null!, localFailed = null!, localCancelled = null!, localRunning = null!;
        Memo<long> localBytes = null!;
        Memo<string> localDetail = null!;
        Timed<string> localProgress = null!;
        owner = Root(scope =>
        {
            Jobs = KeyedCollection<FileJob, int>(row => row.Id);
            localFilter = Signal("All");
            localSelection = Signal(0);
            Visible = Jobs.Rows.Where(row => localFilter.Value == "All" || row.State == localFilter.Value).OrderBy(row => row.Id);
            Items = Visible.AsObservableCollection();
            localSucceeded = Jobs.Rows.Count(row => row.State == "Completed");
            localFailed = Jobs.Rows.Count(row => row.State == "Failed");
            localCancelled = Jobs.Rows.Count(row => row.State == "Cancelled");
            localRunning = Jobs.Rows.Count(row => row.State == "Running");
            localBytes = Jobs.Rows.Sum(row => row.Bytes);
            localDetail = Memo(() => Jobs.Rows.TryGetValue(localSelection.Value, out var job)
                ? $"{job.Path}\n{job.State} · attempt {job.Attempt}\nSHA-256: {job.Digest}\n{job.Error}" : "Select a file job");
            localProgress = Throttle(TimeSpan.FromMilliseconds(100),
                () => $"{localBytes.Value:N0} bytes processed · {localRunning.Value} active");
            return scope;
        });
        filter = localFilter; selection = localSelection; succeeded = localSucceeded; failed = localFailed;
        cancelled = localCancelled; running = localRunning; bytes = localBytes; detail = localDetail; progress = localProgress;
    }

    public KeyedCollection<int, FileJob> Jobs { get; private set; } = null!;
    public Projection<int, FileJob> Visible { get; private set; } = null!;
    public ObservableCollection<FileJob> Items { get; private set; } = null!;
    public FileJob[] Snapshot => Jobs.Rows.Keys.Select(Jobs.Rows.Get).ToArray();
    public string Filter { get => filter.Value; set => filter.Value = value; }
    public int SelectedId { get => selection.Value; set => selection.Value = value; }
    public string SelectedDetail => detail.Value;
    public int VisibleCount => Visible.Count;
    public int Succeeded => succeeded.Value;
    public int Failed => failed.Value;
    public int Cancelled => cancelled.Value;
    public int Running => running.Value;
    public long TotalBytes => bytes.Value;
    public string ProgressText => progress.Value;
    public string Summary => $"{Succeeded} completed · {Failed} failed · {Cancelled} cancelled · {Jobs.Rows.Count} total";
    public int PeakWorkers => Volatile.Read(ref peakWorkers);
    public IReadOnlyList<JobTerminalEvent> TerminalEvents => terminal.ToArray();
    public bool IsDisposed => disposed;
    public Task Completion => Task.WhenAll(tasks.ToArray());

    public int[] Queue(IEnumerable<string> paths)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string[] accepted = paths.Select(System.IO.Path.GetFullPath).ToArray();
        using var active = graph.Activate();
        var ids = new List<int>();
        Jobs.Edit(edit =>
        {
            foreach (string path in accepted)
            {
                int id = ++nextId;
                edit.AddOrUpdate(new(id, System.IO.Path.GetFullPath(path), "Queued", 0, 0, "", "", 1));
                ids.Add(id);
            }
        });
        foreach (int id in ids) Start(id);
        return ids.ToArray();
    }

    private void Start(int id)
    {
        var job = Jobs.Rows.Get(id);
        var source = new CancellationTokenSource();
        if (cancellation.Remove(id, out var old)) old.Dispose();
        cancellation[id] = source;
        tasks.Add(Task.Run(() => ProcessAsync(job, source.Token)));
    }

    public void Cancel(int id)
    {
        if (!disposed && cancellation.TryGetValue(id, out var source)) source.Cancel();
    }

    public void Retry(int id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var active = graph.Activate();
        var job = Jobs.Rows.Get(id);
        if (job.State is not ("Failed" or "Cancelled")) return;
        Jobs.AddOrUpdate(job with { State = "Queued", Bytes = 0, Length = 0, Digest = "", Error = "", Attempt = job.Attempt + 1 });
        Start(id);
    }

    private Task PublishAsync(Action update)
    {
        if (disposed) return Task.CompletedTask;
        int id = Interlocked.Increment(ref publicationId);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        publications[id] = completion;
        if (disposed) completion.TrySetResult();
        else
        {
            try
            {
                graph.Dispatch(() =>
                {
                    try { if (!disposed) { using var active = graph.Activate(); update(); } completion.TrySetResult(); }
                    catch (Exception error) { completion.TrySetException(error); }
                    finally { publications.TryRemove(id, out _); }
                });
            }
            catch (ObjectDisposedException) { completion.TrySetResult(); publications.TryRemove(id, out _); }
        }
        return completion.Task;
    }

    private async Task ProcessAsync(FileJob original, CancellationToken token)
    {
        bool entered = false;
        long count = 0, length = 0;
        string state = "Completed", digest = "", error = "";
        try
        {
            await slots.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            int workers = Interlocked.Increment(ref activeWorkers);
            int previous;
            do { previous = Volatile.Read(ref peakWorkers); if (workers <= previous) break; }
            while (Interlocked.CompareExchange(ref peakWorkers, workers, previous) != previous);
            token.ThrowIfCancellationRequested();
            await using var input = new FileStream(original.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            length = input.Length;
            await PublishAsync(() => Jobs.AddOrUpdate(original with { State = "Running", Length = length })).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[128 * 1024];
            int read;
            while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                hash.AppendData(buffer, 0, read);
                count += read;
                long captured = count;
                await PublishAsync(() => Jobs.AddOrUpdate(original with { State = "Running", Bytes = captured, Length = length })).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            digest = Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (OperationCanceledException) { state = "Cancelled"; }
        catch (Exception failure) { state = "Failed"; error = failure.Message; }
        finally
        {
            if (entered) { Interlocked.Decrement(ref activeWorkers); slots.Release(); }
        }
        await PublishAsync(() =>
        {
            Batch(() =>
            {
                Jobs.AddOrUpdate(original with { State = state, Bytes = count, Length = length, Digest = digest, Error = error });
                terminal.Add(new(original.Id, state, original.Attempt));
            });
        }).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var source in cancellation.Values) source.Cancel();
        foreach (var completion in publications.Values) completion.TrySetResult();
        owner.Dispose();
        _ = Completion.ContinueWith(_ =>
        {
            foreach (var source in cancellation.Values) source.Dispose();
            slots.Dispose();
        }, TaskScheduler.Default);
    }
}
