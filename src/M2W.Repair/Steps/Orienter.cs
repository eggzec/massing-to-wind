using M2W.Repair.Model;

namespace M2W.Repair.Steps;

/// <summary>
/// Makes the triangles of a solid consistently oriented and outward. Orientation is propagated across manifold
/// edges (exactly two triangles) by breadth-first search; then every edge-connected component with negative signed
/// volume is turned inside out.
/// </summary>
public static class Orienter
{
    /// <summary>Makes each component consistent (BFS). Returns how many triangles were reversed.</summary>
    public static int MakeConsistent(Solid s)
    {
        var tris = s.Triangles;
        var edgeTris = EdgeTriangles(tris);
        var visited = new bool[tris.Count];
        var flipped = 0;
        var queue = new Queue<int>();
        for (var seed = 0; seed < tris.Count; seed++)
        {
            if (visited[seed]) continue;
            visited[seed] = true;
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                var tr = tris[t];
                for (var k = 0; k < 3; k++)
                {
                    int a = tr[k], b = tr[(k + 1) % 3];
                    var list = edgeTris[Key(a, b)];
                    if (list.Count != 2) continue; // free or non-manifold: do not propagate
                    var u = list[0] == t ? list[1] : list[0];
                    if (visited[u]) continue;
                    visited[u] = true;
                    if (HasDirected(tris[u], a, b))
                    {
                        tris[u] = tris[u].Reversed();
                        flipped++;
                    }
                    queue.Enqueue(u);
                }
            }
        }
        return flipped;
    }

    /// <summary>Reverses every edge-connected component whose signed volume is negative. Returns triangles reversed.</summary>
    public static int MakeOutward(Solid s)
    {
        var flipped = 0;
        foreach (var comp in Components(s.Triangles))
        {
            var part = comp.Select(i => s.Triangles[i]).ToList();
            if (ShellCheck.Volume(s.Vertices, part) >= 0) continue;
            foreach (var i in comp) s.Triangles[i] = s.Triangles[i].Reversed();
            flipped += comp.Count;
        }
        return flipped;
    }

    public static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    public static bool HasDirected(Tri t, int a, int b) =>
        (t.A == a && t.B == b) || (t.B == a && t.C == b) || (t.C == a && t.A == b);

    public static Dictionary<(int, int), List<int>> EdgeTriangles(IReadOnlyList<Tri> tris)
    {
        var map = new Dictionary<(int, int), List<int>>();
        for (var t = 0; t < tris.Count; t++)
            for (var k = 0; k < 3; k++)
            {
                var key = Key(tris[t][k], tris[t][(k + 1) % 3]);
                if (!map.TryGetValue(key, out var l)) map[key] = l = new List<int>(2);
                l.Add(t);
            }
        return map;
    }

    /// <summary>Edge-connected components (through any shared edge), each a sorted list of triangle numbers.</summary>
    public static List<List<int>> Components(IReadOnlyList<Tri> tris)
    {
        var parent = Enumerable.Range(0, tris.Count).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        foreach (var list in EdgeTriangles(tris).Values)
            for (var i = 1; i < list.Count; i++)
            {
                int a = Find(list[0]), b = Find(list[i]);
                if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
            }
        var groups = new Dictionary<int, List<int>>();
        for (var t = 0; t < tris.Count; t++)
        {
            var r = Find(t);
            if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
            g.Add(t);
        }
        return groups.Values.ToList();
    }
}
