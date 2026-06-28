# 7. Architecture

## 7.1 Projects

```
Domain  ◀── Audio
   ▲    ◀── Beatmap
   │          ▲
   └── Ml ────┘──◀── Audio
        ▲
       Cli ──▶ Domain, Audio, Beatmap, Ml
```

- **`TaikoMapper.Domain`** — the shared vocabulary: `TimingSegment`, `Onset`, `QuantizedOnset`,
  `RhythmGrid`, `NoteEvent`, `TaikoChart`, `TaikoColor`, `BeatDivisor`. Depends on nothing.
- **`TaikoMapper.Audio`** — decode + analysis: onsets, tempo, automatic timing, peak-picking,
  quantization, the rhythm grid ([docs/02](02-audio-analysis.md)).
- **`TaikoMapper.Beatmap`** — the osu! library wrapper: load/save `.osu`, build a beatmap from a chart,
  extract a chart from a beatmap, and run the official difficulty calculator.
- **`TaikoMapper.Ml`** — the mapper: tokenizer, features, model, corpus building, training, inference
  ([docs/03](03-representation.md)–[05](05-generation.md)). Depends on Domain, Beatmap, and Audio.
- **`TaikoMapper.Cli`** — the console host that composes everything ([docs/06](06-cli.md)).

The dependency direction is strict: the model project never reaches "sideways," and `Domain` stays
dependency-free, so the core types can be referenced from anywhere without dragging in heavy libraries.

Build configuration is centralized: `Directory.Packages.props` pins every package version, and
`Directory.Build.props` sets shared compiler settings (nullable reference types on, latest language
version). The solution is `TaikoAutoMapper.slnx`.

## 7.2 Design principles

**Reuse the official osu! library; never hand-roll the format or the math.** Parsing, encoding, and
star rating all go through `ppy.osu.*`. The difficulty value is the game's real
`TaikoDifficultyCalculator` output — and one package version is pinned as the ground truth, because a
star rating is only meaningful relative to a fixed calculator version. (The calculator runs *headless*,
with no game host, via a minimal working-beatmap shim.)

**The rhythm grid is the shared currency.** Analysis defines timing and the tick grid once; the feature
extractor, tokenizer, and decoder all speak in ticks. This is what keeps the model's output aligned to
the detected timing, even across drift and tempo changes.

**Determinism.** Given the same inputs and seed, generation is reproducible — all randomness flows
through a single seeded RNG. This makes runs comparable and bugs reproducible.

**Inspectability.** Timing and the grid are dumpable and exportable; the detector is scoreable against
real maps (`timing-eval`); datasets are summarizable (`dataset stats`) and stored as plain `.npy` +
JSON. Correctness here is hard to assert directly, so every intermediate artifact can be eyeballed or
measured.

## 7.3 Testing

Each `src` project has a matching test project. The style of testing follows what each layer can be held
to:

- **Audio** is tested on *synthetic* signals with known answers — click tracks at a known tempo and
  offset, tones in known frequency bands, a deliberately drifting grid, a two-tempo track — so the
  detectors' correctness is asserted exactly rather than eyeballed.
- **Beatmap** is tested by round-tripping: decode → encode → decode is stable, and the headless
  difficulty calculator returns a known rating for a fixture map.
- **Ml** is tested where it can be made deterministic without a trained model: the tokenizer round-trips
  losslessly (including across a tempo change), the difficulty search converges on synthetic
  rating functions, the playability guards cap rate and streaks, and the model's one-tick-at-a-time
  stepping matches a full-sequence forward pass.

## 7.4 Performance

The audio hot paths (decode, FFT/spectral flux, autocorrelation) reuse buffers and avoid per-frame
allocation. Corpus building decodes and analyzes each unique audio file once and runs maps in parallel.
Inference carries the GRU hidden state forward one tick at a time instead of re-reading the prefix each
step. Difficulty targeting analyzes the audio once and only repeats the decode per search iteration.
