using TaikoMapper.Domain.Chart;
using TaikoMapper.Ml.Representation;

namespace TaikoMapper.Ml.Inference;

/// <summary>
/// A safety net applied to a decoded token sequence before it becomes a chart: the model can emit
/// physically impossible walls (too many notes per second) or runaway single-colour streaks that force
/// one hand. This caps both — dropping notes that violate a minimum gap, and flipping a colour once a
/// mono streak runs too long — leaving everything the model does within human limits untouched.
/// </summary>
public sealed class PlayabilityGuard
{
    private readonly double _maxNotesPerSecond;
    private readonly int _maxMonoStreak;

    public PlayabilityGuard(double maxNotesPerSecond = 40.0, int maxMonoStreak = 12)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNotesPerSecond);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMonoStreak, 1);
        _maxNotesPerSecond = maxNotesPerSecond;
        _maxMonoStreak = maxMonoStreak;
    }

    /// <summary>Returns a cleaned copy of <paramref name="tokens"/> (aligned to <paramref name="tickTimesMs"/>).</summary>
    public TaikoToken[] Apply(IReadOnlyList<TaikoToken> tokens, IReadOnlyList<double> tickTimesMs)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(tickTimesMs);
        if (tokens.Count != tickTimesMs.Count)
            throw new ArgumentException("tokens and tick times must be the same length.", nameof(tickTimesMs));

        var result = new TaikoToken[tokens.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = tokens[i];

        // 1) Rate cap — drop any note that lands sooner than a single hand can hit.
        var minGapMs = 1000.0 / _maxNotesPerSecond;
        var lastNoteMs = double.NegativeInfinity;
        for (var t = 0; t < result.Length; t++)
        {
            if (result[t] == TaikoToken.None)
                continue;
            var timeMs = tickTimesMs[t];
            if (timeMs - lastNoteMs < minGapMs - 1e-6)
                result[t] = TaikoToken.None;
            else
                lastNoteMs = timeMs;
        }

        // 2) Mono-streak cap — flip the colour once one colour has run for too long (hand balance).
        var streak = 0;
        TaikoColor? lastColor = null;
        for (var t = 0; t < result.Length; t++)
        {
            if (result[t] == TaikoToken.None)
                continue;

            var color = ColorOf(result[t]);
            streak = color == lastColor ? streak + 1 : 1;
            lastColor = color;

            if (streak > _maxMonoStreak)
            {
                result[t] = Flip(result[t]);
                lastColor = ColorOf(result[t]);
                streak = 1;
            }
        }

        return result;
    }

    private static TaikoColor ColorOf(TaikoToken token) =>
        token is TaikoToken.Kat or TaikoToken.LargeKat ? TaikoColor.Kat : TaikoColor.Don;

    private static TaikoToken Flip(TaikoToken token) => token switch
    {
        TaikoToken.Don => TaikoToken.Kat,
        TaikoToken.Kat => TaikoToken.Don,
        TaikoToken.LargeDon => TaikoToken.LargeKat,
        TaikoToken.LargeKat => TaikoToken.LargeDon,
        _ => token,
    };
}
