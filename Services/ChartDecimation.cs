namespace SSTLogAnalyser.Services;

public readonly record struct EnvelopeBin(double X, double Min, double Max, double Median, double P05, double P95);

/// <summary>
/// Reduces what the chart has to draw without changing the data shown in tables. Every reduction keeps
/// the extremes of each X bucket, so peaks and limit crossings stay visible.
/// </summary>
public static class ChartDecimation
{
    /// <summary>
    /// Keeps the lowest and highest point of each X bucket, in X order. <paramref name="points"/> must be
    /// sorted by X. Series shorter than twice the bucket count are returned unchanged.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> MinMax(IReadOnlyList<(double X, double Y)> points, int buckets)
    {
        if (buckets < 1 || points.Count <= buckets * 2) return points;
        var minX = points[0].X;
        var span = points[^1].X - minX;
        if (!double.IsFinite(span) || span <= 0) return points;

        var result = new List<(double X, double Y)>(buckets * 2);
        var current = -1;
        int lowIndex = 0, highIndex = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var bucket = Math.Min(buckets - 1, (int)((points[index].X - minX) / span * buckets));
            if (bucket != current)
            {
                if (current >= 0) AddBucket(points, result, lowIndex, highIndex);
                current = bucket;
                lowIndex = highIndex = index;
                continue;
            }
            if (points[index].Y < points[lowIndex].Y) lowIndex = index;
            if (points[index].Y > points[highIndex].Y) highIndex = index;
        }
        AddBucket(points, result, lowIndex, highIndex);
        return result;
    }

    private static void AddBucket(IReadOnlyList<(double X, double Y)> points, List<(double X, double Y)> result, int low, int high)
    {
        if (low == high) { result.Add(points[low]); return; }
        result.Add(points[Math.Min(low, high)]);
        result.Add(points[Math.Max(low, high)]);
    }

    /// <summary>
    /// Min / max / median and the 5th–95th percentile across all curves at each X. Shared sweep points (the usual case) are kept
    /// exactly; only when there are more distinct X values than <paramref name="maxBins"/> are they bucketed.
    /// </summary>
    public static IReadOnlyList<EnvelopeBin> Envelope(IEnumerable<(double X, double Y)> points, int maxBins)
    {
        var finite = points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToArray();
        if (finite.Length == 0) return Array.Empty<EnvelopeBin>();

        var byX = finite.GroupBy(p => p.X).ToArray();
        IEnumerable<(double X, double[] Ys)> bins;
        if (byX.Length <= maxBins)
        {
            bins = byX.Select(g => (g.Key, g.Select(p => p.Y).ToArray()));
        }
        else
        {
            var minX = finite.Min(p => p.X);
            var span = finite.Max(p => p.X) - minX;
            bins = finite
                .GroupBy(p => Math.Min(maxBins - 1, (int)((p.X - minX) / span * maxBins)))
                .Select(g => (minX + (g.Key + 0.5) * span / maxBins, g.Select(p => p.Y).ToArray()));
        }

        return bins
            .Select(bin =>
            {
                Array.Sort(bin.Ys);
                var middle = bin.Ys.Length / 2;
                var median = bin.Ys.Length % 2 == 0 ? (bin.Ys[middle - 1] + bin.Ys[middle]) / 2 : bin.Ys[middle];
                double Percentile(double p) => bin.Ys[(int)Math.Round(p * (bin.Ys.Length - 1))];
                return new EnvelopeBin(bin.X, bin.Ys[0], bin.Ys[^1], median, Percentile(0.05), Percentile(0.95));
            })
            .OrderBy(bin => bin.X)
            .ToArray();
    }

    /// <summary>
    /// Thins a scatter to at most one point per screen-sized grid cell, so tens of thousands of failed
    /// points still render quickly while every failing region remains marked.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> ThinScatter(IReadOnlyList<(double X, double Y)> points, int maxPoints, int xCells, int yCells)
    {
        if (points.Count <= maxPoints) return points;
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        double spanX = maxX - minX, spanY = maxY - minY;
        var seen = new HashSet<long>();
        var result = new List<(double X, double Y)>();
        foreach (var point in points)
        {
            var cx = spanX > 0 ? Math.Min(xCells - 1, (int)((point.X - minX) / spanX * xCells)) : 0;
            var cy = spanY > 0 ? Math.Min(yCells - 1, (int)((point.Y - minY) / spanY * yCells)) : 0;
            if (seen.Add((long)cx * yCells + cy)) result.Add(point);
        }
        return result;
    }
}
