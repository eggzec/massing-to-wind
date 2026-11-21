using FsCheck;
using FsCheck.Xunit;
using M2W.Geometry;
using M2W.Geometry.Models;

namespace M2W.Repair.Tests;

public class Step5GapTests
{
    /// <summary>Adds a vertical prism over a counter-clockwise plan ring, from z0 to z1, as one object.</summary>
    internal static void AddPrism(Mesh m, string name, IReadOnlyList<(double x, double y)> ring, double z0, double z1)
    {
        var b = m.Vertices.Count;
        var n = ring.Count;
        foreach (var (x, y) in ring) m.Vertices.Add(new Vec3(x, y, z0));
        foreach (var (x, y) in ring) m.Vertices.Add(new Vec3(x, y, z1));
        var o = m.AddObject(name);
        o.Faces.Add(new Face(Enumerable.Range(0, n).Reverse().Select(i => b + i).ToArray()));
        o.Faces.Add(new Face(Enumerable.Range(0, n).Select(i => b + n + i).ToArray()));
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            o.Faces.Add(new Face(b + i, b + j, b + n + j, b + n + i));
        }
    }

    /// <summary>Rectangle [x0,x1]×[y0,y1] rotated by <paramref name="deg"/> about the origin, counter-clockwise.</summary>
    internal static (double, double)[] Rect(double x0, double y0, double x1, double y1, double deg = 0)
    {
        var a = deg * Math.PI / 180;
        var (c, s) = (Math.Cos(a), Math.Sin(a));
        return new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }.Select(p => (c * p.Item1 - s * p.Item2, s * p.Item1 + c * p.Item2)).ToArray();
    }

    private static Mesh TwoBlocks(double gap, double hA = 18, double hB = 12, double deg = 0, double bY0 = 0, double bY1 = 10)
    {
        var m = new Mesh();
        AddPrism(m, "B01", Rect(0, 0, 20, 10, deg), 0, hA);
        AddPrism(m, "B02", Rect(20 + gap, bY0, 30 + gap, bY1, deg), 0, hB);
        return m;
    }

    [Fact]
    public void Parallel_gap_below_minGap_is_filled_up_to_the_lower_block()
    {
        var outcome = TestMeshes.Repair(TwoBlocks(0.3));
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Single(outcome.Repaired!.Objects);
        Assert.Equal(200 * 18 + 100 * 12 + 3 * 12, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        var op = Assert.Single(outcome.Report.Operations, o => o.Op == "closeGap");
        Assert.Equal(new[] { "B01", "B02" }, op.Pair);
        Assert.Equal(0.3, op.Width_m!.Value, 9);
        Assert.Equal(12, op.Extra!["fillHeight_m"].GetDouble());
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.2)]
    [InlineData(5.0)]
    public void Gap_of_minGap_or_more_stays_open(double gap)
    {
        var outcome = TestMeshes.Repair(TwoBlocks(gap));
        Assert.Equal(2, outcome.Repaired!.Objects.Count);
        Assert.DoesNotContain(outcome.Report.Operations, o => o.Op == "closeGap");
    }

    [Fact]
    public void Shorter_neighbour_is_bridged_only_where_the_walls_face_each_other()
    {
        // B02 spans y 2..7 of B01's 0..10 wall: the bridge is the 0.5 × 5 m rectangle in between.
        var outcome = TestMeshes.Repair(TwoBlocks(0.5, 10, 10, bY0: 2, bY1: 7));
        Assert.Single(outcome.Repaired!.Objects);
        Assert.Equal(200 * 10 + 50 * 10 + 2.5 * 10, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
    }

    [Fact]
    public void Blocks_meeting_corner_to_corner_are_joined_by_a_small_fillet()
    {
        var m = new Mesh();
        AddPrism(m, "B01", Rect(0, 0, 10, 10), 0, 9);
        AddPrism(m, "B02", Rect(10.3, 10.4, 20, 20), 0, 9); // corners 0.5 m apart, no walls facing
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Single(outcome.Repaired!.Objects);
        var v = TestMeshes.AssertClosedShells(outcome.Repaired!);
        var bare = 100 * 9 + 9.7 * 9.6 * 9;
        Assert.InRange(v, bare + 0.1, bare + 2 * 9); // a fillet of well under 2 m² in plan
    }

    [Fact]
    public void Towers_on_one_podium_closer_than_minGap_are_closed_above_it_to_the_lower_tower()
    {
        var m = new Mesh();
        AddPrism(m, "B01", Rect(0, 0, 30, 10), 0, 6);
        AddPrism(m, "B02", Rect(0, 0, 10, 10), 0, 30);
        AddPrism(m, "B03", Rect(10.6, 0, 20, 10), 0, 24);
        var outcome = TestMeshes.Repair(m);
        Assert.Single(outcome.Repaired!.Objects);
        Assert.Equal(300 * 6 + 100 * 24 + 94 * 18 + 6 * 18, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
    }

    [Fact]
    public void Narrow_inlet_inside_one_block_is_not_closed()
    {
        // U shape with a 0.5 m slot between its arms: a gap within one part, not between blocks.
        var m = new Mesh();
        AddPrism(m, "B01", new[] { (0.0, 0.0), (10.5, 0.0), (10.5, 10.0), (5.5, 10.0), (5.5, 3.0), (5.0, 3.0), (5.0, 10.0), (0.0, 10.0) }, 0, 9);
        var outcome = TestMeshes.Repair(m);
        Assert.Single(outcome.Repaired!.Objects);
        Assert.Equal((105 - 0.5 * 7) * 9, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.DoesNotContain(outcome.Report.Operations, o => o.Op == "closeGap");
    }

    [Fact]
    public void Gap_closing_respects_the_case_minGap()
    {
        var tol = new Tolerances { MinGap = 0.2 };
        var outcome = TestMeshes.Repair(TwoBlocks(0.3), tol: tol);
        Assert.Equal(2, outcome.Repaired!.Objects.Count);
    }

    /// <summary>
    /// Random rotation and gap: below minGap the two blocks become one shell whose volume is both blocks plus the
    /// facing strip filled to the lower height; at minGap or above they stay two shells.
    /// </summary>
    [Property(MaxTest = 60)]
    public Property Rotated_gap_closes_exactly_below_minGap(int degSeed, int gapSeed, int hSeed)
    {
        var deg = Math.Abs(degSeed % 3600) / 10.0;
        var gap = 0.02 + Math.Abs(gapSeed % 1960) / 1000.0; // 0.02 .. 1.98 m
        var hB = 6 + Math.Abs(hSeed % 30);
        var outcome = TestMeshes.Repair(TwoBlocks(gap, 18, hB, deg));
        var shells = outcome.Repaired!.Objects.Count;
        var v = TestMeshes.AssertClosedShells(outcome.Repaired!);
        var lower = Math.Min(18, hB);
        var expected = gap < 1.0 ? 200 * 18 + 100 * hB + 10 * gap * lower : 200 * 18 + 100 * hB;
        var ok = shells == (gap < 1.0 ? 1 : 2) && Math.Abs(v - expected) < 1e-6 * expected + 1e-3;
        return ok.Label($"deg {deg}, gap {gap}, hB {hB}: {shells} shells, volume {v} vs {expected}");
    }
}
