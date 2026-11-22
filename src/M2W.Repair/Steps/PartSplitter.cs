using M2W.Repair.Model;

namespace M2W.Repair.Steps;

/// <summary>
/// Splits an input object that holds several separate closed solids into one part per solid, so a model exported as
/// one object is repaired and reported the same as the same model with one object per part (D-041).
///
/// Parts are found on a welded copy: polygons are joined across edges that exactly two polygons use (an edge where
/// two touching solids meet is used by four and does not join them). Where two solids share a whole face (two
/// touching houses of one height share a wall), each copy of the face is cut off from its solid, since all its edges
/// are used four times; such fragments are joined again with the fragment whose open edges they close the other way
/// round (a solid's faces run through each of its edges once in each direction). The object is split only if there
/// are two or more parts and every one is closed (each of its edges used exactly twice by its own polygons); anything
/// else (an open or broken surface) stays one object, as before. Each part keeps the object's original, unwelded vertices
/// and polygons in input order, so the later steps (welding, cleanup, orientation, holes) see it exactly as they
/// would see it as an object of its own.
/// </summary>
public static class PartSplitter
{
    /// <summary>The parts of <paramref name="s"/>, or <paramref name="s"/> itself if it is not split.</summary>
    public static List<Solid> Split(Solid s, double weld)
    {
        var copy = new Solid(s.Name, s.Block);
        copy.Vertices.AddRange(s.Vertices);
        foreach (var p in s.Polygons) copy.Polygons.Add((int[])p.Clone());
        Welder.Weld(copy, weld); // keeps polygon order and count, renumbers vertices

        // Each polygon's edges after welding (repeated indices collapsed); degenerate polygons take no part.
        var n = copy.Polygons.Count;
        var polyEdges = new List<(int, int)>[n];
        var polyDirected = new List<(int, int)>[n];
        var edgeUsers = new Dictionary<(int, int), List<int>>();
        for (var p = 0; p < n; p++)
        {
            var ring = new List<int>();
            foreach (var i in copy.Polygons[p])
                if (ring.Count == 0 || ring[^1] != i) ring.Add(i);
            while (ring.Count > 1 && ring[0] == ring[^1]) ring.RemoveAt(ring.Count - 1);
            polyEdges[p] = new List<(int, int)>();
            polyDirected[p] = new List<(int, int)>();
            if (ring.Distinct().Count() < 3) continue;
            for (var k = 0; k < ring.Count; k++)
            {
                var key = Orienter.Key(ring[k], ring[(k + 1) % ring.Count]);
                polyEdges[p].Add(key);
                polyDirected[p].Add((ring[k], ring[(k + 1) % ring.Count]));
                if (!edgeUsers.TryGetValue(key, out var l)) edgeUsers[key] = l = new List<int>(2);
                l.Add(p);
            }
        }
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        foreach (var users in edgeUsers.Values)
        {
            if (users.Count != 2) continue;
            int a = Find(users[0]), b = Find(users[1]);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }
        JoinFragments(n, polyEdges, polyDirected, Find, parent);

        // Parts in order of their first polygon; degenerate polygons go with the part of the polygon before them.
        var partOf = new int[n];
        var roots = new Dictionary<int, int>();
        for (var p = 0; p < n; p++)
        {
            if (polyEdges[p].Count == 0) { partOf[p] = -1; continue; }
            var r = Find(p);
            if (!roots.TryGetValue(r, out var id)) roots[r] = id = roots.Count;
            partOf[p] = id;
        }
        if (roots.Count < 2) return new List<Solid> { s };

        // Every part must be closed by itself.
        var uses = new Dictionary<(int part, (int, int) edge), int>();
        for (var p = 0; p < n; p++)
            foreach (var e in polyEdges[p])
                uses[(partOf[p], e)] = uses.GetValueOrDefault((partOf[p], e)) + 1;
        if (uses.Values.Any(c => c != 2)) return new List<Solid> { s };

        var last = 0;
        for (var p = 0; p < n; p++)
            if (partOf[p] < 0) partOf[p] = last; else last = partOf[p];
        var width = roots.Count.ToString().Length;
        var parts = new List<Solid>(roots.Count);
        var maps = new List<Dictionary<int, int>>(roots.Count);
        for (var k = 0; k < roots.Count; k++)
        {
            var name = $"{s.Name}#{(k + 1).ToString().PadLeft(width, '0')}";
            parts.Add(new Solid(name, name, s.Group));
            maps.Add(new Dictionary<int, int>());
        }
        for (var p = 0; p < n; p++)
        {
            var part = parts[partOf[p]];
            var map = maps[partOf[p]];
            var poly = s.Polygons[p];
            var local = new int[poly.Length];
            for (var k = 0; k < poly.Length; k++)
            {
                if (!map.TryGetValue(poly[k], out var li))
                {
                    li = part.Vertices.Count;
                    part.Vertices.Add(s.Vertices[poly[k]]);
                    map[poly[k]] = li;
                }
                local[k] = li;
            }
            part.Polygons.Add(local);
        }
        return parts;
    }

    /// <summary>
    /// Joins components that are not closed by themselves: a fragment whose open edges (used once within it) are
    /// exactly another fragment's open edges, run the other way, is merged with it; first come (lowest polygon) first.
    /// </summary>
    private static void JoinFragments(int n, List<(int, int)>[] polyEdges, List<(int, int)>[] polyDirected,
        Func<int, int> find, int[] parent)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            // Open boundary of each component: directed edges whose undirected edge it uses an odd number of times.
            var uses = new Dictionary<(int comp, (int, int) edge), int>();
            var dir = new Dictionary<(int comp, (int, int) edge), (int, int)>();
            for (var p = 0; p < n; p++)
            {
                if (polyEdges[p].Count == 0) continue;
                var c = find(p);
                for (var k = 0; k < polyEdges[p].Count; k++)
                {
                    var key = (c, polyEdges[p][k]);
                    uses[key] = uses.GetValueOrDefault(key) + 1;
                    dir[key] = polyDirected[p][k];
                }
            }
            var open = new SortedDictionary<int, List<(int, int)>>();
            foreach (var ((c, e), u) in uses)
                if (u == 1)
                {
                    if (!open.TryGetValue(c, out var l)) open[c] = l = new List<(int, int)>();
                    l.Add(dir[(c, e)]);
                }
            if (open.Count == 0) return;
            string Key(IEnumerable<(int, int)> edges) =>
                string.Join(";", edges.Select(e => $"{e.Item1},{e.Item2}").OrderBy(x => x, StringComparer.Ordinal));
            var waiting = new Dictionary<string, Queue<int>>(); // reversed boundary -> fragments that it would close
            var merged = false;
            foreach (var (c, edges) in open)
            {
                var own = Key(edges);
                if (waiting.TryGetValue(own, out var q) && q.Count > 0)
                {
                    var other = q.Dequeue();
                    int a = find(c), b = find(other);
                    if (a != b) { parent[Math.Max(a, b)] = Math.Min(a, b); merged = true; }
                    continue;
                }
                var closer = Key(edges.Select(e => (e.Item2, e.Item1)));
                if (!waiting.TryGetValue(closer, out var w)) waiting[closer] = w = new Queue<int>();
                w.Enqueue(c);
            }
            if (!merged) return;
        }
    }
}
