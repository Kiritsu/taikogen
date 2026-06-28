# 1. Overview

## The problem

osu!taiko is a rhythm game. A *beatmap* is a timed sequence of drum notes the player hits along to a
song: small or large **don** (red, center) and **kat** (blue, rim) notes, plus drumrolls and spinners.
Making a good map by hand takes hours — you must find the song's timing exactly, then place hundreds
or thousands of notes that match the music and play well at a chosen difficulty.

This tool does it automatically: given an audio file and a target difficulty, it produces an
importable map. The hard parts are (1) reading the music's timing and rhythm precisely, and (2)
deciding what to place — which the tool learns from existing maps rather than hand-coded rules.

## The pipeline

```
                ┌─────────────────────────── analysis ───────────────────────────┐
   audio  ──▶   decode → onsets (spectral flux) → tempo → timing → quantize        ──▶  rhythm grid
                                                                                          │
                ┌──────────────────────────── model ─────────────────────────────┐       │
   grid + audio ──▶  per-tick features ──▶  autoregressive decode (GRU + style)   ──▶  token sequence
                                                                                          │
                ┌──────────────────────── assembly ──────────────────────────────┐       │
   tokens  ──▶  playability guards → difficulty targeting → osu! encoder          ──▶   .osz
```

**Analysis** ([docs/02](02-audio-analysis.md)). Decode the song to mono PCM. Compute an
*onset envelope* (where note-like events occur) via spectral flux. Estimate the tempo and lock a beat
grid to the music, detecting the offset, correcting *drift* (a slightly-wrong BPM that slides out of
sync over a song), and splitting on genuine *tempo changes*. Snap the onsets to the grid. The output
is a **rhythm grid**: timing segments plus a quantized list of onsets.

**The model** ([docs/03](03-representation.md), [docs/04](04-model-and-training.md)). The
grid defines a sequence of *ticks* (48 per beat). For each tick the tool builds a feature vector — the
onset strength there, the local note density, the metrical position, the requested difficulty, and the
spectral makeup of the audio. A small neural network reads these features and emits one token per tick
— `none`, `don`, `kat`, `large-don`, or `large-kat` — left to right, feeding each emitted token back
in so it can learn color patterns. A learned **author embedding** conditions the output on a chosen
mapper's style (or the average of all of them).

**Assembly** ([docs/05](05-generation.md)). The decoded tokens are filtered through
playability guards (no impossible note rates, no runaway one-handed streaks), and the whole decode is
wrapped in a difficulty-targeting loop so the map's **official** star rating lands on the request.
Finally the chart is written with osu!'s own encoder as a `.osz` (zipped audio + `.osu`) that imports
directly into osu!lazer.

## Why these choices

- **Reuse the official osu! library.** Parsing/encoding `.osu` and computing difficulty all go through
  `ppy.osu.*`. The star rating is the game's real value from `TaikoDifficultyCalculator`, never a
  heuristic — and one package version is pinned as the ground truth.
- **The rhythm grid is the shared currency.** Analysis defines it once; everything downstream — the
  feature extractor, the tokenizer, the decoder — speaks in grid ticks. This keeps the model's output
  perfectly aligned to the detected timing.
- **Placement and color are learned, not hand-coded.** The model picks notes and colors from what it
  has seen in real maps, capturing idiom and style far better than fixed rules could.
- **Determinism.** Given the same audio, model, difficulty, and seed, generation is reproducible —
  randomness flows through a single seeded RNG.
- **Inspectability.** Timing and the grid are dumpable (`analyze --dump`, `--dump-grid`,
  `--export`), and the detector can be scored against real maps (`timing-eval`) — correctness here is
  hard to assert directly, so the tool is built to be eyeballed and measured.

## What you need to run it

Generation needs a trained model, and training needs a corpus of existing maps. So the real workflow
is `dataset build → train → generate`. The audio and timing tools (`analyze`, `timing-eval`,
`difficulty`) work on their own with no model. See [docs/06](06-cli.md) for the commands.
