namespace M2W.Repair.Planar;

/// <summary>
/// Finds the pairs of axis-aligned boxes that may overlap, with a uniform grid instead of testing all pairs. Used by
/// the planarizer (segments that may cross) and the gap closer (walls that may be closer than minGap).
///
/// The result is a superset of the overlapping pairs (boxes are grown by <c>grow</c> plus a small margin, so a pair
/// at the limit is never lost to rounding); callers apply their own exact test. Pairs come back as (i, j) with
/// i &lt; j, sorted, so a caller that used to loop over all i &lt; j sees the surviving pairs in the same order.
/// </summary>
public static class BoxGrid
{
    public static List<(int i, int j)> Pairs(IReadOnlyList<(double x0, double y0, double x1, double y1)> boxes, double grow)
    {
        var n = boxes.Count;
        var result = new List<(int, int)>();
        if (n < 2) return result;
        var pad = grow + 1e-6;
        var b = new (double x0, double y0, double x1, double y1)[n];
        double sum = 0, minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        for (var k = 0; k < n; k++)
        {
            var (x0, y0, x1, y1) = boxes[k];
            b[k] = (x0 - pad, y0 - pad, x1 + pad, y1 + pad);
            sum += Math.Max(b[k].x1 - b[k].x0, b[k].y1 - b[k].y0);
            minX = Math.Min(minX, b[k].x0); minY = Math.Min(minY, b[k].y0);
            maxX = Math.Max(maxX, b[k].x1); maxY = Math.Max(maxY, b[k].y1);
        }
        // Cell about the size of a typical box; grown while boxes would cover too many cells in all.
        var cell = Math.Max(sum / n, 1e-3);
        var span = Math.Max(maxX - minX, maxY - minY);
        cell = Math.Max(cell, span / 4096);
        while (true)
        {
            long total = 0;
            for (var k = 0; k < n; k++)
                total += (CellOf(b[k].x1, minX, cell) - CellOf(b[k].x0, minX, cell) + 1)
                         * (CellOf(b[k].y1, minY, cell) - CellOf(b[k].y0, minY, cell) + 1);
            if (total <= 16L * n || cell >= span) break;
            cell *= 2;
        }

        var cells = new Dictionary<(long, long), List<int>>();
        var c0 = new (long x, long y)[n];
        for (var k = 0; k < n; k++)
        {
            long cx0 = CellOf(b[k].x0, minX, cell), cx1 = CellOf(b[k].x1, minX, cell);
            long cy0 = CellOf(b[k].y0, minY, cell), cy1 = CellOf(b[k].y1, minY, cell);
            c0[k] = (cx0, cy0);
            for (var cx = cx0; cx <= cx1; cx++)
            for (var cy = cy0; cy <= cy1; cy++)
            {
                if (!cells.TryGetValue((cx, cy), out var list)) cells[(cx, cy)] = list = new List<int>();
                list.Add(k);
            }
        }
        foreach (var ((cx, cy), list) in cells)
            for (var p = 0; p < list.Count; p++)
            for (var q = p + 1; q < list.Count; q++)
            {
                int i = list[p], j = list[q];
                // Each pair once: in the first cell both boxes cover.
                if (Math.Max(c0[i].x, c0[j].x) != cx || Math.Max(c0[i].y, c0[j].y) != cy) continue;
                if (b[i].x0 > b[j].x1 || b[j].x0 > b[i].x1 || b[i].y0 > b[j].y1 || b[j].y0 > b[i].y1) continue;
                result.Add(i < j ? (i, j) : (j, i));
            }
        result.Sort();
        return result;
    }

    private static long CellOf(double v, double origin, double cell) => (long)Math.Floor((v - origin) / cell);
}
