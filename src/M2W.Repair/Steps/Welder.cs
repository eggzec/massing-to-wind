using M2W.Geometry;
using M2W.Repair.Model;

namespace M2W.Repair.Steps;

/// <summary>
/// Merges vertices of one solid that are closer than the weld tolerance. Clusters are found with a uniform grid
/// (cell = tolerance) and union-find, so chains of near points merge into one. Each cluster keeps the position of
/// its lowest-numbered vertex, which keeps the result deterministic and the coordinates exactly as written in the input.
/// </summary>
public static class Welder
{
    /// <summary>Welds in place and returns how many vertices were merged away.</summary>
    public static int Weld(Solid s, double tolerance)
    {
        var n = s.Vertices.Count;
        if (n == 0) return 0;
        var parent = new int[n];
        for (var i = 0; i < n; i++) parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        void Union(int a, int b)
        {
            a = Find(a); b = Find(b);
            if (a == b) return;
            if (a < b) parent[b] = a; else parent[a] = b;
        }

        var tol = Math.Max(tolerance, 0);
        var tol2 = tol * tol;
        if (tol > 0)
        {
            var cell = tol;
            var grid = new Dictionary<(long, long, long), List<int>>();
            (long, long, long) Key(Vec3 v) => ((long)Math.Floor(v.X / cell), (long)Math.Floor(v.Y / cell), (long)Math.Floor(v.Z / cell));
            for (var i = 0; i < n; i++)
            {
                var v = s.Vertices[i];
                var (kx, ky, kz) = Key(v);
                for (var dx = -1L; dx <= 1; dx++)
                for (var dy = -1L; dy <= 1; dy++)
                for (var dz = -1L; dz <= 1; dz++)
                    if (grid.TryGetValue((kx + dx, ky + dy, kz + dz), out var list))
                        foreach (var j in list)
                            if ((s.Vertices[j] - v).LengthSquared <= tol2) Union(i, j);
                var k = (kx, ky, kz);
                if (!grid.TryGetValue(k, out var own)) grid[k] = own = new List<int>();
                own.Add(i);
            }
        }
        else
        {
            var exact = new Dictionary<Vec3, int>();
            for (var i = 0; i < n; i++)
                if (exact.TryGetValue(s.Vertices[i], out var j)) Union(i, j);
                else exact[s.Vertices[i]] = i;
        }

        // Compact: one new vertex per cluster, numbered in order of the cluster's lowest member.
        var newIndex = new int[n];
        var kept = new List<Vec3>();
        var rootNew = new Dictionary<int, int>();
        for (var i = 0; i < n; i++)
        {
            var r = Find(i);
            if (!rootNew.TryGetValue(r, out var ni))
            {
                ni = kept.Count;
                kept.Add(s.Vertices[r]);
                rootNew[r] = ni;
            }
            newIndex[i] = ni;
        }
        var merged = n - kept.Count;
        s.Vertices.Clear();
        s.Vertices.AddRange(kept);
        for (var p = 0; p < s.Polygons.Count; p++)
            s.Polygons[p] = s.Polygons[p].Select(i => newIndex[i]).ToArray();
        for (var t = 0; t < s.Triangles.Count; t++)
        {
            var tr = s.Triangles[t];
            s.Triangles[t] = new Tri(newIndex[tr.A], newIndex[tr.B], newIndex[tr.C]);
        }
        return merged;
    }
}
