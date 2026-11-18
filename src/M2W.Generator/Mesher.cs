using M2W.Geometry;

namespace M2W.Generator;

public enum FaceStyle
{
    /// <summary>Planar polygons (n-gons with collinear vertices where neighbouring faces meet), like a CAD export.</summary>
    Polygons,

    /// <summary>Triangles only; walls split at every floor and cells kept compact so no triangle is a sliver.</summary>
    Triangles,
}

/// <summary>One emitted OBJ object while it is being built and faulted. Coordinates are in the grid frame.</summary>
public sealed class ObjMesh
{
    public ObjMesh(string name, string block)
    {
        Name = name;
        Block = block;
    }

    public string Name { get; }
    public string Block { get; }
    public List<Vec3> V { get; } = new();
    public List<int[]> F { get; } = new();

    public ObjMesh Clone()
    {
        var m = new ObjMesh(Name, Block);
        m.V.AddRange(V);
        foreach (var f in F) m.F.Add((int[])f.Clone());
        return m;
    }
}

/// <summary>
/// Turns a block (or one floor of it) into a closed, outward-facing, manifold surface. The solid is described by a
/// rectilinear grid and a roof height per cell over a common base; faces are built so that every edge is shared by
/// exactly two faces: walls carry a vertex at every height where another face meets their vertical edges.
/// </summary>
public static class Mesher
{
    public const double MaxCell = 12.0;

    /// <summary>Whole block as one object (<c>B01</c>).</summary>
    public static ObjMesh MeshBlock(Block b, FaceStyle style)
    {
        var (xs, ys) = Grid(b, style);
        var top = new double[xs.Length - 1, ys.Length - 1];
        for (var i = 0; i < xs.Length - 1; i++)
            for (var j = 0; j < ys.Length - 1; j++)
            {
                var t = b.TopAt((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2);
                top[i, j] = t > 0 ? t : double.NaN;
            }
        var levels = Enumerable.Range(0, b.FloorCount + 1).Select(b.FloorZ).ToList();
        var m = new ObjMesh(b.Name, b.Name);
        BuildSolid(m, xs, ys, top, 0, style, levels);
        return m;
    }

    /// <summary>Each floor as its own closed object (<c>B01_F01</c>, <c>B01_F02</c>, …), as stacked-floor exports do.</summary>
    public static List<ObjMesh> MeshFloors(Block b, FaceStyle style)
    {
        var (xs, ys) = Grid(b, style);
        var list = new List<ObjMesh>();
        for (var f = 0; f < b.FloorCount; f++)
        {
            var k = b.TierOfFloor(f);
            double z0 = b.FloorZ(f), z1 = b.FloorZ(f + 1);
            var top = new double[xs.Length - 1, ys.Length - 1];
            for (var i = 0; i < xs.Length - 1; i++)
                for (var j = 0; j < ys.Length - 1; j++)
                    top[i, j] = b.InTier(k, (xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2) ? z1 : double.NaN;
            var m = new ObjMesh($"{b.Name}_F{f + 1:00}", b.Name);
            BuildSolid(m, xs, ys, top, z0, style, new[] { z0, z1 });
            list.Add(m);
        }
        return list;
    }

    /// <summary>The block's grid lines; for triangles, long intervals are split so cells stay compact (aspect ≤ 4).</summary>
    public static (double[] Xs, double[] Ys) Grid(Block b, FaceStyle style)
    {
        var xs = HeightField.SortedUnique(b.Lines(0));
        var ys = HeightField.SortedUnique(b.Lines(1));
        if (style == FaceStyle.Triangles)
        {
            // Intervals under 1 m come from injected faults (tiny steps); they do not set the cell size.
            var minX = MinInterval(xs);
            var minY = MinInterval(ys);
            xs = Refine(xs, Math.Min(MaxCell, 4 * minY));
            ys = Refine(ys, Math.Min(MaxCell, 4 * minX));
        }
        return (xs, ys);
    }

    private static double MinInterval(double[] lines)
    {
        var m = MaxCell;
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            var d = lines[i + 1] - lines[i];
            if (d >= 1.0) m = Math.Min(m, d);
        }
        return m;
    }

    private static double[] Refine(double[] lines, double maxStep)
    {
        var result = new List<double> { lines[0] };
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            double a = lines[i], b = lines[i + 1];
            var n = (int)Math.Ceiling((b - a) / maxStep - 1e-9);
            for (var k = 1; k < n; k++) result.Add(Math.Round(a + (b - a) * k / n, 6));
            result.Add(b);
        }
        return result.ToArray();
    }

