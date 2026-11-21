using M2W.Checker;
using M2W.Geometry;
using Xunit;
using static M2W.Checker.Tests.Kit;

namespace M2W.Checker.Tests;

/// <summary>
/// Q-S1 (D-038): an anchor exactly on an edge that two plan triangles share must count as inside, although rounding
/// can give it a barycentric weight of about −1e-17 in both. A point clearly outside must still be outside.
/// All meshes are built by hand.
/// </summary>
public class AnchorContainmentTests
{
    /// <summary>
    /// An axis-aligned box as 12 outward triangles, with a choice of diagonal for the bottom and the top quad:
    /// <paramref name="bottomAlt"/> false splits the bottom along (x0,y0)–(x1,y1), true along (x1,y0)–(x0,y1);
    /// <paramref name="topAlt"/> likewise for the top. Walls as in <see cref="Kit.AddBox"/>.
    /// <paramref name="rotations"/> (0–80, four base-3 digits) picks which vertex each of the four bottom and top
    /// triangles starts at. That keeps the winding but changes the rounding of the barycentric weights.
    /// </summary>
    private static Mesh SplitBox(double x0, double y0, double x1, double y1, double z1, bool bottomAlt, bool topAlt, int rotations = 0)
    {
        var m = new Mesh();
        var o = m.AddObject("shell_000");
        m.Vertices.AddRange(new[]
        {
            new Vec3(x0, y0, 0), new Vec3(x1, y0, 0), new Vec3(x1, y1, 0), new Vec3(x0, y1, 0),
            new Vec3(x0, y0, z1), new Vec3(x1, y0, z1), new Vec3(x1, y1, z1), new Vec3(x0, y1, z1),
        });
        void Tri(int a, int b, int c) => o.Faces.Add(new Face(a, b, c));
        void Rot(int a, int b, int c)
        {
            var r = rotations % 3;
            rotations /= 3;
            if (r == 0) Tri(a, b, c);
            else if (r == 1) Tri(b, c, a);
            else Tri(c, a, b);
        }
        // bottom quad 0,3,2,1 (facing down)
        if (!bottomAlt) { Rot(0, 3, 2); Rot(0, 2, 1); } // diagonal 0–2
        else { Rot(3, 2, 1); Rot(3, 1, 0); }            // diagonal 3–1
        // top quad 4,5,6,7 (facing up)
        if (!topAlt) { Rot(4, 5, 6); Rot(4, 6, 7); }    // diagonal 4–6
        else { Rot(5, 6, 7); Rot(5, 7, 4); }            // diagonal 5–7
        foreach (var (a, b, c, d) in new[] { (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7) })
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }
        return m;
    }

    /// <summary>Every split (bottom × top diagonal) and every vertex rotation of the four plan triangles.</summary>
    private static IEnumerable<(bool BottomAlt, bool TopAlt, int Rotations)> Variants()
    {
        foreach (var bottomAlt in new[] { false, true })
            foreach (var topAlt in new[] { false, true })
                for (var r = 0; r < 81; r++)
                    yield return (bottomAlt, topAlt, r);
    }

    private static Shell ShellOf(Mesh m) =>
        new("shell_000", m.Vertices, m.Objects[0].Faces.Select(f => (f[0], f[1], f[2])).ToList());

    /// <summary>The rule before Q-S1 (all weights ≥ 0), kept here only to prove a test really hits the rounding.</summary>
    private static bool StrictlyContains(Shell s, double x, double y)
    {
        foreach (var t in s.Tris)
        {
            var d = (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X);
            if (d == 0) continue;
            var u = ((t.B.X - x) * (t.C.Y - y) - (t.B.Y - y) * (t.C.X - x)) / d;
            var v = ((t.C.X - x) * (t.A.Y - y) - (t.C.Y - y) * (t.A.X - x)) / d;
            var w = 1 - u - v;
            if (u >= 0 && v >= 0 && w >= 0) return true;
        }
        return false;
    }

    // Boxes in local coordinates with their centre written as a decimal, as expected.json stores an anchor:
    // the Q-S1 box (case_0284), plus two whose centres also round off a diagonal (found by a search over random
    // boxes with 0.1 m corners; many boxes, e.g. (0.3, 0.7)–(12.1, 9.9), never miss).
    private static readonly (double X0, double Y0, double X1, double Y1, double Cx, double Cy)[] Centred =
    {
        (-5.1, 48.8, 62.3, 63.7, 28.6, 56.25),
        (-9.9, -2.8, 3, 13.4, -3.45, 5.3),
        (14.5, -10.5, 67.1, -1.3, 40.8, -5.9),
    };

    public static IEnumerable<object[]> CentredBoxes() => Centred.Select(c => new object[] { c.X0, c.Y0, c.X1, c.Y1, c.Cx, c.Cy });

    [Theory]
    [MemberData(nameof(CentredBoxes))]
    public void An_anchor_at_the_centre_is_contained_for_every_diagonal_split(double x0, double y0, double x1, double y1, double cx, double cy)
    {
        const double h = 14.5;
        var expected = Expect(new[] { Sh((x1 - x0) * (y1 - y0) * h, (x1 - x0) * (y1 - y0), h, anchor: (cx, cy)) });
        foreach (var (bottomAlt, topAlt, rot) in Variants())
        {
            var mesh = SplitBox(x0, y0, x1, y1, h, bottomAlt, topAlt, rot);
            var label = $"bottomAlt {bottomAlt}, topAlt {topAlt}, rotations {rot}";
            Assert.True(ShellOf(mesh).ContainsInPlan(cx, cy), label);

            var r = Grade(mesh, expected);
            Assert.True(r.Get(CheckNames.Volume).Pass, label + ": " + r.Failed());
            Assert.True(r.Get(CheckNames.Footprint).Pass, label + ": " + r.Failed());
            Assert.True(r.Get(CheckNames.MaxHeight).Pass, label + ": " + r.Failed());
            Assert.True(r.Pass, label + ": " + r.Failed());
        }
    }

    [Theory]
    [MemberData(nameof(CentredBoxes))]
    public void The_centre_cases_really_round_off_the_shared_diagonal(double x0, double y0, double x1, double y1, double cx, double cy)
    {
        // Some triangulations of these boxes put the centre outside every triangle under the strict rule (all
        // weights >= 0). Each of them must now contain it.
        var missed = Variants().Where(v => !StrictlyContains(ShellOf(SplitBox(x0, y0, x1, y1, 14.5, v.BottomAlt, v.TopAlt, v.Rotations)), cx, cy)).ToList();
        Assert.NotEmpty(missed);
        foreach (var v in missed)
            Assert.True(ShellOf(SplitBox(x0, y0, x1, y1, 14.5, v.BottomAlt, v.TopAlt, v.Rotations)).ContainsInPlan(cx, cy));
    }

    [Fact]
    public void A_point_on_an_outer_edge_or_corner_is_contained()
    {
        var s = ShellOf(SplitBox(0.3, 0.7, 12.1, 9.9, 10, false, false));
        Assert.True(s.ContainsInPlan(12.1, 5.3));  // east edge
        Assert.True(s.ContainsInPlan(6.2, 0.7));   // south edge
        Assert.True(s.ContainsInPlan(0.3, 9.9));   // corner
        Assert.True(s.ContainsInPlan(12.1, 9.9));  // corner at the end of the diagonal
    }

    [Fact]
    public void A_point_on_a_slanted_outer_edge_is_contained()
    {
        // Acute plot corner (0,0)–(30,0)–(4,9): the midpoint of the slanted side.
        var m = Single("shell_000", (mm, o) => AddPrismPolygons(mm, o, new[] { (0.0, 0.0), (30.0, 0.0), (4.0, 9.0) }, 0, 9));
        var s = new Shell("shell_000", m.Vertices, m.Objects[0].Faces.SelectMany(f => Triangulate(m.Vertices, f)).Select(f => (f[0], f[1], f[2])).ToList());
        Assert.True(s.ContainsInPlan(17, 4.5));
        // A third of the way along the side: not representable exactly, so it lies within rounding of the edge.
        Assert.True(s.ContainsInPlan(30 + (4 - 30) / 3.0, 9 / 3.0));
    }

    [Fact]
    public void A_point_within_rounding_outside_an_outer_edge_is_contained()
    {
        // 1e-12 m is far below the 1e-6 m OBJ resolution: rounding from an origin shift, not a real miss.
        var s = ShellOf(SplitBox(-5.1, 48.8, 62.3, 63.7, 14.5, false, false));
        Assert.True(s.ContainsInPlan(62.3 + 1e-12, 56.25));
        Assert.True(s.ContainsInPlan(28.6, 48.8 - 1e-12));
    }

    [Fact]
    public void A_point_1e_6_m_outside_an_edge_is_not_contained()
    {
        var s = ShellOf(SplitBox(0.3, 0.7, 12.1, 9.9, 10, false, false));
        Assert.False(s.ContainsInPlan(12.1 + 1e-6, 5.3));
        Assert.False(s.ContainsInPlan(6.2, 0.7 - 1e-6));
        Assert.False(s.ContainsInPlan(0.3 - 1e-6, 9.9 + 1e-6));

        // Slanted side (30,0)–(4,9) of the acute corner, 1e-6 m out along its outward normal from the midpoint:
        // inside the triangle's bounding box, so only the weight test can reject it.
        var m = Single("shell_000", (mm, o) => AddPrismPolygons(mm, o, new[] { (0.0, 0.0), (30.0, 0.0), (4.0, 9.0) }, 0, 9));
        var t = new Shell("shell_000", m.Vertices, m.Objects[0].Faces.SelectMany(f => Triangulate(m.Vertices, f)).Select(f => (f[0], f[1], f[2])).ToList());
        var len = Math.Sqrt(26 * 26 + 9 * 9);
        double nx = 9 / len, ny = 26 / len; // outward normal of the side
        Assert.False(t.ContainsInPlan(17 + 1e-6 * nx, 4.5 + 1e-6 * ny));
        Assert.True(t.ContainsInPlan(17 - 1e-6 * nx, 4.5 - 1e-6 * ny)); // and 1e-6 m in, it is inside
    }

    [Fact]
    public void Q_S1_far_coordinate_box_shifted_to_local_passes_with_its_anchor_at_the_centre()
    {
        // The case_0284 situation: a box in map coordinates, moved to local coordinates by the origin and written
        // to a 6-decimal OBJ, as a solver would. The anchor is the centre of the rectangle.
        var origin = new Vec3(725593.914, 847976.113, 0);
        double fx0 = 725588.814, fy0 = 848024.913, fx1 = 725656.214, fy1 = 848039.813, h = 14.5;
        var anchor = (28.6, 56.25);
        const double w = 67.4, d = 14.9;
        var expected = Expect(new[] { Sh(w * d * h, w * d, h, anchor: anchor) });

        var strictMisses = 0;
        foreach (var (bottomAlt, topAlt, rot) in Variants())
        {
            var far = SplitBox(fx0, fy0, fx1, fy1, h, bottomAlt, topAlt, rot);
            var local = ObjReader.ReadString(ObjWriter.WriteString(Moved(far, Vec3.Zero - origin)));
            if (!StrictlyContains(ShellOf(local), anchor.Item1, anchor.Item2)) strictMisses++;

            var label = $"bottomAlt {bottomAlt}, topAlt {topAlt}, rotations {rot}";
            var r = Grade(local, expected, spec: Spec(origin));
            Assert.True(r.Get(CheckNames.Volume).Pass, label + ": " + r.Failed());
            Assert.True(r.Get(CheckNames.Footprint).Pass, label + ": " + r.Failed());
            Assert.True(r.Get(CheckNames.MaxHeight).Pass, label + ": " + r.Failed());
            Assert.True(r.Pass, label + ": " + r.Failed());
        }
        // Precondition: some of these triangulations failed before Q-S1 (the anchor was in no triangle).
        Assert.True(strictMisses > 0, "no triangulation reproduces the Q-S1 miss");
    }

    [Fact]
    public void An_anchor_outside_every_shell_still_leaves_its_expected_shell_unmatched()
    {
        // Two boxes 20 m apart; the anchor sits in the gap, 1e-6 m east of the first box.
        var mesh = Boxes((0, 0, 0, 10, 10, 10), (30, 0, 0, 40, 10, 10));
        var r = Grade(mesh, Expect(new[] { Sh(1000, 100, 10, anchor: (10 + 1e-6, 5)), Sh(1000, 100, 10, anchor: (35, 5)) }));
        Assert.False(r.Get(CheckNames.Volume).Pass);
        Assert.Contains("no repaired shell left that contains its anchor", r.Get(CheckNames.Volume).Detail());
    }
}
