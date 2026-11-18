using M2W.Checker;
using M2W.Geometry;
using M2W.Geometry.Models;
using Xunit;
using static M2W.Checker.Tests.Kit;

namespace M2W.Checker.Tests;

/// <summary>Every check: a hand-built mesh that must pass it and one that must fail it.</summary>
public class CheckTests
{
    private static readonly ExpectedSpec OneBox = Expect(new[] { BoxShell(10, 10, 10) });
    private static Mesh GoodBox() => Boxes((0, 0, 0, 10, 10, 10));

    [Fact]
    public void A_clean_box_passes_every_check()
    {
        var r = Grade(GoodBox(), OneBox);
        Assert.True(r.Pass, r.Failed());
        Assert.Equal(CheckNames.All, r.Checks.Select(c => c.Name));
        Assert.Empty(r.Validate());
    }

    // ------------------------------------------------------------------ parse

    [Fact]
    public void Parse_fails_on_polygons()
    {
        var m = Single("shell_000", (m, o) => AddPrismPolygons(m, o, new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) }, 0, 10));
        var r = Grade(m, OneBox);
        Assert.False(r.Get(CheckNames.Parse).Pass);
        Assert.Contains("not triangles", r.Get(CheckNames.Parse).Detail());
        Assert.True(r.Get(CheckNames.Watertight).Pass); // still analysed (fan-triangulated)
        Assert.False(r.Pass);
    }

    [Theory]
    [InlineData("B01")]
    [InlineData("shell_1")]
    [InlineData("shell_0001")]
    [InlineData("Shell_000")]
    public void Parse_fails_on_names_other_than_shell_NNN(string name)
    {
        var m = GoodBox();
        var renamed = new Mesh(m.Vertices, new[] { new MeshObject(name, m.Objects[0].Faces) });
        Assert.False(Grade(renamed, OneBox).Get(CheckNames.Parse).Pass);
    }

    [Fact]
    public void Parse_fails_on_a_repeated_name_and_on_an_empty_object()
    {
        var two = Boxes((0, 0, 0, 10, 10, 10), (20, 0, 0, 30, 10, 10));
        var dup = new Mesh(two.Vertices, new[] { new MeshObject("shell_000", two.Objects[0].Faces), new MeshObject("shell_000", two.Objects[1].Faces) });
        Assert.Contains("used twice", Grade(dup, Expect(new[] { BoxShell(10, 10, 10), BoxShell(10, 10, 10) })).Get(CheckNames.Parse).Detail());

        var empty = GoodBox();
        empty.AddObject("shell_001");
        Assert.Contains("no faces", Grade(empty, OneBox).Get(CheckNames.Parse).Detail());
    }

    [Fact]
    public void Parse_fails_on_faces_before_any_o_line()
    {
        var m = ObjReader.ReadString("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        Assert.False(Grade(m, OneBox).Get(CheckNames.Parse).Pass);
    }

    [Fact]
    public void An_unreadable_file_fails_parse_and_every_other_check()
    {
        var r = Grader.Grade("t", null, "line 3: bad", () => (null, null), Spec(), OneBox);
        Assert.False(r.Pass);
        Assert.Equal(CheckNames.All, r.Checks.Select(c => c.Name));
        Assert.All(r.Checks, c => Assert.False(c.Pass));
        Assert.Empty(r.Validate());
    }

    // ------------------------------------------------------------------ watertight

    [Fact]
    public void Watertight_counts_free_edges()
    {
        var m = GoodBox();
        m.Objects[0].Faces.RemoveAt(2); // one top triangle
        var r = Grade(m, OneBox);
        var w = r.Get(CheckNames.Watertight);
        Assert.False(w.Pass);
        Assert.Equal(3, w.Value);
    }

    [Fact]
    public void Watertight_is_not_enforced_when_expected_json_says_false()
    {
        var m = GoodBox();
        m.Objects[0].Faces.RemoveAt(2);
        var w = Grade(m, Expect(new[] { BoxShell(10, 10, 10) }, watertight: false)).Get(CheckNames.Watertight);
        Assert.True(w.Pass);
        Assert.Equal(3, w.Value);
    }

    [Fact]
    public void Duplicate_vertices_are_not_welded_by_the_checker()
    {
        // The same box, but the top four vertices exist twice and the walls use the copies: free edges.
        var m = GoodBox();
        var copies = Enumerable.Range(4, 4).Select(i => m.AddVertex(m.Vertices[i])).ToArray();
        var faces = m.Objects[0].Faces;
        for (var k = 4; k < faces.Count; k++)
            faces[k] = new Face(faces[k].Select(i => i >= 4 && i < 8 ? copies[i - 4] : i));
        Assert.False(Grade(m, OneBox).Get(CheckNames.Watertight).Pass);
    }

    // ------------------------------------------------------------------ manifold

    [Fact]
    public void Manifold_fails_on_an_edge_used_by_four_faces()
    {
        // Two boxes in one shell sharing one edge (by index): the edge is used 4 times.
        var m = Boxes((0, 0, 0, 10, 10, 10));
        var o = m.Objects[0];
        var b = m.Vertices.Count;
        AddBox(m, o, 10, 10, 0, 20, 20, 10);
        // Re-point the second box's corner (10,10,0) and (10,10,10) at the first box's vertices 2 and 6.
        for (var k = 12; k < o.Faces.Count; k++)
            o.Faces[k] = new Face(o.Faces[k].Select(i => i == b + 0 ? 2 : i == b + 4 ? 6 : i));
        var r = Grade(m, Expect(new[] { BoxShell(10, 10, 10) }));
        Assert.False(r.Get(CheckNames.Manifold).Pass);
        Assert.True(r.Get(CheckNames.Watertight).Pass);
    }

    [Fact]
    public void Manifold_fails_on_a_vertex_shared_by_two_boxes()
    {
        var m = Boxes((0, 0, 0, 10, 10, 10));
        var o = m.Objects[0];
        var b = m.Vertices.Count;
        AddBox(m, o, 10, 10, 10, 20, 20, 20);
        for (var k = 12; k < o.Faces.Count; k++)
            o.Faces[k] = new Face(o.Faces[k].Select(i => i == b + 0 ? 6 : i)); // (10,10,10) shared
        var r = Grade(m, OneBox);
        var e = r.Get(CheckNames.Manifold);
        Assert.False(e.Pass);
        Assert.Equal(1, e.Value);
        Assert.Contains("1 non-manifold vertices", e.Detail());
    }

    // ------------------------------------------------------------------ outwardNormals

    [Fact]
    public void Outward_normals_fail_on_one_flipped_triangle()
    {
        var m = GoodBox();
        m.Objects[0].Faces[5] = m.Objects[0].Faces[5].Reversed();
        var e = Grade(m, OneBox).Get(CheckNames.OutwardNormals);
        Assert.False(e.Pass);
        Assert.Equal(3, e.Value); // its three edges now run the same way as their neighbours'
    }

    [Fact]
    public void Outward_normals_fail_on_an_inside_out_box()
    {
        var m = GoodBox();
        var faces = m.Objects[0].Faces;
        for (var k = 0; k < faces.Count; k++) faces[k] = faces[k].Reversed();
        var r = Grade(m, OneBox);
        Assert.False(r.Get(CheckNames.OutwardNormals).Pass);
        Assert.Contains("not positive", r.Get(CheckNames.OutwardNormals).Detail());
        Assert.True(r.Get(CheckNames.Watertight).Pass);
    }

    // ------------------------------------------------------------------ euler

    [Fact]
    public void Euler_passes_a_courtyard_with_genus_1_and_fails_it_with_genus_0()
    {
        var m = ZeroRepair(Single("B01", (m, o) => AddCourtyardPolygons(m, o, (0, 0, 30, 30), (10, 10, 20, 20), 0, 12)), Vec3.Zero);
        var shell = Sh(800 * 12, 800, 12, genus: 1);
        var r = Grade(m, Expect(new[] { shell }));
        Assert.True(r.Pass, r.Failed());

        var wrong = Grade(m, Expect(new[] { Sh(800 * 12, 800, 12, genus: 0) }));
        Assert.False(wrong.Get(CheckNames.Euler).Pass);
        Assert.Contains("= 0, expected 2", wrong.Get(CheckNames.Euler).Detail());
    }

    [Fact]
    public void Euler_fails_two_separate_boxes_in_one_shell()
    {
        var m = new Mesh();
        var o = m.AddObject("shell_000");
        AddBox(m, o, 0, 0, 0, 10, 10, 10);
        AddBox(m, o, 20, 0, 0, 30, 10, 10);
        var r = Grade(m, Expect(new[] { Sh(2000, 200, 10) }));
        Assert.False(r.Get(CheckNames.Euler).Pass); // V-E+F = 4
        Assert.True(r.Get(CheckNames.Watertight).Pass);
    }

    // ------------------------------------------------------------------ noSelfIntersections

    [Fact]
    public void Self_intersection_fails_two_interpenetrating_boxes_in_one_shell()
    {
        var m = new Mesh();
        var o = m.AddObject("shell_000");
        AddBox(m, o, 0, 0, 0, 10, 10, 10);
        AddBox(m, o, 5, 5, 0, 15, 15, 10);
        var e = Grade(m, Expect(new[] { Sh(1750, 175, 10) })).Get(CheckNames.NoSelfIntersections);
        Assert.False(e.Pass);
        Assert.True(e.Value > 0);
    }

    [Fact]
    public void Self_intersection_fails_a_box_with_a_corner_pushed_through_the_floor()
    {
        var m = GoodBox();
        m.Vertices[6] = new Vec3(5, 5, -5); // top corner (10,10,10) pushed down through the bottom face
        Assert.False(Grade(m, OneBox).Get(CheckNames.NoSelfIntersections).Pass);
    }

    [Fact]
    public void Self_intersection_fails_a_fold_along_a_shared_edge()
    {
        var v = new List<Vec3> { new(0, 0, 0), new(10, 0, 0), new(5, 5, 0), new(5, 3, 0) };
        var s = new Shell("shell_000", v, new List<(int, int, int)> { (0, 1, 2), (1, 0, 3) });
        Assert.True(SelfIntersection.PairIntersects(s, 0, 1));
        var flat = new Shell("shell_000", v.Take(3).Append(new Vec3(5, -5, 0)).ToList(), new List<(int, int, int)> { (0, 1, 2), (1, 0, 3) });
        Assert.False(SelfIntersection.PairIntersects(flat, 0, 1));
    }

    [Fact]
    public void Self_intersection_fails_triangles_sharing_a_vertex_that_cross()
    {
        var v = new List<Vec3>
        {
            new(0, 0, 0), new(10, 0, 0), new(0, 10, 0),  // ground triangle
            new(2, 2, -5), new(2, 2, 5),                 // pierces it
            new(-2, 0, 5), new(0, -2, 5),                // rises away from it
        };
        var s = new Shell("shell_000", v, new List<(int, int, int)> { (0, 1, 2), (0, 3, 4) });
        Assert.True(SelfIntersection.PairIntersects(s, 0, 1));
        var ok = new Shell("shell_000", v, new List<(int, int, int)> { (0, 1, 2), (0, 5, 6) });
        Assert.False(SelfIntersection.PairIntersects(ok, 0, 1));
        // An edge leaving the shared vertex that lies inside the other triangle is a crossing too.
        var lying = new Shell("shell_000", v.Append(new Vec3(3, 3, 0)).ToList(), new List<(int, int, int)> { (0, 1, 2), (0, 7, 4) });
        Assert.True(SelfIntersection.PairIntersects(lying, 0, 1));
    }

    [Fact]
    public void Self_intersection_passes_non_convex_blocks()
    {
        var lPlan = new[] { (0.0, 0.0), (20.0, 0.0), (20.0, 8.0), (8.0, 8.0), (8.0, 20.0), (0.0, 20.0) };
        var m = ZeroRepair(Single("B01", (m, o) => AddPrismPolygons(m, o, lPlan, 0, 9)), Vec3.Zero);
        var r = Grade(m, Expect(new[] { Sh(256 * 9, 256, 9) }));
        Assert.True(r.Pass, r.Failed());
    }

    [Fact]
    public void Finely_split_faces_are_not_self_intersections()
    {
        // 2 shells x 6 faces x 30 x 30 quads = 21,600 triangles, 1.5 m apart.
        var m = new Mesh();
        AddGridBox(m, m.AddObject("shell_000"), new Vec3(0, 0, 0), new Vec3(30, 30, 30), 30);
        AddGridBox(m, m.AddObject("shell_001"), new Vec3(31.5, 0, 0), new Vec3(61.5, 30, 30), 30);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var r = Grade(m, Expect(new[] { BoxShell(30, 30, 30), BoxShell(30, 30, 30) }));
        Assert.True(r.Pass, r.Failed());
        Assert.Equal(1.5, r.Get(CheckNames.MinGap).Value, 9);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"grading took {watch.Elapsed}");
    }

    // ------------------------------------------------------------------ minGap_m

    [Theory]
    [InlineData(1.1, true)]
    [InlineData(1.0, true)]
    [InlineData(0.9, false)]
    [InlineData(0.0, false)]
    public void Min_gap_between_shells(double gap, bool pass)
    {
        var m = Boxes((0, 0, 0, 10, 10, 10), (10 + gap, 0, 0, 20 + gap, 10, 10));
        var e = Grade(m, Expect(new[] { BoxShell(10, 10, 10), BoxShell(10, 10, 10) })).Get(CheckNames.MinGap);
        Assert.Equal(pass, e.Pass);
        Assert.Equal(gap, e.Value, 9);
        Assert.Equal(1.0, e.Limit);
    }

    [Fact]
    public void Min_gap_is_measured_diagonally_between_corners()
    {
        // Corners 0.6 m apart in x and y: distance 0.85 m < 1 m although each axis gap alone is 0.6 m.
        var m = Boxes((0, 0, 0, 10, 10, 10), (10.6, 10.6, 0, 20, 20, 10));
        var e = Grade(m, Expect(new[] { BoxShell(10, 10, 10), BoxShell(9.4, 9.4, 10) })).Get(CheckNames.MinGap);
        Assert.False(e.Pass);
        Assert.Equal(Math.Sqrt(0.72), e.Value, 9);
    }

    [Fact]
    public void Min_gap_is_zero_for_a_shell_inside_another()
    {
        var m = Boxes((0, 0, 0, 30, 30, 30), (10, 10, 0, 20, 20, 10));
        var e = Grade(m, Expect(new[] { BoxShell(30, 30, 30), BoxShell(10, 10, 10) })).Get(CheckNames.MinGap);
        Assert.False(e.Pass);
        Assert.Equal(0, e.Value);
    }

    [Fact]
    public void Min_gap_passes_with_a_single_shell()
    {
        Assert.True(Grade(GoodBox(), OneBox).Get(CheckNames.MinGap).Pass);
    }

    // ------------------------------------------------------------------ minEdge_m

    [Theory]
    [InlineData(0.3, true)]
    [InlineData(0.25, true)]
    [InlineData(0.2, false)]
    public void Min_edge(double thin, bool pass)
    {
        var m = Boxes((0, 0, 0, 10, thin, 10));
        var e = Grade(m, Expect(new[] { BoxShell(10, thin, 10) })).Get(CheckNames.MinEdge);
        Assert.Equal(pass, e.Pass);
        Assert.Equal(thin, e.Value, 9);
    }

    // ------------------------------------------------------------------ shellCount

    [Fact]
    public void Shell_count_must_equal_expected()
    {
        var two = Boxes((0, 0, 0, 10, 10, 10), (20, 0, 0, 30, 10, 10));
        Assert.True(Grade(two, Expect(new[] { BoxShell(10, 10, 10), BoxShell(10, 10, 10) })).Get(CheckNames.ShellCount).Pass);
        var e = Grade(two, OneBox).Get(CheckNames.ShellCount);
        Assert.False(e.Pass);
        Assert.Equal(2, e.Value);
        Assert.Equal(1, e.Limit);
    }

    // ------------------------------------------------------------------ volume, footprint, height

    [Theory]
    [InlineData(10.09, true)]  // +0.9 %
    [InlineData(10.2, false)]  // +2 %
    public void Volume_within_relative_tolerance(double height, bool pass)
    {
        var m = Boxes((0, 0, 0, 10, 10, height));
        // Height and footprint expected as built, volume expected for 10 m: only volume can fail.
        var r = Grade(m, Expect(new[] { Sh(1000, 100, height) }));
        Assert.Equal(pass, r.Get(CheckNames.Volume).Pass);
        Assert.Equal(100 * height, r.Get(CheckNames.Volume).Value, 6);
        Assert.Equal(1000, r.Get(CheckNames.Volume).Limit);
    }

    [Theory]
    [InlineData(10.04, true)]
    [InlineData(10.2, false)]
    public void Footprint_within_relative_tolerance(double width, bool pass)
    {
        var m = Boxes((0, 0, 0, width, 10, 10));
        var r = Grade(m, Expect(new[] { Sh(width * 100, 100, 10) }));
        Assert.Equal(pass, r.Get(CheckNames.Footprint).Pass);
        Assert.Equal(width * 10, r.Get(CheckNames.Footprint).Value, 6);
    }

    [Fact]
    public void Footprint_is_the_ground_floor_not_the_roof_of_a_podium()
    {
        var m = ZeroRepair(Single("B01", (m, o) => AddPodiumTowerPolygons(m, o, (0, 0, 40, 30), (10, 5, 30, 25), 6, 60)), Vec3.Zero);
        var r = Grade(m, Expect(new[] { Sh(1200 * 6 + 400 * 54, 1200, 60) }));
        Assert.True(r.Pass, r.Failed());
    }

    [Theory]
    [InlineData(18.005, true)]
    [InlineData(18.02, false)]
    public void Height_within_absolute_tolerance(double height, bool pass)
    {
        var m = Boxes((0, 0, 0, 10, 10, height));
        var r = Grade(m, Expect(new[] { Sh(100 * height, 100, 18) }));
        Assert.Equal(pass, r.Get(CheckNames.MaxHeight).Pass);
    }

    [Fact]
    public void Shells_are_matched_regardless_of_their_order()
    {
        var m = Boxes((0, 0, 0, 10, 10, 30), (20, 0, 0, 40, 10, 9));
        var r = Grade(m, Expect(new[] { BoxShell(20, 10, 9), BoxShell(10, 10, 30) }));
        Assert.True(r.Pass, r.Failed());
    }

    [Fact]
    public void A_missing_shell_fails_the_measures()
    {
        var m = Boxes((0, 0, 0, 10, 10, 30));
        var r = Grade(m, Expect(new[] { BoxShell(20, 10, 9), BoxShell(10, 10, 30) }));
        Assert.False(r.Get(CheckNames.Volume).Pass);
        Assert.Contains("no repaired shell", r.Get(CheckNames.Volume).Detail());
        Assert.False(r.Get(CheckNames.ShellCount).Pass);
    }

    // ------------------------------------------------------------------ groundContact

    [Theory]
    [InlineData(0.0005, true)]
    [InlineData(-0.0005, true)]
    [InlineData(0.01, false)]
    [InlineData(-0.3, false)]
    public void Ground_contact_within_weld(double z0, bool pass)
    {
        var m = Boxes((0, 0, z0, 10, 10, 10));
        var e = Grade(m, OneBox).Get(CheckNames.GroundContact);
        Assert.Equal(pass, e.Pass);
        Assert.Equal(Math.Abs(z0), e.Value, 9);
        Assert.Equal(0.001, e.Limit);
    }

    // ------------------------------------------------------------------ result.json

    [Fact]
    public void Grading_is_deterministic()
    {
        var m = Boxes((0, 0, 0, 10, 10, 10), (10.5, 0, 0, 20, 10, 10));
        var exp = Expect(new[] { BoxShell(10, 10, 10), BoxShell(9.5, 10, 10) });
        var a = M2W.Geometry.Json.M2WJson.Serialize(Grade(m, exp));
        var b = M2W.Geometry.Json.M2WJson.Serialize(Grade(m, exp));
        Assert.Equal(a, b);
        Assert.Contains("\"detail\"", a);
    }
}
