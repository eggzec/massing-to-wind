using FsCheck;
using FsCheck.Xunit;
using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Repair.Model;
using M2W.Repair.Planar;
using M2W.Repair.Steps;

namespace M2W.Repair.Tests;

public class Step1CleanupTests
{
    [Fact]
    public void Clean_box_comes_back_closed_with_the_same_volume()
    {
        var outcome = TestMeshes.Repair(TestMeshes.Box(0, 0, 0, 20, 10, 9));
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(1, outcome.Report.ShellCount);
        Assert.Equal(1800, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
    }

    [Fact]
    public void Far_coordinates_are_shifted_by_minus_origin()
    {
        var origin = new Vec3(681940, 5883213, 12.5);
        var input = TestMeshes.Box(origin.X + 5, origin.Y + 7, origin.Z, origin.X + 25, origin.Y + 17, origin.Z + 9);
        var outcome = TestMeshes.Repair(input, origin);
        Assert.Equal(-origin, outcome.Report.ShiftApplied);
        var b = MeshMeasures.BoundingBox(outcome.Repaired!);
        Assert.Equal(new Vec3(5, 7, 0), b.Min);
        Assert.Equal(new Vec3(25, 17, 9), b.Max);
        Assert.Equal(1800, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
    }

    [Fact]
    public void Unshared_and_nudged_vertices_are_welded()
    {
        // Every face gets its own vertex copies; some copies are nudged by less than the weld tolerance.
        var box = TestMeshes.Box(0, 0, 0, 10, 10, 3);
        var m = new Mesh();
        var o = m.AddObject("B01");
        var k = 0;
        foreach (var f in box.Objects[0].Faces)
        {
            var idx = new List<int>();
            foreach (var i in f)
            {
                var v = box.Vertices[i];
                if (k++ % 3 == 0) v += new Vec3(0.0004, -0.0003, 0.0002);
                idx.Add(m.AddVertex(v));
            }
            o.Faces.Add(new Face(idx));
        }
        var outcome = TestMeshes.Repair(m);
        Assert.True(outcome.Report.Status == RepairStatus.Repaired, string.Join(" | ", outcome.Report.Warnings));
        var weld = outcome.Report.Operations.Single(x => x.Op == "weld");
        Assert.Equal(24 - 8, weld.Count);
        Assert.Equal(8, outcome.Repaired!.Vertices.Count);
        TestMeshes.AssertClosedShells(outcome.Repaired);
    }

    [Fact]
    public void Welding_never_merges_points_further_apart_than_the_tolerance()
    {
        var s = new Solid("B01", "B01");
        s.Vertices.AddRange(new[] { new Vec3(0, 0, 0), new Vec3(0.0011, 0, 0), new Vec3(0.0005, 0, 0) });
        Assert.Equal(2, Welder.Weld(s, 0.001)); // the middle point chains all three
        var t = new Solid("B01", "B01");
        t.Vertices.AddRange(new[] { new Vec3(0, 0, 0), new Vec3(0.0011, 0, 0) });
        Assert.Equal(0, Welder.Weld(t, 0.001));
    }

    [Fact]
    public void Duplicate_and_degenerate_faces_are_removed()
    {
        var m = TestMeshes.Box(0, 0, 0, 10, 10, 3);
        var o = m.Objects[0];
        o.Faces.Add(o.Faces[1]);                    // exact duplicate of the top
        o.Faces.Add(new Face(0, 1, 1));             // repeated index
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Contains(outcome.Report.Operations, x => x.Op == "removeDegenerateFaces" && x.Count == 1);
        Assert.Contains(outcome.Report.Operations, x => x.Op == "removeDuplicateFaces" && x.Count == 2); // two triangles
        TestMeshes.AssertClosedShells(outcome.Repaired!);
    }

    [Fact]
    public void Object_names_map_to_blocks()
    {
        Assert.Equal("B01", Solid.BlockOf("B01_F03"));
        Assert.Equal("B12", Solid.BlockOf("B12"));
        Assert.Equal("B01_Fx", Solid.BlockOf("B01_Fx"));
        Assert.Equal("tower", Solid.BlockOf("tower"));
    }

    [Fact]
    public void Same_input_gives_byte_identical_obj()
    {
        var input = TestMeshes.Box(530000.123, 180000.456, 0, 530020.5, 180010.25, 18);
        string Write()
        {
            var o = TestMeshes.Repair(input, new Vec3(530000, 180000, 0));
            return ObjWriter.WriteString(o.Repaired!);
        }
        Assert.Equal(Write(), Write());
    }

    // ------------------------------------------------------------------ triangulator

    [Fact]
    public void Triangulator_keeps_collinear_vertices_and_makes_no_zero_area_triangles()
    {
        // A square with extra points on two edges (as left by a neighbouring face).
        var ring = new List<P2> { new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(3, 3), new(1.5, 3), new(0, 3) };
        var tris = new List<Tri>();
        Assert.True(Triangulator.TryTriangulate(ring, null, tris));
        Assert.Equal(ring.Count - 2, tris.Count);
        Assert.All(tris, t => Assert.True(P2.Orient(ring[t.A], ring[t.B], ring[t.C]) > 0));
        Assert.Equal(9, tris.Sum(t => P2.Orient(ring[t.A], ring[t.B], ring[t.C]) / 2), 9);
        var used = tris.SelectMany(t => new[] { t.A, t.B, t.C }).Distinct().Count();
        Assert.Equal(ring.Count, used);
    }

    [Fact]
    public void Triangulator_handles_a_courtyard_hole()
    {
        var outer = new List<P2> { new(0, 0), new(30, 0), new(30, 20), new(0, 20) };
        var hole = new List<P2> { new(10, 5), new(10, 15), new(20, 15), new(20, 5) };
        var tris = new List<Tri>();
        Assert.True(Triangulator.TryTriangulate(outer, new[] { hole }, tris));
        var all = outer.Concat(hole).ToList();
        Assert.All(tris, t => Assert.True(P2.Orient(all[t.A], all[t.B], all[t.C]) > 0));
        Assert.Equal(600 - 100, tris.Sum(t => P2.Orient(all[t.A], all[t.B], all[t.C]) / 2), 9);
    }

    [Fact]
    public void Triangulator_handles_an_L_shape_in_3D_both_orientations()
    {
        var v = new List<Vec3> { new(0, 0, 5), new(20, 0, 5), new(20, 8, 5), new(8, 8, 5), new(8, 20, 5), new(0, 20, 5) };
        var up = Enumerable.Range(0, 6).ToArray();
        var tris = new List<Tri>();
        Assert.True(Triangulator.TryTriangulate3D(v, up, tris));
        Assert.All(tris, t => Assert.True(Vec3.Cross(v[t.B] - v[t.A], v[t.C] - v[t.A]).Z > 0));
        Assert.True(Triangulator.TryTriangulate3D(v, up.Reverse().ToArray(), tris));
        Assert.All(tris, t => Assert.True(Vec3.Cross(v[t.B] - v[t.A], v[t.C] - v[t.A]).Z < 0));
    }

    /// <summary>Random star-shaped polygons (simple by construction): the triangles tile the polygon exactly.</summary>
    [Property(MaxTest = 200)]
    public Property Triangulated_area_equals_polygon_area(PositiveInt seed)
    {
        var rnd = new System.Random(seed.Get);
        var n = rnd.Next(3, 40);
        var angles = Enumerable.Range(0, n).Select(_ => rnd.NextDouble() * 2 * Math.PI).OrderBy(a => a).ToList();
        var gaps = angles.Zip(angles.Skip(1), (a, b) => b - a).Append(angles[0] + 2 * Math.PI - angles[^1]).ToList();
        // Star-shaped around the origin (so simple and counter-clockwise) only if every angular gap is below pi.
        if (gaps.Any(d => d < 1e-3 || d >= Math.PI - 1e-3)) return true.ToProperty();
        var ring = angles.Select(a => { var r = 1 + rnd.NextDouble() * 20; return new P2(r * Math.Cos(a), r * Math.Sin(a)); }).ToList();
        var tris = new List<Tri>();
        var ok = Triangulator.TryTriangulate(ring, null, tris);
        var area = tris.Sum(t => P2.Orient(ring[t.A], ring[t.B], ring[t.C]) / 2);
        return (ok && tris.Count == n - 2 && tris.All(t => P2.Orient(ring[t.A], ring[t.B], ring[t.C]) > 0)
                && Math.Abs(area - Triangulator.SignedArea(ring)) < 1e-9 * Math.Max(1, area)).ToProperty();
    }
}
