# File processing

Choose File processing. Queue the sample files or enter one path per line. The monitor streams file bytes through SHA-256; each input gets a stable job ID.

Select a job to inspect its digest, error and attempt number. Filter by state. Queue a large file several times to observe two active worker slots and queued work, then cancel a queued or running entry. A missing path produces a failed row; create the file and retry that row to preserve its ID while increasing its attempt number.

[FileJobMonitor.cs](../../src/Ranvier.Examples.CSharp/Jobs/FileJobMonitor.cs) uses a semaphore for worker admission. It validates all paths before inserting a queue batch. Workers never mutate graph nodes directly: each chunk awaits graph-thread publication. This bounds outstanding progress publications rather than accumulating one callback for every chunk.

The keyed collection supplies filtered/sorted rows, an observable collection adapter and incremental counters. The UI follows stable row keys with independently reactive captions, retaining selection across immutable progress snapshots and filter changes. Progress text is throttled by 100 ms, while terminal state and terminal-event history are immediate and retained independently. Selected details follow a stable ID across immutable row replacement and retry.

The graph host pumps pending work in console scenarios. Avalonia provides graph-thread scheduling for the desktop host. Disposing the monitor cancels workers and settles outstanding publication acknowledgements; the host waits for completion before disposing the graph.

The default C# console hashes bundled files, verifies success and concurrency limits, then demonstrates a real missing-file failure and repaired retry. Tests independently compare hashes with SHA256.HashData and cover cancellation and disposal.
