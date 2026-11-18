using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Geometry.Stages;
using M2W.Repair.Model;

namespace M2W.Repair.Tests;

/// <summary>Hand-built inputs for the solver tests. Every box is a closed, outward solid unless a test breaks it.</summary>
internal static class TestMeshes
{
    /// <summary>Adds an axis-aligned box as one object with six quads (outward, counter-clockwise from outside).</summary>
    public static MeshObject AddBox(Mesh m, string name, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var b = m.Vertices.Count;
        m.Vertices.AddRange(new[]
        {
            new Vec3(x0, y0, z0), new Vec3(x1, y0, z0), new Vec3(x1, y1, z0), new Vec3(x0, y1, z0),
            new Vec3(x0, y0, z1), new Vec3(x1, y0, z1), new Vec3(x1, y1, z1), new Vec3(x0, y1, z1),
        });
        var o = m.AddObject(name);
        o.Faces.Add(new Face(b + 0, b + 3, b + 2, b + 1)); // bottom, normal -z
        o.Faces.Add(new Face(b + 4, b + 5, b + 6, b + 7)); // top, +z
        o.Faces.Add(new Face(b + 0, b + 1, b + 5, b + 4)); // -y
        o.Faces.Add(new Face(b + 1, b + 2, b + 6, b + 5)); // +x
        o.Faces.Add(new Face(b + 2, b + 3, b + 7, b + 6)); // +y
        o.Faces.Add(new Face(b + 3, b + 0, b + 4, b + 7)); // -x
        return o;
    }

    public static Mesh Box(double x0, double y0, double z0, double x1, double y1, double z1, string name = "B01")
    {
        var m = new Mesh();
        AddBox(m, name, x0, y0, z0, x1, y1, z1);
        return m;
    }

    public static CaseSpec Case(Vec3? origin = null, Tolerances? tol = null) => new()
    {
        SchemaVersion = SchemaVersions.Current,
        CaseId = "case_test",
        Units = "m",
        Origin = origin ?? Vec3.Zero,
        Tolerances = tol ?? new Tolerances(),
        Files = new CaseFileNames { Input = "input.obj" },
    };

    public static RepairOutcome Repair(Mesh input, Vec3? origin = null, Tolerances? tol = null) =>
        new MassingRepairer().Repair(new RepairRequest { Case = Case(origin, tol), Input = input });

    /// <summary>Checks every output shell with the solver's own shell check and returns total volume.</summary>
    public static double AssertClosedShells(Mesh m)
    {
        double total = 0;
        foreach (var o in m.Objects)
        {
            Assert.StartsWith("shell_", o.Name);
            var map = new Dictionary<int, int>();
            var verts = new List<Vec3>();
            var tris = new List<Tri>();
            int L(int i)
            {
                if (!map.TryGetValue(i, out var l)) { l = verts.Count; verts.Add(m.Vertices[i]); map[i] = l; }
                return l;
            }
            foreach (var f in o.Faces)
            {
                Assert.True(f.IsTriangle);
                tris.Add(new Tri(L(f[0]), L(f[1]), L(f[2])));
            }
            var r = ShellCheck.Check(verts, tris);
            Assert.True(r.Ok, $"{o.Name}: {r}");
            total += r.Volume;
        }
        return total;
    }
}
