using M2W.Geometry;

namespace M2W.Repair.Model;

/// <summary>
/// Puts output shells in an order that depends only on their geometry, never on the order of the input objects
/// (D-047). Within a shell, vertices are numbered in order of their written position (X, then Y, then Z, rounded to
/// the 6 decimals of the output file); each triangle starts at its smallest vertex number (orientation kept) and
/// triangles are sorted. Shells are sorted by their sorted vertex lists, so the first shell is the one with the
/// lowest corner (smallest X, then Y) of its ground outline. Only the order changes: every shell keeps exactly its
/// vertices and triangles.
/// </summary>
internal static class CanonicalOrder
{
    /// <summary>
    /// Returns the shells in canonical order, each renumbered. <paramref name="newIndex"/>[i] is where input shell i
    /// went.
    /// </summary>
    public static List<OutputShell> Apply(IReadOnlyList<OutputShell> shells, out int[] newIndex)
    {
        var canon = shells.Select(Renumber).ToList();
        var keys = canon.Select(s => s.Vertices.Select(OutputShell.Round6).ToList()).ToList();
        var order = Enumerable.Range(0, canon.Count).ToList();
        order.Sort((a, b) =>
        {
            var c = CompareShells(keys[a], canon[a], keys[b], canon[b]);
            return c != 0 ? c : a.CompareTo(b); // only identical shells tie; their order cannot show in the file
        });
        newIndex = new int[canon.Count];
        for (var k = 0; k < order.Count; k++) newIndex[order[k]] = k;
        return order.Select(i => canon[i]).ToList();
    }

    /// <summary>The shell with vertices in written-position order and triangles sorted.</summary>
    public static OutputShell Renumber(OutputShell s)
    {
        var pos = s.Vertices.Select(OutputShell.Round6).ToList();
        var order = Enumerable.Range(0, pos.Count).ToList();
        // Equal written positions (an invalid shell, reported elsewhere) keep their relative order.
        order.Sort((a, b) =>
        {
            var c = ComparePos(pos[a], pos[b]);
            return c != 0 ? c : a.CompareTo(b);
        });
        var map = new int[pos.Count];
        for (var k = 0; k < order.Count; k++) map[order[k]] = k;
        var tris = s.Triangles.Select(t => new Tri(map[t.A], map[t.B], map[t.C]).Canonical()).ToList();
        tris.Sort((x, y) => x.A != y.A ? x.A.CompareTo(y.A) : x.B != y.B ? x.B.CompareTo(y.B) : x.C.CompareTo(y.C));
        return new OutputShell { Vertices = order.Select(i => s.Vertices[i]).ToList(), Triangles = tris, Blocks = s.Blocks };
    }

    private static int ComparePos(Vec3 p, Vec3 q)
    {
        var c = p.X.CompareTo(q.X);
        if (c != 0) return c;
        c = p.Y.CompareTo(q.Y);
        return c != 0 ? c : p.Z.CompareTo(q.Z);
    }

    private static int CompareShells(List<Vec3> ka, OutputShell a, List<Vec3> kb, OutputShell b)
    {
        for (var i = 0; i < Math.Min(ka.Count, kb.Count); i++)
        {
            var c = ComparePos(ka[i], kb[i]);
            if (c != 0) return c;
        }
        if (ka.Count != kb.Count) return ka.Count.CompareTo(kb.Count);
        for (var i = 0; i < Math.Min(a.Triangles.Count, b.Triangles.Count); i++)
        {
            var (x, y) = (a.Triangles[i], b.Triangles[i]);
            var c = x.A != y.A ? x.A.CompareTo(y.A) : x.B != y.B ? x.B.CompareTo(y.B) : x.C.CompareTo(y.C);
            if (c != 0) return c;
        }
        return a.Triangles.Count.CompareTo(b.Triangles.Count);
    }
}