    /// <summary>
    /// Adds the faces of the solid {(x, y, z) : cell (x, y) is set and zb ≤ z ≤ top(cell)} to <paramref name="m"/>.
    /// NaN marks an empty cell. <paramref name="levels"/> (sorted, including zb and every roof) are the heights at
    /// which triangle-style walls are split.
    /// </summary>
    public static void BuildSolid(ObjMesh m, double[] xs, double[] ys, double[,] top, double zb, FaceStyle style, IReadOnlyList<double> levels)
    {
        var s = new SolidBuilder(m, xs, ys, top, zb);
        if (style == FaceStyle.Polygons) s.Polygons();
        else s.Triangles(levels);
    }

    private sealed class SolidBuilder
    {
        private readonly ObjMesh _m;
        private readonly double[] _xs, _ys;
        private readonly double[,] _top;
        private readonly double _zb;
        private readonly int _nx, _ny;
        private readonly Dictionary<(int, int, double), int> _index = new();

        public SolidBuilder(ObjMesh m, double[] xs, double[] ys, double[,] top, double zb)
        {
            _m = m;
            _xs = xs;
            _ys = ys;
            _top = top;
            _zb = zb;
            _nx = xs.Length - 1;
            _ny = ys.Length - 1;
        }

        private bool Has(int i, int j) => i >= 0 && j >= 0 && i < _nx && j < _ny && !double.IsNaN(_top[i, j]);

        /// <summary>Roof of a cell, or the base for empty cells.</summary>
        private double Eff(int i, int j) => Has(i, j) ? _top[i, j] : _zb;

        private int V(int i, int j, double z)
        {
            var key = (i, j, z);
            if (!_index.TryGetValue(key, out var idx))
            {
                idx = _m.V.Count;
                _m.V.Add(new Vec3(_xs[i], _ys[j], z));
                _index[key] = idx;
            }
            return idx;
        }

        private void Add(List<int> loop) => _m.F.Add(loop.ToArray());

        private void Tri(int a, int b, int c) => _m.F.Add(new[] { a, b, c });

