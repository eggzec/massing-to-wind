namespace M2W.Geometry;

/// <summary>Axis-aligned bounding box.</summary>
public readonly record struct Bounds(Vec3 Min, Vec3 Max)
{
    public Vec3 Size => Max - Min;
    public Vec3 Center => (Min + Max) * 0.5;
}

/// <summary>An undirected edge between two vertex indices, stored with <see cref="A"/> &lt; <see cref="B"/>.</summary>
public readonly record struct Edge
{
    public Edge(int a, int b)
    {
        if (a == b) throw new ArgumentException("An edge needs two different vertices.");
        (A, B) = a < b ? (a, b) : (b, a);
    }

    public int A { get; }
    public int B { get; }
}

/// <summary>Basic measures every role needs. Pure functions; nothing here modifies a mesh.</summary>
public static class MeshMeasures
{
    /// <summary>Bounding box of the vertices used by the given faces (all faces if null). Throws if there are none.</summary>
    public static Bounds BoundingBox(Mesh mesh, IEnumerable<Face>? faces = null)
    {
        var used = faces is null ? Enumerable.Range(0, mesh.Vertices.Count) : faces.SelectMany(f => f);
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        var any = false;
        foreach (var i in used)
        {
            var v = mesh.Vertices[i];
            any = true;
            minX = Math.Min(minX, v.X); minY = Math.Min(minY, v.Y); minZ = Math.Min(minZ, v.Z);
            maxX = Math.Max(maxX, v.X); maxY = Math.Max(maxY, v.Y); maxZ = Math.Max(maxZ, v.Z);
        }
        if (!any) throw new InvalidOperationException("No vertices to bound.");
        return new Bounds(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));
    }

    /// <summary>
    /// Area vector of a planar polygon (normal × area), via the sum of fan triangles. Exact for any simple planar
    /// polygon, convex or not. Its direction follows the right-hand rule on the vertex order.
    /// </summary>
    public static Vec3 AreaVector(Mesh mesh, Face face)
    {
        var p0 = mesh.Vertices[face[0]];
        var sum = Vec3.Zero;
        for (var k = 1; k + 1 < face.Count; k++)
            sum += Vec3.Cross(mesh.Vertices[face[k]] - p0, mesh.Vertices[face[k + 1]] - p0);
        return sum * 0.5;
    }

    /// <summary>
    /// Signed volume enclosed by the faces (divergence theorem). Positive when the faces form a closed shell with
    /// outward normals. Computed relative to the bounding-box centre so large map coordinates keep precision.
    /// Meaningful only for closed shells.
    /// </summary>
    public static double SignedVolume(Mesh mesh, IEnumerable<Face>? faces = null)
    {
        var list = (faces ?? mesh.AllFaces).ToList();
        if (list.Count == 0) return 0;
        var c = BoundingBox(mesh, list).Center;
        double six = 0;
        foreach (var f in list)
        {
            var p0 = mesh.Vertices[f[0]] - c;
            for (var k = 1; k + 1 < f.Count; k++)
            {
                var p1 = mesh.Vertices[f[k]] - c;
                var p2 = mesh.Vertices[f[k + 1]] - c;
                six += Vec3.Dot(p0, Vec3.Cross(p1, p2));
            }
        }
        return six / 6.0;
    }

    /// <summary>Total surface area of the faces.</summary>
    public static double SurfaceArea(Mesh mesh, IEnumerable<Face>? faces = null) =>
        (faces ?? mesh.AllFaces).Sum(f => AreaVector(mesh, f).Length);

    /// <summary>
    /// How many faces use each undirected edge, in first-seen order. In a closed manifold shell every count is 2;
    /// a count of 1 is a free (boundary) edge and more than 2 is non-manifold. Consecutive repeated indices are skipped.
    /// </summary>
    public static IReadOnlyDictionary<Edge, int> EdgeUseCounts(IEnumerable<Face> faces)
    {
        var counts = new Dictionary<Edge, int>();
        foreach (var f in faces)
            for (var k = 0; k < f.Count; k++)
            {
                int a = f[k], b = f[(k + 1) % f.Count];
                if (a == b) continue;
                var e = new Edge(a, b);
                counts[e] = counts.TryGetValue(e, out var n) ? n + 1 : 1;
            }
        return counts;
    }
}
