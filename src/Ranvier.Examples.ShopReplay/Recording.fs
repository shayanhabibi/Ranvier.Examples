namespace Ranvier.Examples.ShopReplay

open System
open System.IO
open System.Text
open System.Text.Json
open Ranvier

/// <summary>The sample's JSONL representation of a Ranvier journal entry.</summary>
type JournalRow =
    { Schema: int
      Stream: string
      Seq: int
      Kind: string
      Key: string
      PayloadType: string
      Codec: string
      Version: int
      Data: string
      Origin: string
      Started: int
      Index: int
      Call: int }

/// <summary>The production observations used to verify a replay, independently of its trace.</summary>
type Capture =
    { Format: int
      Producer: string
      Observations: Checkout array }

/// <summary>Durable sample recordings and immutable checkpoint copies.</summary>
module Recording =
    /// <summary>Serializes a keyed input or a flush; non-replayable inputs fail capture.</summary>
    let encode (entry: JournalEntry) =
        let codec, version, data =
            match entry.Payload with
            | JournalPayload.Encoded (name, version, text) -> name, version, text
            | JournalPayload.Empty -> null, 0, null
            | JournalPayload.NonReplayable kind -> invalidOp ("Cannot capture " + kind)

        let origin, started, index, call =
            match entry.Origin with
            | JournalOrigin.External -> "external", 0, 0, 0
            | JournalOrigin.Produced (started, index, call) -> "produced", started, index, call

        { Schema = entry.Schema
          Stream = entry.Stream
          Seq = entry.Seq
          Kind = string entry.Kind
          Key = Option.toObj entry.Key
          PayloadType = entry.PayloadType
          Codec = codec
          Version = version
          Data = data
          Origin = origin
          Started = started
          Index = index
          Call = call }

    /// <summary>Decodes the sample's journal row, rejecting unknown entry and origin kinds.</summary>
    let decode (row: JournalRow) : JournalEntry =
        let kind =
            match row.Kind with
            | "Write" -> JournalEntryKind.Write
            | "Settle" -> JournalEntryKind.Settle
            | "Fail" -> JournalEntryKind.Fail
            | "Message" -> JournalEntryKind.Message
            | "Flush" -> JournalEntryKind.Flush
            | "Setup" -> JournalEntryKind.Setup
            | other -> raise (InvalidDataException ("Unknown journal kind: " + other))

        let origin =
            match row.Origin with
            | "external" -> JournalOrigin.External
            | "produced" -> JournalOrigin.Produced (row.Started, row.Index, row.Call)
            | other -> raise (InvalidDataException ("Unknown journal origin: " + other))

        if row.Schema <> 3 || row.Stream <> "shop" then
            raise (InvalidDataException "Unsupported shop journal schema or stream")

        let payload =
            if kind = JournalEntryKind.Flush then
                JournalPayload.Empty
            elif isNull row.Codec || isNull row.Data then
                raise (InvalidDataException "Missing encoded payload")
            else
                JournalPayload.Encoded (row.Codec, row.Version, row.Data)

        { Schema = row.Schema
          Stream = row.Stream
          Seq = row.Seq
          Kind = kind
          Key = Option.ofObj row.Key
          PayloadType = row.PayloadType
          Payload = payload
          Origin = origin }

    /// <summary>Reads a complete recording and rejects missing or reordered entries.</summary>
    let readJournal path =
        let entries =
            File.ReadAllLines path
            |> Array.map (fun line ->
                let row = JsonSerializer.Deserialize<JournalRow> line

                if isNull (box row) then
                    raise (InvalidDataException "Null journal row")

                decode row)

        let mutable next = 1

        for entry in entries do
            if
                entry.Kind = JournalEntryKind.Setup
                && entry.Seq = 0
                && next = 1
            then
                ()
            elif entry.Seq <> next then
                raise (InvalidDataException "Journal sequence is incomplete")
            else
                next <- next + 1

        if
            entries.Length = 0
            || entries[entries.Length - 1].Kind
               <> JournalEntryKind.Flush
        then
            raise (InvalidDataException "Recording must end at a flush")

        entries

    /// <summary>Copies an acknowledged, quiescent store into a new directory.</summary>
    let copyCheckpoint source destination =
        if Directory.Exists destination then
            invalidOp ("Destination already exists: " + destination)

        Directory.CreateDirectory destination |> ignore

        for file in Directory.GetFiles (source, "*", SearchOption.AllDirectories) do
            let target = Path.Combine (destination, Path.GetRelativePath (source, file))

            Directory.CreateDirectory (Path.GetDirectoryName target)
            |> ignore

            File.Copy (file, target, false)

/// <summary>An append-only JSONL journal; each record is flushed before returning to the graph.</summary>
type FileJournal(path: string) =
    let stream =
        new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)

    let writer = new StreamWriter (stream, UTF8Encoding (false), leaveOpen = true)

    interface IInputJournal with
        member _.Stream = "shop"

        member _.Record entry =
            writer.WriteLine (JsonSerializer.Serialize (Recording.encode entry))
            writer.Flush ()
            stream.Flush (true)

    interface IDisposable with
        member _.Dispose() =
            writer.Dispose ()
            stream.Dispose ()
