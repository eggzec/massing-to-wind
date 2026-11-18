using M2W.Geometry;

namespace M2W.Repair.Model;

/// <summary>
/// The solver's own check of a shell it is about to write: every directed edge appears once and its reverse once
/// (closed, manifold, consistently oriented), no zero-area triangle, and positive volume. Used to decide between
/// <c>repaired</c> and <c>partial</c>; it never changes geometry.
/// </summary>
public static class ShellCheck
{
    public sealed record Result(int FreeEdges, int NonManifoldEdges, int MisorientedEdges, int ZeroAreaTriangles, double Volume)
    {
        public bool Ok => FreeEdges == 0 && NonManifoldEdges == 0 && MisorientedEdges == 0 && ZeroAreaTriangles == 0 && Volume > 0;

        public override string ToString() =>
            $"{FreeEdges} free edges, {NonManifoldEdges} non-manifold edges, {MisorientedEdges} misoriented edges, " +
            $"{ZeroAreaTriangles} zero-area triangles, volume {Volume:0.###} m3";
    }

    public static Result Check(IReadOnlyList<Vec3> v, IReadOnlyList<Tri> tris)
    {
        var directed = new Dictionary<(int, int), int>();
        var zero = 0;
        foreach (var t in tris)
        {
            if (Vec3.Cross(v[t.B] - v[t.A], v[t.C] - v[t.A]).LengthSquared <= 1e-24) zero++;
            for (var k = 0; k < 3; k++)
            {
                var a = t[k]; var b = t[(k + 1) % 3];
                directed[(a, b)] = directed.TryGetValue((a, b), out var n) ? n + 1 : 1;
            }
        }
        int free = 0, nonManifold = 0, misoriented = 0;
        foreach (var ((a, b), n) in directed)
        {
            if (a > b && directed.ContainsKey((b, a))) continue; // count each undirected edge once
            directed.TryGetValue((b, a), out var r);
            var total = n + r;
            if (total == 1) free++;
            else if (total > 2) nonManifold++;
            else if (n != 1 || r != 1) misoriented++;
        }
        return new Result(free, nonManifold, misoriented, zero, Volume(v, tris));
    }

    /// <summary>Signed volume, relative to the first vertex for precision.</summary>
    public static double Volume(IReadOnlyList<Vec3> v, IReadOnlyList<Tri> tris)
    {
        if (tris.Count == 0) return 0;
        var c = v[tris[0].A];
        double six = 0;
        foreach (var t in tris)
            six += Vec3.Dot(v[t.A] - c, Vec3.Cross(v[t.B] - c, v[t.C] - c));
        return six / 6.0;
    }
}
