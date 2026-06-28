# 6. Command line

All functionality is exposed by the `TaikoMapper.Cli` console host. During development run it with:

```pwsh
dotnet run --project src/TaikoMapper.Cli -- <command> [args]
```

or build once (`dotnet build -c Release`) and call the produced `taikomapper` executable directly.

The commands fall into two groups: **audio/timing** tools that need no model, and the **model**
workflow (`dataset → train → generate`).

---

## Audio / timing

### `analyze <audio> [options]`

Decode a song and report its detected timing and rhythm grid.

| Option | Effect |
|--------|--------|
| `--bpm <v>` | override tempo detection |
| `--offset <ms>` | override offset detection (pins a single timing point) |
| `--dump` | also print tempo candidates and an onset-envelope summary |
| `--dump-grid` | also print the quantized rhythm grid |
| `--export <file.json>` | write the grid to JSON for inspection |

Timing is automatic: the offset is detected, re-anchored when the grid drifts, and split into multiple
timing points on tempo changes. The report lists each detected timing point.

### `timing-eval <folder> [--jobs <n>]`

Score automatic timing against a folder of maps whose human timing points are the ground truth. Reports,
per map and in aggregate: the offset-phase error, the detected BPM vs the map's BPM, and the number of
detected vs human timing points. Use it to measure and tune the detector on real data. `--jobs` sets the
degree of parallelism.

### `difficulty <map.osu>`

Print the official osu!taiko star rating of an existing beatmap.

---

## Model workflow

### `dataset build <folder> --out <dir> [options]`

Build a training corpus from a folder of taiko beatmaps (`.osu` + audio, searched recursively). Writes
one folder per map (`tokens.npy`, `features.npy`, `meta.json`) plus a `manifest.json` and
`authors.json`.

| Option | Effect |
|--------|--------|
| `--out <dir>` | output directory (default `dataset`) |
| `--ticks <n>` | grid resolution in ticks per beat (default 48) |
| `--jobs <n>` | parallelism (default = CPU cores) |

Non-taiko, unreadable, or audio-missing maps are skipped. Each unique audio file is analyzed once.

### `dataset stats <dir> [--min-maps <n>]`

Summarize a built dataset: total examples and authors, a difficulty histogram, and a per-author table
(map count + difficulty range). Authors with fewer than `--min-maps` (default 8) maps are flagged as
thin for style learning.

### `train <dataset-dir> --out <model.dat> [options]`

Train the author-style model on a built dataset (TorchSharp, CPU). Writes `model.dat` and a
`model.dat.json` sidecar, checkpointing after every epoch.

| Option | Effect |
|--------|--------|
| `--out <model.dat>` | model output path |
| `--epochs <n>` | number of training epochs |
| `--window <w>` / `--stride <s>` | window length and stride for slicing long maps |
| `--batch <b>` | batch size |
| `--seed <n>` | RNG seed |

### `generate <audio> --model <model.dat> --difficulty <stars> [options]`

Generate a map from audio with a trained model. Targets the requested difficulty (by searching the
model's conditioning against the official star rating) and applies the playability guards.

| Option | Effect |
|--------|--------|
| `--model <model.dat>` | **required** — the trained model |
| `--difficulty <stars>` | **required** — target star rating |
| `--author <name>` | generate in this mapper's style; **omit for a generic style** (the average of all authors) |
| `--temp <t>` | sampling temperature (default 0.8) — lower is cleaner, higher is more varied |
| `--seed <n>` | RNG seed (generation is deterministic given the seed) |
| `--out <path>` | output path; `.osz` (default, imports into osu!lazer) or `.osu` for a bare map |
| `--bpm <v>` / `--offset <ms>` | override automatic timing |

Example:

```pwsh
dotnet run --project src/TaikoMapper.Cli -- generate song.mp3 --model model.dat --author grumd --difficulty 6 --out map.osz
dotnet run --project src/TaikoMapper.Cli -- generate song.mp3 --model model.dat --difficulty 5             # generic style
```
