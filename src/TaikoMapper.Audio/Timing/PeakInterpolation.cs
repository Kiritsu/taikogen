namespace TaikoMapper.Audio.Timing;

/// <summary>Sub-sample peak refinement by fitting a parabola to three points.</summary>
internal static class PeakInterpolation
{
    /// <summary>
    /// Given a discrete peak at index <paramref name="peak"/>, returns a refined
    /// fractional index using the standard 3-point parabolic vertex estimate.
    /// Falls back to the integer index at the boundaries or a degenerate fit.
    /// </summary>
    public static double Refine(double[] y, int peak, int lo, int hi)
    {
        if (peak <= lo || peak >= hi)
            return peak;

        var ym = y[peak - 1];
        var y0 = y[peak];
        var yp = y[peak + 1];

        var denom = ym - 2.0 * y0 + yp;
        if (Math.Abs(denom) < double.Epsilon)
            return peak;

        var delta = 0.5 * (ym - yp) / denom;
        if (delta is < -1.0 or > 1.0)
            return peak; // refinement out of the local interval — distrust it

        return peak + delta;
    }
}
