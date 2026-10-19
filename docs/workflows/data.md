# Data quality editor

Choose Data quality. The initial CSV contains six station readings: three included valid rows, two included invalid rows, and one excluded row.

1. Select row 4. Replace `sensor fault` with `19`.
2. Select row 5. Replace unit `deg` with `C`. The summary becomes five included valid rows, zero invalid rows, mean **20.26 C**.
3. Inspect the selected row's normalization execution count. Editing another row reuses this row's cached calculation.
4. Convert included valid rows to Fahrenheit or Kelvin. The normalized Celsius mean retains the same meaning, and observers see one settled batch.
5. Edit the source CSV externally and reload. Drafts survive matching IDs. Changed baselines show conflicts; **Use source values for row** accepts upstream. Removed edited rows remain visible as invalid orphans until explicitly accepted.
6. Export included rows. Invalid included values block export; excluded rows remain editable and are omitted. The destination is an actual CSV file with quoted fields.

[DataQuality.fs](../../src/Ranvier.Examples.FSharp/DataQuality.fs) parses CSV off-thread, validates structure and unique IDs, then merges rows on the graph. Each row owns editable raw-text/unit/inclusion drafts and a memoized normalization calculation. Invalid raw text remains visible. Numeric input uses a decimal point; decimal commas require repair rather than silently changing scale.

Keyed row projections filter, map captions and incrementally calculate counts, sums and means. The UI list uses the projection's observable collection adapter. Selection drives a separate asynchronous calibration lookup, with pending/error boundaries and superseded-result suppression. The desktop lookup reads the bundled JSON calibration records.

The F# `data` console checks the repair result and unchanged-row execution count, converts units in a batch, and writes a repaired CSV. Tests also cover quoted station names, failed export, source changes and out-of-order calibration completions.
