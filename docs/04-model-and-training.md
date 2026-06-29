# 4. Model & training

This chapter covers the network that decides the notes, the corpus it learns from, and how training
works. Everything is in [`TaikoMapper.Ml`](../src/TaikoMapper.Ml), built on **TorchSharp** with the
CPU `libtorch` backend (no GPU required).

## 4.1 The network

[`TaikoStyleModel`](../src/TaikoMapper.Ml/Model/TaikoStyleModel.cs) predicts, for each grid tick, a probability
over the five tokens. It's small and autoregressive — each tick's prediction depends on the audio
features there, the chosen style, **and the previously emitted token**, so it can learn color patterns
(don/kat runs, kat doublets, color inversions) that the audio alone doesn't determine.

The forward pass, per tick, sums three contributions into one vector and runs them through a recurrent
layer:

```
x = featureProjection(features)        # the 13 audio/metrical features → a hidden vector
  + styleEmbedding(author)             # "who is mapping this" (the style)
  + tokenEmbedding(previousToken)      # what was placed on the tick before
GRU(x)  →  Linear  →  logits over {None, Don, Kat, LargeDon, LargeKat}
```

A **GRU** (gated recurrent unit) carries left-to-right context, so the prediction at a tick is informed
by everything before it. At inference the GRU's hidden state is carried forward one tick at a time
instead of re-reading the whole prefix each step — much cheaper, and provably identical to running the
whole sequence at once (there's a test for that).

**Author style.** Each mapper in the corpus gets a learned **embedding** — a vector indexed by author
id, added in at every tick. Training nudges these vectors so each captures that mapper's tendencies.
To generate *without* a specific author, the model uses the **centroid** — the mean of all author
embeddings (`GenericStyleVector`) — for a generic, author-agnostic style. The id-based path is used for
training; a style-vector path (`RunSequenceWithStyle`) handles both specific and generic styles at
inference.

## 4.2 Building a corpus

[`CorpusBuilder`](../src/TaikoMapper.Ml/Data/CorpusBuilder.cs) turns a folder of beatmaps into training data.
For each taiko `.osu` whose audio resolves, it:

1. parses the map with the osu! library and extracts its notes (and the mapper's name and the official
   star rating);
2. runs the audio analysis (decoding + the FFT/onset pass is done **once per song** and cached, since a
   beatmap set's difficulties share one audio file);
3. quantizes the onsets onto the map's own timing and tokenizes the map's notes;
4. extracts the per-tick features.

It runs across maps in parallel and skips anything unreadable, so a large corpus builds quickly. The
output ([`DatasetWriter`](../src/TaikoMapper.Ml/Data/DatasetWriter.cs)) is one folder per map containing:

- `tokens.npy` — the token per tick (`uint8`, shape `[T]`),
- `features.npy` — the features (`float32`, shape `[T, 13]`),
- `meta.json` — author, bpm, offset, star rating, length.

Plus a dataset-level `manifest.json` (every example) and `authors.json` (author → id). The `.npy`
format is plain NumPy, so the data is inspectable and language-neutral.

`dataset stats` ([docs/06](06-cli.md)) summarizes a built dataset: how many maps each author
contributes and their difficulty spread. Style fidelity needs enough maps **per author** — a mapper
with only a handful is flagged as thin.

## 4.3 The training loop

Maps are long (tens of thousands of ticks), so [`TaikoDataset`](../src/TaikoMapper.Ml/Data/TaikoDataset.cs)
slices them into fixed-length **windows** (with a stride, so windows overlap) — the units the model
trains on.

[`StyleTrainer`](../src/TaikoMapper.Ml/Model/StyleTrainer.cs) trains with standard ingredients:

- **Teacher forcing.** During training the "previous token" fed in is the *ground-truth* previous token
  (a start-of-sequence marker at tick 0), so the model learns one-step-ahead prediction without
  compounding its own errors.
- **Class-weighted loss.** The vast majority of ticks are `None`, so a naive model would just predict
  "no note" everywhere. The cross-entropy loss **down-weights `None`** so placing notes is worth
  learning; the four placed tokens keep equal weight, so their natural proportions (and the rarity of
  finishers) are preserved.
- **Adam** optimizer, a configurable number of epochs, and a **checkpoint saved after every epoch** —
  so a long run can be stopped at any point and the latest model is on disk.

Training writes `model.dat` (the weights) plus a `model.dat.json` sidecar
([`StyleModelIO`](../src/TaikoMapper.Ml/Model/StyleModelIO.cs)) holding the architecture dimensions, the
ticks-per-beat, the feature names, and the author→id map — everything inference needs to reconstruct
and drive the model.

> **Hardware note.** The backend is CPU `libtorch`. CUDA is NVIDIA-only and TorchSharp ships no ROCm
> build, so AMD GPUs aren't used; at this model size CPU training is fine.
