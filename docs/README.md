# TaikoAutoMapper documentation

A from-scratch guide to how the auto-mapper turns a raw music file into a playable osu!taiko
beatmap. It assumes no background in audio signal processing, music theory, machine learning, or
rhythm games — each idea is built up before it's used.

The system has three steps, and the docs follow them:

| Step | Question it answers | Docs |
|------|---------------------|------|
| **Analysis** | *How fast is the music, where are the beats, and where is every drum-able event?* | [02 — Audio analysis](02-audio-analysis.md) |
| **The model** | *Which note (if any) goes on each grid position, and what color?* | [03 — Representation](03-representation.md), [04 — Model & training](04-model-and-training.md) |
| **Assembly** | *How do we hit a target difficulty, keep the map playable, and write the file?* | [05 — Generation](05-generation.md) |

Read in order:

1. **[Overview](01-overview.md)** — the whole pipeline at a glance, and why it's built this way.
2. **[Audio analysis](02-audio-analysis.md)** — decoding, onset detection (spectral flux), tempo,
   automatic timing (offset, drift, tempo changes), and the rhythm grid.
3. **[Representation](03-representation.md)** — how a beatmap becomes a per-tick token sequence on the
   grid, and the per-tick features the model conditions on.
4. **[Model & training](04-model-and-training.md)** — the network (a per-tick autoregressive GRU with
   author embeddings), building a corpus, the dataset format, and the training loop.
5. **[Generation](05-generation.md)** — autoregressive decoding, difficulty targeting, playability
   guards, and writing the `.osz`.
6. **[Command line](06-cli.md)** — every command and flag.
7. **[Architecture](07-architecture.md)** — the projects, their dependencies, and the cross-cutting
   design principles (determinism, official difficulty, inspectability).

A **[glossary](glossary.md)** collects the recurring terms.

## A note on data

Beatmaps and their audio belong to their creators. Training needs a corpus of existing maps; keep any
such corpus to personal, offline use and don't redistribute it. The repository contains no beatmap or
audio data.
