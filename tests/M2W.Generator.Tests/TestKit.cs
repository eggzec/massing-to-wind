using M2W.Geometry;

namespace M2W.Generator.Tests;

/// <summary>Independent mesh measures for the tests (none of this is used by the generator itself).</summary>
internal static class Topo
{
    public static Mesh ToMesh(ObjMesh m)
    {
        var mesh = new Mesh();
        mesh.Vertices.AddRange(m.V);
        mesh.Objects.Add(new MeshObject(m.Name, m.F.Select(f => new Face(f))));
        return mesh;
    }

    public static double Volume(ObjMesh m) => MeshMeasures.SignedVolume(ToMesh(m));

    /// <summary>Free edges (used once), non-manifold edges (used 3+ times) and edges whose two faces run the same way.</summary>
    public static (int Free, int NonManifold, int Misoriented) EdgeStats(IEnumerable<IReadOnlyList<int>> faces)
    {
        var directed = new Dictionary<(int, int), int>();
        foreach (var f in faces)
            for (var k = 0; k < f.Count; k++)
            {
                var e = (f[k], f[(k + 1) % f.Count]);
                if (e.Item1 == e.Item2) continue;
                directed[e] = directed.TryGetValue(e, out var n) ? n + 1 : 1;
            }
        int free = 0, non = 0, mis = 0;
        var seen = new HashSet<(int, int)>();
        foreach (var ((a, b), n) in directed)
        {
            var key = a < b ? (a, b) : (b, a);
            if (!seen.Add(key)) continue;
            directed.TryGetValue((b, a), out var back);
            var total = n + back;
            if (total == 1) free++;
            else if (total > 2) non++;
            else if (n != 1 || back != 1) mis++;
        }
        return (free, non, mis);
    }

    public static (int Free, int NonManifold, int Misoriented) EdgeStats(ObjMesh m) => EdgeStats(m.F);

    public static int Euler(ObjMesh m)
    {
        var verts = m.F.SelectMany(f => f).Distinct().Count();
        var edges = new HashSet<(int, int)>();
        foreach (var f in m.F)
            for (var k = 0; k < f.Length; k++)
            {
                int a = f[k], b = f[(k + 1) % f.Length];
                edges.Add(a < b ? (a, b) : (b, a));
            }
        return verts - edges.Count + m.F.Count;
    }

    public static double AngleDeg(Vec3 a, Vec3 b, Vec3 c)
    {
        var u = b - a;
        var v = c - a;
        var cos = Vec3.Dot(u, v) / (u.Length * v.Length);
        return Math.Acos(Math.Clamp(cos, -1, 1)) * 180 / Math.PI;
    }

    public static double MinTriangleAngle(IReadOnlyList<Vec3> v, IReadOnlyList<int> t) =>
        Math.Min(AngleDeg(v[t[0]], v[t[1]], v[t[2]]), Math.Min(AngleDeg(v[t[1]], v[t[2]], v[t[0]]), AngleDeg(v[t[2]], v[t[0]], v[t[1]])));

    public static double MinEdge(IReadOnlyList<Vec3> v, IEnumerable<IReadOnlyList<int>> faces)
    {
        var m = double.PositiveInfinity;
        foreach (var f in faces)
            for (var k = 0; k < f.Count; k++)
                m = Math.Min(m, Vec3.Distance(v[f[k]], v[f[(k + 1) % f.Count]]));
        return m;
    }

    /// <summary>
    /// Heights where a vertical line through (x, y) crosses the object's non-vertical faces (ray casting with
    /// an even-odd point-in-polygon test in plan). Pairs of crossings bound the solid along the line.
    /// </summary>
    public static List<double> Crossings(ObjMesh m, double x, double y)
    {
        var zs = new List<double>();
        foreach (var f in m.F)
        {
            var n = Normal(m.V, f);
            if (Math.Abs(n.Z) < 1e-9) continue;
            var inside = false;
            for (int i = 0, j = f.Length - 1; i < f.Length; j = i++)
            {
                var pi = m.V[f[i]];
                var pj = m.V[f[j]];
                if ((pi.Y > y) != (pj.Y > y) && x < (pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y) + pi.X) inside = !inside;
            }
            if (!inside) continue;
            var p0 = m.V[f[0]];
            zs.Add(p0.Z - (n.X * (x - p0.X) + n.Y * (y - p0.Y)) / n.Z);
        }
        zs.Sort();
        return zs;
    }

    public static Vec3 Normal(IReadOnlyList<Vec3> v, IReadOnlyList<int> f)
    {
        double x = 0, y = 0, z = 0;
        for (var k = 0; k < f.Count; k++)
        {
            var a = v[f[k]];
            var b = v[f[(k + 1) % f.Count]];
            x += (a.Y - b.Y) * (a.Z + b.Z);
            y += (a.Z - b.Z) * (a.X + b.X);
            z += (a.X - b.X) * (a.Y + b.Y);
        }
        var n = new Vec3(x, y, z);
        return n.Length == 0 ? n : n / n.Length;
    }

    /// <summary>Length of the union of the objects' solids along the vertical line through (x, y).</summary>
    public static double ColumnLength(IEnumerable<ObjMesh> objects, double x, double y)
    {
        var intervals = new List<(double, double)>();
        foreach (var o in objects)
        {
            var zs = Crossings(o, x, y);
            if (zs.Count % 2 != 0) throw new InvalidOperationException($"{o.Name}: odd number of crossings at ({x}, {y})");
            for (var k = 0; k < zs.Count; k += 2) intervals.Add((zs[k], zs[k + 1]));
        }
        intervals.Sort();
        double total = 0, curLo = double.NaN, curHi = double.NaN;
        foreach (var (lo, hi) in intervals)
        {
            if (double.IsNaN(curLo) || lo > curHi) { if (!double.IsNaN(curLo)) total += curHi - curLo; curLo = lo; curHi = hi; }
            else curHi = Math.Max(curHi, hi);
        }
        if (!double.IsNaN(curLo)) total += curHi - curLo;
        return total;
    }
}

/// <summary>The default case set (seed 1), built once for all tests.</summary>
internal static class Cases
{
    private static readonly Lazy<List<BuiltCase>> All = new(() =>
        CasePlan.Make(1).Select(p => CaseBuilder.Build(p.CaseId, p.Seed, p.Recipe)).ToList());

    public static List<BuiltCase> Seed1 => All.Value;
}
