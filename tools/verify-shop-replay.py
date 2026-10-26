"""Exercise untraced production capture and traced replay in separate processes."""

import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src/Ranvier.Examples.ShopReplay"


def run(arguments, succeeds=True):
    result = subprocess.run(["dotnet", *map(str, arguments)], cwd=ROOT, text=True, capture_output=True)
    if (result.returncode == 0) != succeeds:
        raise AssertionError(f"Unexpected exit {result.returncode}: {arguments}\n{result.stdout}\n{result.stderr}")
    return result.stdout + result.stderr


def app(configuration, command, directory, succeeds=True):
    return run([PROJECT / f"bin/{configuration}/net10.0/Ranvier.Examples.ShopReplay.dll", command, directory], succeeds)


def fingerprint(directory):
    return {str(p.relative_to(directory)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in directory.rglob("*") if p.is_file()}


def main():
    run(["build", PROJECT, "-c", "Release", "-p:RanvierTrace=false"])
    run(["build", PROJECT, "-c", "Debug", "-p:RanvierTrace=true"])
    with tempfile.TemporaryDirectory(prefix="ranvier-shop-tests-") as temporary:
        root = Path(temporary)
        capture = root / "incident"
        production = app("Release", "capture", capture)
        assert "production tracing=false" in production
        checkpoint_before = fingerprint(capture / "checkpoint")
        traces = []
        for _ in range(3):
            replay = app("Debug", "replay", capture)
            assert "5 observations match; live calls=0; tracing=true" in replay
            traces.append((capture / "replay-trace.jsonl").read_bytes())
            assert "shipping" in (capture / "replay-why.txt").read_text()
            assert "checkout" in (capture / "replay-snapshot.txt").read_text()
        assert traces[0] == traces[1] == traces[2], "Trace changed between fresh replay processes"
        assert fingerprint(capture / "checkpoint") == checkpoint_before, "Replay changed checkpoint"

        failure = root / "failure"
        app("Release", "capture-failure", failure)
        assert "5 observations match; live calls=0" in app("Debug", "replay", failure)

        assert "untraced Release" in app("Debug", "capture", root / "wrong-mode", False)
        assert "requires Debug" in app("Release", "replay", capture, False)
        app("Release", "capture", capture, False)

        for name in ["truncated", "sequence", "codec", "observation", "checkpoint"]:
            damaged = root / name
            shutil.copytree(capture, damaged)
            journal_path = damaged / "journal.jsonl"
            rows = [json.loads(line) for line in journal_path.read_text().splitlines()]
            if name == "truncated":
                rows.pop()
            elif name == "sequence":
                rows[-2]["Seq"] += 10
            elif name == "codec":
                next(row for row in reversed(rows) if row["Kind"] == "Settle")["Codec"] = "incompatible"
            elif name == "observation":
                manifest = damaged / "capture.json"
                data = json.loads(manifest.read_text())
                data["Observations"][-1]["Total"] += 1
                manifest.write_text(json.dumps(data))
            elif name == "checkpoint":
                shutil.rmtree(damaged / "checkpoint")
            journal_path.write_text("".join(json.dumps(row) + "\n" for row in rows))
            app("Debug", "replay", damaged, False)

    print("PASS: Release capture; 3 identical traced replays; async failure; immutable checkpoint; 8 rejection checks")


if __name__ == "__main__":
    main()