        private void Quad(int a, int b, int c, int d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        /// <summary>Heights where faces meet the vertical line through grid vertex (i, j).</summary>
        private SortedSet<double> Breaks(int i, int j) => new()
        {
            _zb, Eff(i - 1, j - 1), Eff(i, j - 1), Eff(i - 1, j), Eff(i, j),
        };

        // ------------------------------------------------------------ walls (shared by both styles)

        private readonly record struct WallSeg(bool AlongX, int Line, int Index, double Lo, double Hi, bool Positive);

        /// <summary>Wall segments: on x-lines between cells (i-1, j) and (i, j); on y-lines between (i, j-1) and (i, j).</summary>
        private List<List<WallSeg>> WallRuns()
        {
            var runs = new List<List<WallSeg>>();
            for (var i = 0; i <= _nx; i++)
            {
                List<WallSeg>? run = null;
                for (var j = 0; j < _ny; j++)
                {
                    double l = Eff(i - 1, j), r = Eff(i, j);
                    if (l == r) { run = null; continue; }
                    var seg = new WallSeg(false, i, j, Math.Min(l, r), Math.Max(l, r), l > r);
                    if (run is not null && run[^1].Lo == seg.Lo && run[^1].Hi == seg.Hi && run[^1].Positive == seg.Positive)
                        run.Add(seg);
                    else
                    {
                        run = new List<WallSeg> { seg };
                        runs.Add(run);
                    }
                }
            }
            for (var j = 0; j <= _ny; j++)
            {
                List<WallSeg>? run = null;
                for (var i = 0; i < _nx; i++)
                {
                    double b = Eff(i, j - 1), a = Eff(i, j);
                    if (a == b) { run = null; continue; }
                    var seg = new WallSeg(true, j, i, Math.Min(a, b), Math.Max(a, b), b > a);
                    if (run is not null && run[^1].Lo == seg.Lo && run[^1].Hi == seg.Hi && run[^1].Positive == seg.Positive)
                        run.Add(seg);
                    else
                    {
                        run = new List<WallSeg> { seg };
                        runs.Add(run);
                    }
                }
            }
            return runs;
        }

        /// <summary>Grid vertices along the bottom edge of a wall run, in loop order (so the normal points outward).</summary>
        private static List<(int I, int J)> BottomSequence(List<WallSeg> run)
        {
            var first = run[0];
            var seq = new List<(int, int)>();
            if (!first.AlongX)
            {
                // x-line: vertices (line, j). +x normal runs with increasing y.
                for (var j = run[0].Index; j <= run[^1].Index + 1; j++) seq.Add((first.Line, j));
                if (!first.Positive) seq.Reverse();
            }
            else
            {
                // y-line: vertices (i, line). +y normal runs with decreasing x.
                for (var i = run[0].Index; i <= run[^1].Index + 1; i++) seq.Add((i, first.Line));
                if (first.Positive) seq.Reverse();
            }
            return seq;
        }

        private void PolygonWall(List<WallSeg> run)
        {
            double lo = run[0].Lo, hi = run[0].Hi;
            var seq = BottomSequence(run);
            var loop = new List<int>();
            foreach (var (i, j) in seq) loop.Add(V(i, j, lo));
            var end = seq[^1];
            foreach (var z in Breaks(end.I, end.J)) if (z > lo && z < hi) loop.Add(V(end.I, end.J, z));
            for (var k = seq.Count - 1; k >= 0; k--) loop.Add(V(seq[k].I, seq[k].J, hi));
            var start = seq[0];
            foreach (var z in Breaks(start.I, start.J).Reverse()) if (z > lo && z < hi) loop.Add(V(start.I, start.J, z));
            Add(loop);
        }

        private void TriangleWall(List<WallSeg> run, IReadOnlyList<double> levels)
        {
            double lo = run[0].Lo, hi = run[0].Hi;
            var seq = BottomSequence(run);
            var bands = levels.Where(z => z >= lo && z <= hi).ToList();
            for (var k = 0; k + 1 < seq.Count; k++)
            {
                var (ai, aj) = seq[k];
                var (bi, bj) = seq[k + 1];
                for (var t = 0; t + 1 < bands.Count; t++)
                {
                    double z0 = bands[t], z1 = bands[t + 1];
                    Quad(V(ai, aj, z0), V(bi, bj, z0), V(bi, bj, z1), V(ai, aj, z1));
                }
            }
        }

        // ------------------------------------------------------------ polygons

        public void Polygons()
        {
            // Roofs: one polygon per region of equal height; regions with holes or pinches fall back to row strips.
            var seen = new bool[_nx, _ny];
            for (var i = 0; i < _nx; i++)
                for (var j = 0; j < _ny; j++)
                {
                    if (!Has(i, j) || seen[i, j]) continue;
                    var h = _top[i, j];
                    var region = Flood(i, j, (a, b) => Has(a, b) && _top[a, b] == h, seen);
                    Cap(region, h, up: true);
                }

            // Base: every cell.
            var all = new HashSet<(int, int)>();
            for (var i = 0; i < _nx; i++)
                for (var j = 0; j < _ny; j++)
                    if (Has(i, j)) all.Add((i, j));
            Cap(all, _zb, up: false);

            foreach (var run in WallRuns()) PolygonWall(run);
        }

        private HashSet<(int, int)> Flood(int i0, int j0, Func<int, int, bool> member, bool[,] seen)
        {
            var region = new HashSet<(int, int)>();
            var stack = new Stack<(int, int)>();
            stack.Push((i0, j0));
            seen[i0, j0] = true;
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                region.Add((a, b));
                foreach (var (c, d) in new[] { (a + 1, b), (a - 1, b), (a, b + 1), (a, b - 1) })
                    if (c >= 0 && d >= 0 && c < _nx && d < _ny && !seen[c, d] && member(c, d))
                    {
                        seen[c, d] = true;
                        stack.Push((c, d));
                    }
            }
            return region;
        }

