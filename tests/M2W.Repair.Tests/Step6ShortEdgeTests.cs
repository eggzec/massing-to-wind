using FsCheck;
using FsCheck.Xunit;
using M2W.Geometry;
using M2W.Geometry.Models;
using static M2W.Repair.Tests.Step5GapTests;

namespace M2W.Repair.Tests;

public class Step6ShortEdgeTests
{
    /// <summary>Shortest triangle edge of the output, as written (6 decimals).</summary>
    private static double ShortestEdge(Mesh m)
    {
        var best = double.PositiveInfinity;
        foreach (var o in m.Objects)
            foreach (var f in o.Faces)
                for (var k = 0; k < 3; k++)
                    best = Math.Min(best, (m.Vertices[f[(k + 1) % 3]] - m.Vertices[f[k]]).Length);
        return best;
    }

    private static (double x, double y)[] Rotate(IEnumerable<(double x, double y)> ring, double deg)
    {
        var a = deg * Math.PI / 180;
        var (c, s) = (Math.Cos(a), Math.Sin(a));
        return ring.Select(p => (c * p.x - s * p.y, s * p.x + c * p.y)).ToArray();
    }

    private static Mesh Single((double, double)[] ring, double h)
    {
        var m = new Mesh();
        AddPrism(m, "B01", ring, 0, h);
        return m;
    }

