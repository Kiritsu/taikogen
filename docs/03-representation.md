# 3. Representation

The model is a sequence model: it reads a sequence of feature vectors and writes a sequence of tokens.
This chapter is about the two sequences — how a beatmap becomes **tokens**, and what **features** the
model sees at each position. Both live in [`TaikoMapper.Ml`](../src/TaikoMapper.Ml).

## 3.1 The grid as a sequence of ticks

The rhythm grid divides each beat into **48 ticks**
([`MapTokenizer.DefaultTicksPerBeat`](../src/TaikoMapper.Ml/Representation/MapTokenizer.cs)). Why 48? It's the smallest
number that puts every supported subdivision on a whole tick: halves, thirds, quarters, sixths,
eighths, twelfths, sixteenths all divide 48 evenly. So any note the analyzer can place lands exactly on
a tick — no rounding loss.

A song with one tempo is just ticks `0, 1, 2, …`. With multiple timing segments (drift re-anchors or
tempo changes), the timeline is built by [`TokenGrid`](../src/TaikoMapper.Ml/Representation/TokenGrid.cs): each segment
contributes a contiguous run of ticks at its own tempo, laid end to end. `TokenGrid` is the single
place that knows how to convert a global tick index to an absolute time and back, so the tokenizer, the
feature extractor, and the decoder all agree on the timeline — even across a tempo change.

## 3.2 Tokens

The vocabulary ([`TaikoToken`](../src/TaikoMapper.Ml/Representation/TaikoToken.cs)) is five values:

| Token | Meaning |
|-------|---------|
| `None` | no note on this tick |
| `Don` | small red (center) note |
| `Kat` | small blue (rim) note |
| `LargeDon` | large red ("finisher") |
| `LargeKat` | large blue ("finisher") |

A whole map is therefore a flat array of tokens, one per grid tick, mostly `None` with notes sprinkled
on. [`MapTokenizer`](../src/TaikoMapper.Ml/Representation/MapTokenizer.cs) converts between this array and a
[`TaikoChart`](../src/TaikoMapper.Domain/Chart/TaikoChart.cs) (the list of placed notes). Encoding snaps each
note to its tick; decoding emits a note at each non-`None` tick's time. Because every note already sits
on a tick, `decode(encode(chart))` reproduces the chart exactly — including across tempo changes. (Rolls
and spinners aren't in the vocabulary yet; the model places the five hit types.)

The token + grid bundle is a [`TokenizedMap`](../src/TaikoMapper.Ml/Representation/TokenizedMap.cs): the author id, the
timing segments, the ticks-per-beat, the per-segment tick counts, and the tokens.

## 3.3 Features

Tokens are what the model *writes*. **Features** are what it *reads* to decide them — a vector per tick,
aligned one-to-one with the tokens, built by
[`MapFeatureExtractor`](../src/TaikoMapper.Ml/Representation/MapFeatureExtractor.cs). There are 13 columns:

| # | Feature | What it tells the model |
|---|---------|-------------------------|
| 1 | `onset_strength` | how strong an audio onset is at this tick — is there even a sound to map? |
| 2 | `local_density` | how busy the surrounding few seconds are — calm verse vs. dense chorus |
| 3–4 | `tick_in_beat` (sin, cos) | position within the beat — downbeat? off-beat? a 1/4 subdivision? |
| 5–6 | `beat_in_bar` (sin, cos) | position within the bar — phrase structure |
| 7 | `target_difficulty` | the requested star rating (the knob generation turns) |
| 8–13 | `band_0 … band_5` | the six spectral-band energies — the timbre at this moment |

Metrical position is encoded as sine/cosine pairs rather than a raw number so the model sees it as a
smooth cycle (tick 47 is adjacent to tick 0, not far from it). Everything is normalized per map to a
common range, so a quiet song and a loud one look comparable to the network.

The spectral bands are why the model can place *color* sensibly rather than guessing: a bass-heavy kick
and a bright snare have different band signatures, and the model learns which tends to be a don and
which a kat. `target_difficulty` is special — at generation time it's the dial the difficulty-targeting
loop turns (see [docs/05](05-generation.md)).

## 3.4 Putting it together

For a real map (used in training), the corpus builder runs the analysis, tokenizes the map's notes onto
the grid, and extracts the features — producing an aligned `(features, tokens)` pair. For generation,
there are no notes yet: the grid and features come from the audio, and the model produces the tokens.
Either way the representation is identical, which is what makes a model trained on real maps directly
usable to write new ones.