        private void Cap(HashSet<(int, int)> region, double z, bool up)
        {
            var loop = TraceSingleLoop(region);
            if (loop is not null)
            {
                var ids = loop.Select(p => V(p.I, p.J, z)).ToList();
                if (!up) ids.Reverse();
                Add(ids);
                return;
            }
            // Row strips (counter-clockwise from above), with every grid vertex on their edges.
            for (var j = 0; j < _ny; j++)
            {
                var i = 0;
                while (i < _nx)
                {
                    if (!region.Contains((i, j))) { i++; continue; }
                    var i0 = i;
                    while (i < _nx && region.Contains((i, j))) i++;
                    var ids = new List<int>();
                    for (var k = i0; k <= i; k++) ids.Add(V(k, j, z));
                    for (var k = i; k >= i0; k--) ids.Add(V(k, j + 1, z));
                    if (!up) ids.Reverse();
                    Add(ids);
                }
            }
        }

        /// <summary>The boundary of a region as one counter-clockwise loop of grid vertices, or null if the region has
        /// a hole or touches itself at a corner.</summary>
        private static List<(int I, int J)>? TraceSingleLoop(HashSet<(int, int)> region)
        {
            var next = new Dictionary<(int, int), (int, int)>();
            bool AddEdge((int, int) a, (int, int) b)
            {
                if (next.ContainsKey(a)) return false; // pinch: two boundary edges leave one vertex
                next[a] = b;
                return true;
            }
            foreach (var (i, j) in region.OrderBy(c => c.Item2).ThenBy(c => c.Item1))
            {
                if (!region.Contains((i, j - 1)) && !AddEdge((i, j), (i + 1, j))) return null;
                if (!region.Contains((i + 1, j)) && !AddEdge((i + 1, j), (i + 1, j + 1))) return null;
                if (!region.Contains((i, j + 1)) && !AddEdge((i + 1, j + 1), (i, j + 1))) return null;
                if (!region.Contains((i - 1, j)) && !AddEdge((i, j + 1), (i, j))) return null;
            }
            var start = next.Keys.OrderBy(k => k.Item2).ThenBy(k => k.Item1).First();
            var loop = new List<(int, int)>();
            var cur = start;
            do
            {
                loop.Add(cur);
                cur = next[cur];
            } while (cur != start && loop.Count <= next.Count);
            return loop.Count == next.Count ? loop : null;
        }

        // ------------------------------------------------------------ triangles

        public void Triangles(IReadOnlyList<double> levels)
        {
            for (var j = 0; j < _ny; j++)
                for (var i = 0; i < _nx; i++)
                {
                    if (!Has(i, j)) continue;
                    var h = _top[i, j];
                    Quad(V(i, j, h), V(i + 1, j, h), V(i + 1, j + 1, h), V(i, j + 1, h));
                    Quad(V(i, j, _zb), V(i, j + 1, _zb), V(i + 1, j + 1, _zb), V(i + 1, j, _zb));
                }
            foreach (var run in WallRuns()) TriangleWall(run, levels);
        }
    }
}
