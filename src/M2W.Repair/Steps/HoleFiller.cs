using M2W.Geometry;
using M2W.Repair.Model;
using M2W.Repair.Planar;

namespace M2W.Repair.Steps;

/// <summary>
/// Closes open surfaces of a consistently oriented solid. First T-junctions are resolved (a free edge that has a
/// vertex of another free edge lying on it is split there, so a split vertex on a neighbour's edge stops looking like
/// a hole). Then each remaining loop of free edges is filled with triangles, oriented to match the surface.
/// </summary>
public static class HoleFiller
{
    public sealed record Result(int TJunctionsSplit, int HolesFilled, int HolesLeftOpen);

    public static Result Close(Solid s, double tolerance)
    {
        var split = ResolveTJunctions(s, tolerance);
        var (filled, open) = FillLoops(s);
        return new Result(split, filled, open);
    }

    /// <summary>Splits free edges at free-edge vertices within <paramref name="tolerance"/> of them. Returns splits made.</summary>
    public static int ResolveTJunctions(Solid s, double tolerance)
    {
        var total = 0;
        var tol = Math.Max(tolerance, 1e-9);
        for (var iter = 0; iter < 16; iter++)
        {
            var edgeTris = Orienter.EdgeTriangles(s.Triangles);
            var free = edgeTris.Where(kv => kv.Value.Count == 1).Select(kv => kv.Key).ToList();
            if (free.Count == 0) break;
            var candidates = free.SelectMany(e => new[] { e.Item1, e.Item2 }).Distinct().OrderBy(i => i).ToList();
            var touched = new HashSet<int>();
            var replace = new Dictionary<int, List<Tri>>();
            foreach (var (u, w) in free)
            {
                var t = edgeTris[(u, w)][0];
                if (touched.Contains(t)) continue;
                var tr = s.Triangles[t];
                // Directed as in the triangle: a -> b, opposite vertex c.
                int k = 0;
                while (!((tr[k] == u && tr[(k + 1) % 3] == w) || (tr[k] == w && tr[(k + 1) % 3] == u))) k++;
                int a = tr[k], b = tr[(k + 1) % 3], c = tr[(k + 2) % 3];
                var pa = s.Vertices[a]; var pb = s.Vertices[b];
                var d = pb - pa;
                var len2 = d.LengthSquared;
                if (len2 == 0) continue;
                var on = new List<(double t, int v)>();
                foreach (var v in candidates)
                {
                    if (v == a || v == b || v == c) continue;
                    var p = s.Vertices[v];
                    var par = Vec3.Dot(p - pa, d) / len2;
                    if (par <= 0 || par >= 1) continue;
                    if ((pa + d * par - p).LengthSquared > tol * tol) continue;
                    on.Add((par, v));
                }
                if (on.Count == 0) continue;
                on.Sort((x, y) => x.t.CompareTo(y.t));
                var chain = new List<int> { a };
                chain.AddRange(on.Select(o => o.v));
                chain.Add(b);
                var pieces = new List<Tri>();
                for (var i = 0; i + 1 < chain.Count; i++) pieces.Add(new Tri(chain[i], chain[i + 1], c));
                replace[t] = pieces;
                touched.Add(t);
                total += on.Count;
            }
            if (replace.Count == 0) break;
            var next = new List<Tri>(s.Triangles.Count + replace.Count * 2);
            for (var t = 0; t < s.Triangles.Count; t++)
                if (replace.TryGetValue(t, out var pieces)) next.AddRange(pieces);
                else next.Add(s.Triangles[t]);
            s.Triangles.Clear();
            s.Triangles.AddRange(next);
        }
        return total;
    }

    /// <summary>Fills loops of free edges. Returns (filled, left open).</summary>
    public static (int filled, int open) FillLoops(Solid s)
    {
        var edgeTris = Orienter.EdgeTriangles(s.Triangles);
        // Free directed edges as they appear in their triangle; the fill must contain each reversed.
        var outgoing = new SortedDictionary<int, List<int>>(); // reversed edge b -> a, keyed by b
        var freeCount = 0;
        foreach (var t in s.Triangles)
            for (var k = 0; k < 3; k++)
            {
                int a = t[k], b = t[(k + 1) % 3];
                if (edgeTris[Orienter.Key(a, b)].Count != 1) continue;
                if (!outgoing.TryGetValue(b, out var l)) outgoing[b] = l = new List<int>();
                l.Add(a);
                freeCount++;
            }
        if (freeCount == 0) return (0, 0);

        int filled = 0, open = 0;
        var loops = new List<List<int>>();
        while (true)
        {
            var start = outgoing.FirstOrDefault(kv => kv.Value.Count > 0);
            if (start.Value is null) break;
            var loop = new List<int> { start.Key };
            var cur = start.Key;
            var closed = false;
            while (true)
            {
                if (!outgoing.TryGetValue(cur, out var outs) || outs.Count == 0) break;
                var nxt = outs[0];
                outs.RemoveAt(0);
                if (nxt == loop[0]) { closed = true; break; }
                var seenAt = loop.IndexOf(nxt);
                if (seenAt >= 0)
                {
                    // Figure-eight: close the inner loop first, keep walking the outer one.
                    var inner = loop.GetRange(seenAt, loop.Count - seenAt);
                    if (inner.Count >= 3) loops.Add(inner); else open++;
                    loop.RemoveRange(seenAt + 1, loop.Count - seenAt - 1);
                    cur = nxt;
                    continue;
                }
                loop.Add(nxt);
                cur = nxt;
                if (loop.Count > freeCount + 1) break;
            }
            if (closed && loop.Count >= 3) loops.Add(loop);
            else open++;
        }
        var (f, o) = FillGrouped(s, loops);
        return (filled + f, open + o);
    }

