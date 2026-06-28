# TaikoAutoMapper

Generate playable **osu!taiko** beatmaps from arbitrary audio, using a neural model trained on
existing maps.

The tool listens to a song, works out its timing and rhythmic structure, then a model places and
colors the taiko notes — in a chosen mapper's style, or a generic blend of all of them — at a
requested difficulty. The result is an importable `.osz`.

```
audio ──▶ analysis (timing + onset grid) ──▶ model (per-tick tokens) ──▶ beatmap (.osz)
```

## How it works, in one paragraph

Stage 1 (**audio analysis**) decodes the song to mono PCM, detects onsets and tempo, and locks a
beat grid to the music — including automatic offset/drift correction and tempo-change detection.
Stage 2 (**the model**) reads per-tick conditioning features (onset strength, local density,
metrical position, target difficulty, and spectral-band energies) and decodes a token sequence —
one of `{none, don, kat, large-don, large-kat}` per grid tick — autoregressively, conditioned on a
learned mapper style. Stage 3 (**assembly**) targets the requested star rating, applies playability
guards, and writes the `.osu`/`.osz` with the official osu! encoder. Difficulty is always the
**official** value from osu!'s own `TaikoDifficultyCalculator`.

## Quickstart

Requires the **.NET 10 SDK** (pinned in `global.json`).

```pwsh
dotnet build TaikoAutoMapper.slnx
dotnet test  TaikoAutoMapper.slnx
```

Generation is model-based, so there are three steps: build a dataset from maps you own, train, then
generate. (Beatmaps and audio are owned by their creators — keep any training corpus to personal,
offline use.)

```pwsh
# 1. Build a training corpus from a folder of taiko beatmaps (.osu + audio)
dotnet run --project src/TaikoMapper.Cli -- dataset build path/to/maps --out dataset --jobs 8

# 2. Inspect per-author coverage (how many maps each mapper contributes, difficulty spread)
dotnet run --project src/TaikoMapper.Cli -- dataset stats dataset

# 3. Train the model (TorchSharp, CPU)
dotnet run --project src/TaikoMapper.Cli -- train dataset --out model.dat --epochs 12

# 4. Generate a map — in an author's style, or omit --author for a generic style
dotnet run --project src/TaikoMapper.Cli -- generate path/to/song.mp3 --model model.dat --author grumd --difficulty 6 --out map.osz
```

Audio and timing can be used on their own, without a model:

```pwsh
dotnet run --project src/TaikoMapper.Cli -- analyze path/to/song.mp3            # detected timing + rhythm grid
dotnet run --project src/TaikoMapper.Cli -- timing-eval path/to/maps            # score auto-timing vs human timing points
dotnet run --project src/TaikoMapper.Cli -- difficulty map.osu                  # official star rating of a map
```

## Documentation

A from-scratch guide to the whole system lives in [`docs/`](docs/README.md):

1. [Overview](docs/01-overview.md) — the pipeline end to end
2. [Audio analysis](docs/02-audio-analysis.md) — onsets, tempo, automatic timing, the rhythm grid
3. [Representation](docs/03-representation.md) — turning a map into a per-tick token sequence + features
4. [Model & training](docs/04-model-and-training.md) — the network, the corpus, the training loop
5. [Generation](docs/05-generation.md) — decoding, difficulty targeting, playability guards
6. [Command line](docs/06-cli.md) — every command and flag
7. [Architecture](docs/07-architecture.md) — projects, dependencies, design principles
8. [Glossary](docs/glossary.md)

## Layout

```
src/
  TaikoMapper.Domain/    # shared models: TimingSegment, Onset, RhythmGrid, NoteEvent, TaikoChart
  TaikoMapper.Audio/     # decode + onset/tempo analysis + automatic timing detection
  TaikoMapper.Beatmap/   # osu! library wrapper: load/save .osu, official difficulty
  TaikoMapper.Ml/        # the mapper: tokenizer, features, model, training, inference
  TaikoMapper.Cli/       # console host
tests/                   # one test project per src project
```

`Domain` depends on nothing; `Audio` and `Beatmap` depend on `Domain`; `Ml` depends on
`Domain` + `Beatmap` + `Audio`; `Cli` composes everything.

## Key dependencies

- **.NET 10**, central package management (`Directory.Packages.props`).
- **osu!** — `ppy.osu.Game` + `ppy.osu.Game.Rulesets.Taiko` for `.osu` IO and the official difficulty
  calculator (one pinned version is the ground truth for star ratings).
- **Audio** — `FftFlat` (FFT), `NAudio.Core` (WAV + resampling), `NLayer` (MP3), `NVorbis` (Ogg).
- **ML** — `TorchSharp` with the `libtorch-cpu` backend (CPU only; no CUDA/ROCm required).
