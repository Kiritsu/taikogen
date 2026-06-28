# 5. Generation

Generation takes a song, a trained model, an author (or none), a difficulty, and a seed, and writes a
map. It's orchestrated by [`StyleGenerator`](../src/TaikoMapper.Ml/StyleGenerator.cs).

## 5.1 Analyze once, decode many

The audio analysis from [docs/02](02-audio-analysis.md) is the expensive part, and it doesn't depend on
the difficulty. So the generator runs it **once** up front, producing the timing segments, the token
grid, the per-tick features, and the tick times. Everything after — and there can be several decodes
(see difficulty targeting below) — only re-runs the cheap parts.

## 5.2 Autoregressive decoding

With features in hand, the model writes tokens left to right:

1. start with a "start-of-sequence" previous token;
2. at each tick, run one GRU step on `(features[tick], style, previousToken)`, carrying the hidden
   state forward;
3. turn the output logits into probabilities, scaled by a **temperature**, and **sample** one token;
4. feed that token back in as the previous token for the next tick.

Sampling (rather than always taking the most likely token) gives the map variety; **temperature**
controls how adventurous it is — low is conservative and clean, high is more varied. All sampling draws
from a single **seeded** RNG, so a given `(audio, model, author, difficulty, temperature, seed)`
reproduces the exact same map. The **style** is the chosen author's embedding, or the generic centroid
when no author is given.

## 5.3 Playability guards

A model can occasionally emit something physically unplayable. A safety net
([`PlayabilityGuard`](../src/TaikoMapper.Ml/PlayabilityGuard.cs)) cleans the token sequence before it
becomes a chart:

- **Rate cap** — drops any note that lands sooner than a single hand could hit (a minimum gap between
  consecutive notes), so there are no impossible walls.
- **Mono-streak cap** — once one color has run for too long, the next note's color is flipped, so the
  player isn't forced onto one hand indefinitely (hand balance).

The guards are generous — they only intervene on genuine violations, leaving everything within human
limits as the model wrote it.

## 5.4 Hitting the target difficulty

The model only *conditions* on a target difficulty (feature #7) — it doesn't guarantee its output lands
there. Asking for 6★ might produce a 7★ map. So generation **closes the loop**: it binary-searches the
conditioning value until the decoded map's **official** star rating matches the request.

```
search the conditioning c in [0,1] so that  StarRating(decode(c))  ≈  requested difficulty
```

Each step sets the `target_difficulty` feature column to a candidate value, re-decodes (with guards),
builds the chart, and asks osu!'s real `TaikoDifficultyCalculator` for the rating — exactly the value a
player would see. Because the model tends to overshoot, the search settles on a *lower* conditioning
than the nominal one, and the delivered map lands on target. If a difficulty is unreachable for a song,
it returns the closest it can get. The search is the pure, tested
`StyleGenerator.SearchConditioning`; the audio is analyzed once, so only the decode repeats per step.

## 5.5 Writing the file

The decoded chart is handed to [`TaikoBeatmapBuilder`](../src/TaikoMapper.Beatmap/TaikoBeatmapBuilder.cs),
which builds an osu! `Beatmap`: one uninherited timing point per timing segment, and a hit object per
note. Color and finisher-size are encoded the way osu!'s taiko ruleset reads them (via the hit's
sample flags), so the map imports correctly.

The osu! library's own encoder serializes it. By default the output is a **`.osz`** — a zip of the
audio plus the `.osu` — which imports straight into osu!lazer; passing a `.osu` path instead writes the
bare map. Because the star rating was computed during targeting, the achieved value is reported and is
exactly what the game will show.
