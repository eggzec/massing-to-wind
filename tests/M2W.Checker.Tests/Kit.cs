using M2W.Checker;
using M2W.Geometry;
using M2W.Geometry.Models;

namespace M2W.Checker.Tests;

/// <summary>Hand-built meshes and in-memory case files for the checker tests.</summary>
internal static class Kit
{
    public static string RepoRoot { get; } = FindRoot();
    public static string CasesDir => Path.Combine(RepoRoot, "cases");
    public static string Example0007 => Path.Combine(CasesDir, "_example", "case_0007");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGREEMENT.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (AGREEMENT.md) not found.");
    }

    // ------------------------------------------------------------------ building meshes

    /// <summary>Adds an axis-aligned box as 12 outward triangles to object <paramref name="o"/>.</summary>
    public static void AddBox(Mesh m, MeshObject o, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var b = m.Vertices.Count;
        m.Vertices.AddRange(new[]
        {
            new Vec3(x0, y0, z0), new Vec3(x1, y0, z0), new Vec3(x1, y1, z0), new Vec3(x0, y1, z0),
            new Vec3(x0, y0, z1), new Vec3(x1, y0, z1), new Vec3(x1, y1, z1), new Vec3(x0, y1, z1),
        });
        int[][] quads =
        {
            new[] { 0, 3, 2, 1 }, // bottom (down)
            new[] { 4, 5, 6, 7 }, // top (up)
            new[] { 0, 1, 5, 4 }, // south
            new[] { 1, 2, 6, 5 }, // east
            new[] { 2, 3, 7, 6 }, // north
            new[] { 3, 0, 4, 7 }, // west
        };
        foreach (var q in quads)
        {
            o.Faces.Add(new Face(b + q[0], b + q[1], b + q[2]));
            o.Faces.Add(new Face(b + q[0], b + q[2], b + q[3]));
        }
    }

    /// <summary>
    /// A closed box whose six faces are each split into n × n quads (2 triangles each), welded: many coplanar
    /// triangles sharing vertices and edges, the hardest case for false self-intersections.
    /// </summary>
    public static void AddGridBox(Mesh m, MeshObject o, Vec3 min, Vec3 max, int n)
    {
        var index = new Dictionary<(int, int, int), int>();
        int V(int i, int j, int k)
        {
            if (!index.TryGetValue((i, j, k), out var id))
            {
                id = m.AddVertex(new Vec3(
                    min.X + (max.X - min.X) * i / n, min.Y + (max.Y - min.Y) * j / n, min.Z + (max.Z - min.Z) * k / n));
                index[(i, j, k)] = id;
            }
            return id;
        }
        void Quad(int a, int b, int c, int d)
        {
            o.Faces.Add(new Face(a, b, c));
            o.Faces.Add(new Face(a, c, d));
        }
        for (var u = 0; u < n; u++)
            for (var w = 0; w < n; w++)
            {
                Quad(V(u, w, 0), V(u, w + 1, 0), V(u + 1, w + 1, 0), V(u + 1, w, 0)); // bottom (down)
                Quad(V(u, w, n), V(u + 1, w, n), V(u + 1, w + 1, n), V(u, w + 1, n)); // top (up)
                Quad(V(u, 0, w), V(u + 1, 0, w), V(u + 1, 0, w + 1), V(u, 0, w + 1)); // south (-y)
                Quad(V(u, n, w), V(u, n, w + 1), V(u + 1, n, w + 1), V(u + 1, n, w)); // north (+y)
                Quad(V(0, u, w), V(0, u, w + 1), V(0, u + 1, w + 1), V(0, u + 1, w)); // west (-x)
                Quad(V(n, u, w), V(n, u + 1, w), V(n, u + 1, w + 1), V(n, u, w + 1)); // east (+x)
            }
    }

    public static Mesh Boxes(params (double x0, double y0, double z0, double x1, double y1, double z1)[] boxes)
    {
        var m = new Mesh();
        for (var i = 0; i < boxes.Length; i++)
        {
            var (x0, y0, z0, x1, y1, z1) = boxes[i];
            AddBox(m, m.AddObject($"shell_{i:000}"), x0, y0, z0, x1, y1, z1);
        }
        return m;
    }

    /// <summary>
    /// Adds a vertical prism over a simple footprint (counter-clockwise in plan) from z0 to z1, as polygons
    /// (bottom, top, one quad per wall), like a generator would emit a whole block.
    /// </summary>
    public static void AddPrismPolygons(Mesh m, MeshObject o, IReadOnlyList<(double X, double Y)> plan, double z0, double z1)
    {
        var b = m.Vertices.Count;
        var n = plan.Count;
        foreach (var (x, y) in plan) m.Vertices.Add(new Vec3(x, y, z0));
        foreach (var (x, y) in plan) m.Vertices.Add(new Vec3(x, y, z1));
        o.Faces.Add(new Face(Enumerable.Range(0, n).Reverse().Select(i => b + i)));
        o.Faces.Add(new Face(Enumerable.Range(0, n).Select(i => b + n + i)));
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            o.Faces.Add(new Face(b + i, b + j, b + n + j, b + n + i));
        }
    }

    /// <summary>
    /// A courtyard block (genus 1) between two rectangles: outer and inner (x0, y0, x1, y1), all quads,
    /// with the roof and ground rings split into four trapezoids so no polygon has a hole.
    /// </summary>
    public static void AddCourtyardPolygons(Mesh m, MeshObject o, (double, double, double, double) outer, (double, double, double, double) inner, double z0, double z1)
    {
        var (ox0, oy0, ox1, oy1) = outer;
        var (ix0, iy0, ix1, iy1) = inner;
        (double, double)[] op = { (ox0, oy0), (ox1, oy0), (ox1, oy1), (ox0, oy1) };
        (double, double)[] ip = { (ix0, iy0), (ix1, iy0), (ix1, iy1), (ix0, iy1) };
        var b = m.Vertices.Count;
        int O(int k, int level) => b + level * 8 + k % 4;      // outer ring vertex k at level 0 (z0) or 1 (z1)
        int I(int k, int level) => b + level * 8 + 4 + k % 4;  // inner ring
        foreach (var z in new[] { z0, z1 })
        {
            foreach (var (x, y) in op) m.Vertices.Add(new Vec3(x, y, z));
            foreach (var (x, y) in ip) m.Vertices.Add(new Vec3(x, y, z));
        }
        for (var k = 0; k < 4; k++)
        {
            o.Faces.Add(new Face(O(k, 1), O(k + 1, 1), I(k + 1, 1), I(k, 1)));  // roof trapezoid (up)
            o.Faces.Add(new Face(O(k, 0), I(k, 0), I(k + 1, 0), O(k + 1, 0)));  // ground trapezoid (down)
            o.Faces.Add(new Face(O(k, 0), O(k + 1, 0), O(k + 1, 1), O(k, 1)));  // outer wall (out)
            o.Faces.Add(new Face(I(k + 1, 0), I(k, 0), I(k, 1), I(k + 1, 1)));  // courtyard wall (into the courtyard)
        }
    }

    /// <summary>
    /// A podium with a set-back tower on it, as one closed block (genus 0): podium rectangle up to zp, tower
    /// rectangle (strictly inside it) up to zt. The podium roof ring is split into four trapezoids.
    /// </summary>
    public static void AddPodiumTowerPolygons(Mesh m, MeshObject o, (double, double, double, double) podium, (double, double, double, double) tower, double zp, double zt)
    {
        var (px0, py0, px1, py1) = podium;
        var (tx0, ty0, tx1, ty1) = tower;
        (double, double)[] pp = { (px0, py0), (px1, py0), (px1, py1), (px0, py1) };
        (double, double)[] tp = { (tx0, ty0), (tx1, ty0), (tx1, ty1), (tx0, ty1) };
        var b = m.Vertices.Count;
        foreach (var (x, y) in pp) m.Vertices.Add(new Vec3(x, y, 0));   // b+0..3
        foreach (var (x, y) in pp) m.Vertices.Add(new Vec3(x, y, zp));  // b+4..7
        foreach (var (x, y) in tp) m.Vertices.Add(new Vec3(x, y, zp));  // b+8..11
        foreach (var (x, y) in tp) m.Vertices.Add(new Vec3(x, y, zt));  // b+12..15
        int P0(int k) => b + k % 4;
        int P1(int k) => b + 4 + k % 4;
        int T0(int k) => b + 8 + k % 4;
        int T1(int k) => b + 12 + k % 4;
        o.Faces.Add(new Face(P0(3), P0(2), P0(1), P0(0)));        // ground (down)
        o.Faces.Add(new Face(T1(0), T1(1), T1(2), T1(3)));        // tower roof (up)
        for (var k = 0; k < 4; k++)
        {
            o.Faces.Add(new Face(P0(k), P0(k + 1), P1(k + 1), P1(k))); // podium wall
            o.Faces.Add(new Face(P1(k), P1(k + 1), T0(k + 1), T0(k))); // podium roof trapezoid (up)
            o.Faces.Add(new Face(T0(k), T0(k + 1), T1(k + 1), T1(k))); // tower wall
        }
    }

    public static Mesh Single(string objectName, Action<Mesh, MeshObject> build)
    {
        var m = new Mesh();
        build(m, m.AddObject(objectName));
        return m;
    }

    /// <summary>A copy of <paramref name="m"/> with every vertex moved by <paramref name="d"/>.</summary>
    public static Mesh Moved(Mesh m, Vec3 d) =>
        new(m.Vertices.Select(v => v + d), m.Objects.Select(o => new MeshObject(o.Name, o.Faces)));

    // ------------------------------------------------------------------ "zero repair"

    /// <summary>
    /// The least a correct solver must do to a clean (fault-free) input: subtract the origin, triangulate the
    /// planar polygons (ear clipping, no new vertices) and name one shell per block (objects grouped by the
    /// block part of their name, in first-seen order). No geometry is changed. Used to prove the checker passes
    /// clean controls with zero repair.
    /// </summary>
    public static Mesh ZeroRepair(Mesh input, Vec3 origin)
    {
        var outMesh = new Mesh(input.Vertices.Select(v => v - origin), Array.Empty<MeshObject>());
        var groups = new List<(string Block, List<Face> Faces)>();
        foreach (var obj in input.Objects)
        {
            var block = BlockLocator.BlockOf(obj.Name) ?? obj.Name;
            var g = groups.FindIndex(x => x.Block == block);
            if (g < 0) { groups.Add((block, new List<Face>())); g = groups.Count - 1; }
            foreach (var f in obj.Faces) groups[g].Faces.AddRange(Triangulate(outMesh.Vertices, f));
        }
        for (var i = 0; i < groups.Count; i++) outMesh.Objects.Add(new MeshObject($"shell_{i:000}", groups[i].Faces));
        return outMesh;
    }

    /// <summary>Ear clipping of a planar simple polygon, keeping its orientation. Collinear vertices are kept.</summary>
    public static IEnumerable<Face> Triangulate(IReadOnlyList<Vec3> v, Face f)
    {
        if (f.IsTriangle) { yield return f; yield break; }
        // Newell normal; project onto the plane of the two other axes.
        var n = Vec3.Zero;
        for (var k = 0; k < f.Count; k++)
        {
            var a = v[f[k]];
            var b = v[f[(k + 1) % f.Count]];
            n += new Vec3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        double ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
        Func<Vec3, (double, double)> proj = az >= ax && az >= ay ? p => (p.X, p.Y) : ax >= ay ? p => (p.Y, p.Z) : p => (p.Z, p.X);
        var sign = (az >= ax && az >= ay ? n.Z : ax >= ay ? n.X : n.Y) >= 0 ? 1.0 : -1.0;

        var idx = f.ToList();
        double Cross((double, double) o, (double, double) a, (double, double) b) =>
            ((a.Item1 - o.Item1) * (b.Item2 - o.Item2) - (a.Item2 - o.Item2) * (b.Item1 - o.Item1)) * sign;

        var guard = 0;
        while (idx.Count > 3 && guard++ < 10000)
        {
            var clipped = false;
            for (var k = 0; k < idx.Count; k++)
            {
                int ip = idx[(k - 1 + idx.Count) % idx.Count], ic = idx[k], inx = idx[(k + 1) % idx.Count];
                var (p, c, q) = (proj(v[ip]), proj(v[ic]), proj(v[inx]));
                if (Cross(p, c, q) <= 1e-12) continue; // reflex or collinear: not an ear
                var blocked = false;
                foreach (var other in idx)
                {
                    if (other == ip || other == ic || other == inx) continue;
                    var r = proj(v[other]);
                    if (r == p || r == c || r == q) continue;
                    if (Cross(p, c, r) >= -1e-12 && Cross(c, q, r) >= -1e-12 && Cross(q, p, r) >= -1e-12) { blocked = true; break; }
                }
                if (blocked) continue;
                yield return new Face(ip, ic, inx);
                idx.RemoveAt(k);
                clipped = true;
                break;
            }
            if (!clipped) throw new InvalidOperationException($"ear clipping failed on {f}");
        }
        if (idx.Count == 3) yield return new Face(idx[0], idx[1], idx[2]);
    }

    // ------------------------------------------------------------------ case files in memory

    public static CaseSpec Spec(Vec3? origin = null, double weld = 0.001) => new()
    {
        SchemaVersion = "0.1",
        CaseId = "t",
        Units = "m",
        Origin = origin ?? Vec3.Zero,
        Tolerances = new Tolerances { Weld = weld },
        Files = new CaseFileNames { Input = "input.obj" },
    };

    public static ExpectedShell Sh(double volume, double footprint, double height, int genus = 0, (double X, double Y)? anchor = null) =>
        new()
        {
            Volume_m3 = volume, FootprintArea_m2 = footprint, MaxHeight_m = height, Genus = genus,
            Anchor_m = anchor is { } a ? new[] { a.X, a.Y } : null,
        };

    public static ExpectedSpec Expect(IEnumerable<ExpectedShell> shells, string[][]? merge = null, string[][]? apart = null,
        bool watertight = true, bool outward = true, bool noSelf = true, double minGap = 1.0, double minEdge = 0.25)
    {
        var list = shells.ToList();
        return new ExpectedSpec
        {
            SchemaVersion = "0.1",
            CaseId = "t",
            Seed = 1,
            FaultsInjected = Array.Empty<string>(),
            Expected = new ExpectedGeometry
            {
                ShellCount = list.Count,
                Shells = list,
                MustMerge = (merge ?? Array.Empty<string[]>()).Select(g => (IReadOnlyList<string>)g).ToList(),
                MustStaySeparate = (apart ?? Array.Empty<string[]>()).Select(g => (IReadOnlyList<string>)g).ToList(),
            },
            Checks = new ExpectedChecks
            {
                Watertight = watertight, OutwardNormals = outward, NoSelfIntersections = noSelf,
                MinGap_m = minGap, MinEdge_m = minEdge,
            },
            Tolerances = new CheckTolerances { VolumeRel = 0.01, AreaRel = 0.01, Height_m = 0.01 },
        };
    }

    /// <summary>Expected values of an axis-aligned box standing on the ground.</summary>
    public static ExpectedShell BoxShell(double dx, double dy, double dz) => Sh(dx * dy * dz, dx * dy, dz);

    public static CheckResult Grade(Mesh repaired, ExpectedSpec expected, Mesh? input = null, CaseSpec? spec = null) =>
        Grader.Grade("t", repaired, null, () => input is null ? (null, "no input given") : (input, null), spec ?? Spec(), expected);

    public static CheckEntry Get(this CheckResult r, string name) => r.Checks.Single(c => c.Name == name);

    public static string Detail(this CheckEntry e) =>
        e.Extra is not null && e.Extra.TryGetValue("detail", out var d) ? d.GetString() ?? "" : "";

    public static string Failed(this CheckResult r) =>
        string.Join(" | ", r.Checks.Where(c => !c.Pass).Select(c => $"{c.Name}: value {c.Value}, limit {c.Limit}, {c.Detail()}"));
}
