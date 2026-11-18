using M2W.Repair.Model;
using M2W.Repair.Planar;

namespace M2W.Repair.Steps;

/// <summary>Face-level cleanup after welding: collapse repeated indices, drop degenerate and duplicate faces, triangulate.</summary>
public static class FaceCleanup
{
    /// <summary>
    /// Removes consecutive repeated indices from every polygon (welding creates them) and drops polygons left with
    /// fewer than three distinct vertices. Returns the number of polygons dropped.
    /// </summary>
    public static int RemoveDegeneratePolygons(Solid s)
    {
        var dropped = 0;
        var keep = new List<int[]>(s.Polygons.Count);
        foreach (var poly in s.Polygons)
        {
            var list = new List<int>(poly.Length);
            foreach (var i in poly)
                if (list.Count == 0 || list[^1] != i) list.Add(i);
            while (list.Count > 1 && list[0] == list[^1]) list.RemoveAt(list.Count - 1);
            if (list.Distinct().Count() < 3) { dropped++; continue; }
            keep.Add(list.ToArray());
        }
        s.Polygons.Clear();
        s.Polygons.AddRange(keep);
        return dropped;
    }

    /// <summary>
    /// Triangulates every polygon. Triangles from the input are kept as they are, even when their area is zero
    /// (sliver repair is a later step); polygons that cannot be triangulated are reported in the return value.
    /// </summary>
    public static (int polygonsSplit, int failed) Triangulate(Solid s)
    {
        int split = 0, failed = 0;
        var buf = new List<Tri>();
        foreach (var poly in s.Polygons)
        {
            if (poly.Length == 3)
            {
                s.Triangles.Add(new Tri(poly[0], poly[1], poly[2]));
                continue;
            }
            if (Triangulator.TryTriangulate3D(s.Vertices, poly, buf))
            {
                s.Triangles.AddRange(buf);
                split++;
            }
            else
            {
                // Fallback: a fan keeps the surface closed even if the polygon is not simple; counted as a failure.
                for (var k = 1; k + 1 < poly.Length; k++) s.Triangles.Add(new Tri(poly[0], poly[k], poly[k + 1]));
                failed++;
            }
        }
        s.Polygons.Clear();
        return (split, failed);
    }

    /// <summary>
    /// Removes triangles that use the same three vertices as an earlier triangle of the same solid (either
    /// orientation). A closed solid never contains such a pair. Returns the number removed.
    /// </summary>
    public static int RemoveDuplicateTriangles(Solid s)
    {
        var seen = new HashSet<(int, int, int)>();
        var keep = new List<Tri>(s.Triangles.Count);
        foreach (var t in s.Triangles)
            if (seen.Add(t.SortedKey())) keep.Add(t);
        var removed = s.Triangles.Count - keep.Count;
        s.Triangles.Clear();
        s.Triangles.AddRange(keep);
        return removed;
    }

    /// <summary>Drops vertices no triangle uses and renumbers. Returns how many were dropped.</summary>
    public static int Compact(Solid s)
    {
        var map = new int[s.Vertices.Count];
        Array.Fill(map, -1);
        var kept = new List<M2W.Geometry.Vec3>();
        int Map(int i)
        {
            if (map[i] < 0) { map[i] = kept.Count; kept.Add(s.Vertices[i]); }
            return map[i];
        }
        for (var t = 0; t < s.Triangles.Count; t++)
        {
            var tr = s.Triangles[t];
            s.Triangles[t] = new Tri(Map(tr.A), Map(tr.B), Map(tr.C));
        }
        var dropped = s.Vertices.Count - kept.Count;
        s.Vertices.Clear();
        s.Vertices.AddRange(kept);
        return dropped;
    }
}
