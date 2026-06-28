# Glossary

**Onset** — the start of a note-like sound (a drum hit, a plucked note, a vocal syllable). What we map
notes to.

**Onset envelope / onset detection function** — a per-frame signal that spikes where onsets occur. Here
it's the spectral flux.

**Spectral flux** — frame-to-frame increase in spectral energy, summed over frequency bins. Rises when
new energy appears, so it marks onsets.

**FFT (Fast Fourier Transform)** — decomposes a short slice of audio into how much energy sits at each
frequency. Computed per frame.

**Frame / hop** — the analysis window (1024 samples) and how far it advances each step (256 samples).
Frames overlap.

**Spectral band** — energy summed over a range of frequencies. Six log-spaced bands (bass → treble)
form a coarse description of timbre over time, used as model features.

**Tempo / BPM** — the beat rate (beats per minute).

**Autocorrelation** — how well a signal lines up with a time-shifted copy of itself; its peak lag gives
the beat period and so the tempo.

**Octave error** — mistaking a tempo for half or double its true value (they correlate too). Resolved
with a tempo prior.

**Offset** — the time of the first beat; equivalently the beat **phase** that best aligns the grid to
the music.

**Drift** — a slightly-wrong BPM accumulating phase error over a song, so the grid slides out of sync.
Corrected by re-anchoring.

**Timing segment / timing point** — a (start time, BPM) region. One osu! uninherited timing point. A
song may have several (from drift re-anchoring or tempo changes).

**Beat divisor / subdivision** — a simple fraction of a beat (1/1, 1/2, 1/3, 1/4, 1/6, 1/8, 1/12,
1/16) that notes sit on.

**Quantization** — snapping a detected onset to the nearest beat subdivision.

**Tick / ticks-per-beat** — the integer grid the map sits on. 48 ticks per beat — the smallest number
that puts every supported subdivision on a whole tick.

**Rhythm grid** — the analysis output: the timing segments plus the quantized onsets.

**Don / kat / finisher** — taiko note types. Don = red (center), kat = blue (rim); each has a large
"finisher" variant. Five tokens: `None`, `Don`, `Kat`, `LargeDon`, `LargeKat`.

**Token grid** — the per-tick timeline spanning all timing segments; converts a global tick index to a
time and back.

**Feature vector** — the 13 numbers the model reads per tick (onset strength, local density, metrical
position, target difficulty, six spectral bands).

**Author embedding** — a learned vector per mapper, capturing their style; added in at every tick.

**Generic style / centroid** — the mean of all author embeddings, used to generate without a specific
author.

**GRU (gated recurrent unit)** — the recurrent layer that gives the model left-to-right context.

**Autoregressive** — generating one token at a time, feeding each emitted token back in as input to the
next step.

**Teacher forcing** — during training, feeding the ground-truth previous token (not the model's own) so
it learns one-step-ahead prediction.

**Temperature** — a knob on sampling randomness. Lower = more conservative/clean, higher = more varied.

**Conditioning (target difficulty)** — the difficulty value fed to the model as a feature. Generation
searches it to hit the requested rating.

**Difficulty targeting** — binary-searching the conditioning so the decoded map's official star rating
matches the request.

**Star rating** — osu!'s official difficulty value, from `TaikoDifficultyCalculator`. Always the real
value, never a heuristic.

**Playability guard** — a post-decode pass that caps the note rate and breaks runaway single-color
streaks so the map stays humanly playable.

**Window / stride** — the fixed length a long map is sliced into for training, and how far each slice
advances.

**`.osu` / `.osz`** — an osu! beatmap file, and a zip of the beatmap plus its audio (imports directly
into osu!lazer).