    [Fact]
    public void Ledge_thinner_than_minEdge_is_absorbed_by_the_block_above_it()
    {
        // B01_F01 reaches 0.1 m beyond the upper floors along the whole +y wall: a 0.1 m ledge at 4 m.
        var m = new Mesh();
        AddPrism(m, "B01_F01", Rect(0, 0, 20, 10.1), 0, 4);
        AddPrism(m, "B01_F02", Rect(0, 0, 20, 10), 4, 20);
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Single(outcome.Repaired!.Objects);
        Assert.Equal(20 * 10.1 * 20, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.True(ShortestEdge(outcome.Repaired!) >= 0.25 - 1e-6);
        Assert.Contains(outcome.Report.Operations, o => o.Op == "raiseThinRegion");
    }

    [Fact]
    public void Ledge_of_minEdge_or_more_is_kept()
    {
        var m = new Mesh();
        AddPrism(m, "B01_F01", Rect(0, 0, 20, 10.3), 0, 4);
        AddPrism(m, "B01_F02", Rect(0, 0, 20, 10), 4, 20);
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(20 * 0.3 * 4 + 200 * 20, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.DoesNotContain(outcome.Report.Operations, o => o.Op == "raiseThinRegion");
    }

    [Fact]
    public void Z_jog_moves_the_shorter_wall_onto_the_longer_ones_line()
    {
        // East wall: x = 20 for y 0..6, then x = 20.12 for y 6..10 (shorter, sticking out): cut back to x = 20.
        var ring = new[] { (0.0, 0.0), (20.0, 0.0), (20.0, 6.0), (20.12, 6.0), (20.12, 10.0), (0.0, 10.0) };
        var outcome = TestMeshes.Repair(Single(ring, 12));
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(200 * 12, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.True(ShortestEdge(outcome.Repaired!) >= 0.25 - 1e-6);
        Assert.Contains(outcome.Report.Operations, o => o.Op == "alignJogCut");
    }

    [Fact]
    public void Z_jog_with_the_shorter_wall_inside_is_filled_out()
    {
        var ring = new[] { (0.0, 0.0), (20.12, 0.0), (20.12, 6.0), (20.0, 6.0), (20.0, 10.0), (0.0, 10.0) };
        var outcome = TestMeshes.Repair(Single(ring, 12));
        Assert.Equal(20.12 * 10 * 12, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.Contains(outcome.Report.Operations, o => o.Op == "alignJog");
    }

    [Fact]
    public void Shallow_bump_is_cut_back_and_shallow_notch_filled()
    {
        // South wall with a 6 m section moved 0.15 m out (bump) and, on the north wall, one moved 0.15 m in (notch).
        var ring = new[]
        {
            (0.0, 0.0), (7.0, 0.0), (7.0, -0.15), (13.0, -0.15), (13.0, 0.0), (20.0, 0.0),
            (20.0, 10.0), (13.0, 10.0), (13.0, 9.85), (7.0, 9.85), (7.0, 10.0), (0.0, 10.0),
        };
        var outcome = TestMeshes.Repair(Single(ring, 9));
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(200 * 9, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.Contains(outcome.Report.Operations, o => o.Op == "cutBump");
        Assert.Contains(outcome.Report.Operations, o => o.Op == "fillNotch");
        Assert.All(outcome.Repaired!.Vertices, v => Assert.True(v.Y == 0 || v.Y == 10));
    }

    [Fact]
    public void Corner_cut_off_by_a_short_edge_is_squared()
    {
        var ring = new[] { (0.0, 0.0), (20.0, 0.0), (20.0, 9.9), (19.9, 10.0), (0.0, 10.0) };
        var outcome = TestMeshes.Repair(Single(ring, 6));
        Assert.Equal(200 * 6, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.Contains(outcome.Report.Operations, o => o.Op == "squareCorner");
    }

    [Fact]
    public void Split_vertex_next_to_a_corner_is_merged()
    {
        // An extra outline vertex 0.05 m from a corner, slightly off the wall line.
        var ring = new[] { (0.0, 0.0), (20.0, 0.0), (20.0, 10.0), (19.95, 10.003), (0.0, 10.0) };
        var outcome = TestMeshes.Repair(Single(ring, 6));
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(200 * 6, TestMeshes.AssertClosedShells(outcome.Repaired!), 3);
        Assert.True(ShortestEdge(outcome.Repaired!) >= 0.25 - 1e-6);
    }

    [Fact]
    public void Case_minEdge_is_respected()
    {
        // 0.3 m jog: kept at minEdge 0.25, removed at minEdge 0.4.
        var ring = new[] { (0.0, 0.0), (20.0, 0.0), (20.0, 6.0), (20.3, 6.0), (20.3, 10.0), (0.0, 10.0) };
        var kept = TestMeshes.Repair(Single(ring, 12));
        Assert.Equal((200 + 1.2) * 12, TestMeshes.AssertClosedShells(kept.Repaired!), 6);
        var removed = TestMeshes.Repair(Single(ring, 12), tol: new Tolerances { MinEdge = 0.4 });
        Assert.Equal(200 * 12, TestMeshes.AssertClosedShells(removed.Repaired!), 6);
        Assert.True(ShortestEdge(removed.Repaired!) >= 0.4 - 1e-6);
    }

    /// <summary>
    /// A rotated L-shaped block with a random shallow bump, notch or jog of depth below minEdge on random walls: the
    /// output is closed, has no edge shorter than minEdge, its volume differs from the clean block by at most depth
    /// times the wall, and repairing it again changes nothing.
    /// </summary>
    [Property(MaxTest = 60)]
    public Property Shallow_features_leave_no_short_edge(int degSeed, int depthSeed, int kindSeed, int posSeed)
    {
        var deg = Math.Abs(degSeed % 3600) / 10.0;
        var depth = (0.01 + Math.Abs(depthSeed % 230) / 1000.0) * (kindSeed % 2 == 0 ? 1 : -1); // ±0.01 .. 0.24
        var start = 2 + Math.Abs(posSeed % 8);   // feature on the south wall from x = start
        var len = 4 + Math.Abs(kindSeed % 9);    // over len metres; kind 3 runs it to the corner (a jog)
        var toCorner = Math.Abs(kindSeed % 4) == 3;
        var end = toCorner ? 30.0 : start + len;
        var ring = new List<(double, double)> { (0, 0), (start, 0), (start, -depth), (end, -depth) };
        if (!toCorner) ring.Add((end, 0));
        ring.AddRange(new[] { (30.0, toCorner ? -depth : 0), (30.0, 10.0), (12.0, 10.0), (12.0, 25.0), (0.0, 25.0) });
        if (toCorner) ring.RemoveAt(4);
        var outcome = TestMeshes.Repair(Single(Rotate(ring, deg), 15));
        var v = TestMeshes.AssertClosedShells(outcome.Repaired!);
        var clean = (300 + 180) * 15.0;
        // The input differs from the clean block by depth × feature; the repair moves at most depth × the rest.
        var strip = Math.Abs(depth) * 30 * 15;
        var shortest = ShortestEdge(outcome.Repaired!);
        var again = TestMeshes.Repair(outcome.Repaired!);
        var same = again.Repaired!.Vertices.Count == outcome.Repaired!.Vertices.Count
                   && Math.Abs(TestMeshes.AssertClosedShells(again.Repaired!) - v) < 1e-6 * v;
        var ok = outcome.Report.Status == RepairStatus.Repaired && outcome.Repaired.Objects.Count == 1
                 && shortest >= 0.25 - 1e-6 && Math.Abs(v - clean) <= strip + 1e-3 && same;
        return ok.Label($"deg {deg}, depth {depth}, x {start}..{end}: shortest {shortest}, volume {v} vs {clean} ± {strip}, repeat same {same}, status {outcome.Report.Status}");
    }
}