    private sealed record LoopInfo(List<int> Loop, Vec3 Normal, double Offset, double MaxDev);

    /// <summary>
    /// Fills loops, treating coplanar nested loops as one polygon with holes (a deleted courtyard ground face leaves an
    /// outer and an inner loop; filling each as a disk would cover the courtyard).
    /// </summary>
    private static (int filled, int open) FillGrouped(Solid s, List<List<int>> loops)
    {
        int filled = 0, open = 0;
        var infos = new List<LoopInfo>();
        foreach (var l in loops)
        {
            var n = Triangulator.Newell(s.Vertices, l);
            if (n.LengthSquared == 0) { open++; continue; }
            n /= n.Length;
            var d = Vec3.Dot(n, s.Vertices[l[0]]);
            var dev = l.Max(i => Math.Abs(Vec3.Dot(n, s.Vertices[i]) - d));
            infos.Add(new LoopInfo(l, n, d, dev));
        }
        var used = new bool[infos.Count];
        for (var i = 0; i < infos.Count; i++)
        {
            if (used[i]) continue;
            var group = new List<int> { i };
            used[i] = true;
            var a = infos[i];
            var planeTol = Math.Max(1e-6, a.MaxDev) + 1e-6;
            for (var j = i + 1; j < infos.Count; j++)
            {
                if (used[j]) continue;
                var b = infos[j];
                if (Math.Abs(Vec3.Dot(a.Normal, b.Normal)) < 1 - 1e-6) continue;
                if (b.Loop.Any(v => Math.Abs(Vec3.Dot(a.Normal, s.Vertices[v]) - a.Offset) > planeTol)) continue;
                group.Add(j);
                used[j] = true;
            }
            var (f, o) = FillGroup(s, group.Select(g => infos[g].Loop).ToList(), a.Normal);
            filled += f; open += o;
        }
        return (filled, open);
    }

    private static (int filled, int open) FillGroup(Solid s, List<List<int>> group, Vec3 n)
    {
        var rings = group.Select(l => Project(s.Vertices, l, n)).ToList();
        var areas = rings.Select(Triangulator.SignedArea).ToList();
        // Nesting depth of each loop: how many other loops contain it.
        var depth = new int[group.Count];
        var parent = new int[group.Count];
        for (var i = 0; i < group.Count; i++)
        {
            parent[i] = -1;
            for (var j = 0; j < group.Count; j++)
            {
                if (i == j || Math.Abs(areas[j]) <= Math.Abs(areas[i])) continue;
                if (!Inside(rings[j], rings[i][0])) continue;
                depth[i]++;
                if (parent[i] < 0 || Math.Abs(areas[j]) < Math.Abs(areas[parent[i]])) parent[i] = j;
            }
        }
        int filled = 0, open = 0;
        var local = new List<Tri>();
        for (var i = 0; i < group.Count; i++)
        {
            if (depth[i] % 2 == 1) continue; // a hole of its parent
            var holes = Enumerable.Range(0, group.Count).Where(h => parent[h] == i && depth[h] == depth[i] + 1).ToList();
            var all = new List<int>(group[i]);
            foreach (var h in holes) all.AddRange(group[h]);
            if (!Triangulator.TryTriangulate(rings[i], holes.Select(h => (IReadOnlyList<P2>)rings[h]).ToList(), local))
            {
                open += 1 + holes.Count;
                continue;
            }
            var reverse = areas[i] < 0; // the fill keeps the outer loop's orientation
            foreach (var t in local)
            {
                var tt = new Tri(all[t.A], all[t.B], all[t.C]);
                s.Triangles.Add(reverse ? tt.Reversed() : tt);
            }
            filled += 1 + holes.Count;
        }
        return (filled, open);
    }

    /// <summary>Projects a loop onto the plane with normal <paramref name="n"/>, right-handed about n.</summary>
    private static List<P2> Project(IReadOnlyList<Vec3> v, List<int> loop, Vec3 n)
    {
        double ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
        var pts = new List<P2>(loop.Count);
        foreach (var i in loop)
        {
            var p = v[i];
            if (az >= ax && az >= ay) pts.Add(n.Z > 0 ? new P2(p.X, p.Y) : new P2(p.Y, p.X));
            else if (ax >= ay) pts.Add(n.X > 0 ? new P2(p.Y, p.Z) : new P2(p.Z, p.Y));
            else pts.Add(n.Y > 0 ? new P2(p.Z, p.X) : new P2(p.X, p.Z));
        }
        return pts;
    }

    private static bool Inside(List<P2> ring, P2 q)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i]; var b = ring[j];
            if ((a.Y > q.Y) != (b.Y > q.Y) && q.X < (b.X - a.X) * (q.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
}
