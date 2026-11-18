namespace M2W.Checker;

/// <summary>Minimum-cost assignment (Hungarian method, O(n³)) on a rectangular cost matrix. Deterministic.</summary>
public static class Assignment
{
    /// <summary>
    /// Returns, for each row, the column it is assigned to, or −1 if the row is left unassigned (more rows than
    /// columns). Each column is used at most once. Minimises the total cost of assigned pairs.
    /// </summary>
    public static int[] Solve(double[,] cost)
    {
        int rows = cost.GetLength(0), cols = cost.GetLength(1);
        var n = Math.Max(rows, cols);
        var result = Enumerable.Repeat(-1, rows).ToArray();
        if (n == 0) return result;

        // Square matrix padded with zeros; 1-based arrays as in the classic e-maxx formulation.
        var a = new double[n + 1, n + 1];
        for (var i = 0; i < rows; i++)
            for (var j = 0; j < cols; j++)
                a[i + 1, j + 1] = cost[i, j];

        var u = new double[n + 1];
        var v = new double[n + 1];
        var p = new int[n + 1];
        var way = new int[n + 1];
        for (var i = 1; i <= n; i++)
        {
            p[0] = i;
            var j0 = 0;
            var minv = Enumerable.Repeat(double.PositiveInfinity, n + 1).ToArray();
            var used = new bool[n + 1];
            do
            {
                used[j0] = true;
                int i0 = p[j0], j1 = 0;
                var delta = double.PositiveInfinity;
                for (var j = 1; j <= n; j++)
                {
                    if (used[j]) continue;
                    var cur = a[i0, j] - u[i0] - v[j];
                    if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                    if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                }
                for (var j = 0; j <= n; j++)
                {
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                    else minv[j] -= delta;
                }
                j0 = j1;
            } while (p[j0] != 0);
            do
            {
                var j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }

        for (var j = 1; j <= n; j++)
            if (p[j] >= 1 && p[j] <= rows && j <= cols)
                result[p[j] - 1] = j - 1;
        return result;
    }
}
