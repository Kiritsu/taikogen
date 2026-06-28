# 2. Audio analysis

This step turns a raw audio file into a **rhythm grid**: the song's timing, plus a list of onsets
snapped to a beat grid. It's the foundation everything else stands on — if the timing is wrong, the
map plays out of sync no matter how good the note choices are. All of it lives in
[`TaikoMapper.Audio`](../src/TaikoMapper.Audio).

## 2.1 Decoding to mono PCM

Audio on disk is compressed (MP3, Ogg Vorbis) or packed (WAV). The first step
([`AudioDecoder`](../src/TaikoMapper.Audio/AudioDecoder.cs)) decodes it to a flat array of floating-point
**samples** — amplitude over time — and downmixes to a single (mono) channel resampled to 44.1 kHz.
WAV is read with NAudio, MP3 with NLayer, Ogg with NVorbis.

One subtlety matters for timing: MP3 and Ogg encoders prepend a small amount of silent **decoder
padding**, so a naively decoded compressed file is a few tens of milliseconds late relative to what
osu! plays. The decoder trims a fixed compressed-format delay so detected onsets line up with the
game; WAV and synthetic signals are untouched.

The result is a [`MonoAudio`](../src/TaikoMapper.Audio/MonoAudio.cs) — samples plus a sample rate.

## 2.2 Onsets via spectral flux

An **onset** is the start of a note-like event — a drum hit, a plucked string, a vocal syllable. We
don't need to identify *what* made the sound, only *when* energy suddenly appears.

The signal is cut into short overlapping frames (1024 samples, hop 256). Each frame is run through a
**Fast Fourier Transform**, which decomposes it into how much energy sits at each frequency. Comparing
one frame's spectrum to the previous and summing only the *increases* (half-wave-rectified
**spectral flux**) gives a number per frame that spikes when new energy arrives. That per-frame series
is the **onset detection function**.

[`SpectralFluxAnalyzer`](../src/TaikoMapper.Audio/SpectralFluxAnalyzer.cs) computes this and, in the same
pass, the per-frame energy in six **log-spaced frequency bands** (bass → treble). The bands are the
song's coarse *timbre* over time — what distinguishes a kick drum from a hi-hat from a vocal — and the
model later uses them as features. The output is an
[`OnsetEnvelope`](../src/TaikoMapper.Audio/OnsetEnvelope.cs): the flux series plus the band energies.

## 2.3 Tempo

Music is periodic: beats recur at a roughly fixed spacing. **Autocorrelation** of the onset function —
how well it lines up with a time-shifted copy of itself — peaks at that spacing. The peak's lag gives
the beat period, and so the tempo in BPM.

The catch is **octave ambiguity**: a 120 BPM track also correlates at 60 and 240. To resolve it,
[`TempoEstimator`](../src/TaikoMapper.Audio/TempoEstimator.cs) weights candidates by a broad log-Gaussian
**prior** centered on typical osu! tempos, so a sensible octave wins without hard-coding one value.

## 2.4 Automatic timing

Knowing the tempo isn't enough — we need *where* the beats land (the **offset**), and that has to stay
correct for the whole song. [`TimingAnalyzer`](../src/TaikoMapper.Audio/TimingAnalyzer.cs) produces a list
of **timing segments** (each a start time + BPM, i.e. one osu! timing point) in two tiers.

**Tier 1 — offset and drift.** The precise offset is the beat *phase* that best aligns the onset
function to a grid at the detected tempo (a comb fold over one beat, refined to sub-frame resolution).
But a BPM that's even slightly off accumulates phase error over minutes, so the grid slowly slides out
of sync. The analyzer tracks the phase in sliding windows, smoothed and unwrapped so it doesn't jump,
and emits a **re-anchoring segment** — same BPM, corrected offset — whenever the drift exceeds a
tolerance. Most songs need one segment; a slightly-off tempo gets a few.

**Tier 2 — tempo changes.** Some songs genuinely change tempo. The analyzer estimates the tempo in
large sliding windows and looks for a sustained shift. The trap is that dense sections make a window's
estimate lock onto a simple *metrical ratio* of the true tempo (×½, ×4⁄3, ×2…) — a false alarm. So any
window whose tempo is close to such a ratio of the song's dominant tempo is treated as the same tempo;
only a genuinely different, sustained value opens a new region. The result is conservative by design:
constant-tempo songs stay one tempo.

Manual `--bpm`/`--offset` always override detection — fully automatic timing is the hardest part of the
whole system and will be wrong on some tracks. The [`timing-eval`](06-cli.md) command scores the
detector against a folder of real maps' human timing points, so its accuracy is measurable.

## 2.5 Peak-picking and quantization

The onset function is continuous; we want discrete events.
[`OnsetPeakPicker`](../src/TaikoMapper.Audio/OnsetPeakPicker.cs) finds local maxima that stand out above
an adaptive local threshold — the actual onsets.

Each onset is then **quantized**: snapped to the nearest **beat subdivision** of its active timing
segment. Taiko notes sit on simple fractions of a beat — 1/1, 1/2, 1/3, 1/4, 1/6, 1/8 (and finer 1/12,
1/16). [`RhythmQuantizer`](../src/TaikoMapper.Audio/RhythmQuantizer.cs) records, per onset, the snapped time,
which subdivision it landed on, the signed residual (how far it moved), and whether it was close enough
to count as on-tick. The residual lets later steps distinguish a confident snap from a guess.

## 2.6 The rhythm grid

The output is a [`RhythmGrid`](../src/TaikoMapper.Domain/RhythmGrid.cs): the ordered
[`TimingSegment`](../src/TaikoMapper.Domain/TimingSegment.cs)s plus the quantized onsets. A timing segment
knows how to convert between absolute time and beats (`TimeToBeats` / `BeatsToTime`), which is what lets
every later step speak in *ticks* (48 per beat) rather than milliseconds. Because the grid carries
multiple segments, a song with drift or tempo changes is represented faithfully, and the notes the
model places stay aligned to the music throughout.

[`GridAnalyzer`](../src/TaikoMapper.Audio/GridAnalyzer.cs) ties it together: audio in, rhythm
grid out.
