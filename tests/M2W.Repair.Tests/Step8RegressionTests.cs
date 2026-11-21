using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Geometry.Stages;
using static M2W.Repair.Tests.Step5GapTests;

namespace M2W.Repair.Tests;

/// <summary>Fixed cases for the defects the M4 properties found (each was a random site; here simplified).</summary>
public class Step8RegressionTests
{
    private static double ShortestEdge(Mesh m)
    {
        var best = double.PositiveInfinity;
        foreach (var o in m.Objects)
            foreach (var f in o.Faces)
                for (var k = 0; k < 3; k++)
                    best = Math.Min(best, (m.Vertices[f[(k + 1) % 3]] - m.Vertices[f[k]]).Length);
        return best;
    }

    private static RepairOutcome RepairValid(Mesh m, int shells)
    {
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(shells, outcome.Repaired!.Objects.Count);
        TestMeshes.AssertClosedShells(outcome.Repaired!);
        Assert.True(ShortestEdge(outcome.Repaired!) >= 0.25 - 1e-6);
        var again = TestMeshes.Repair(outcome.Repaired!);
        Assert.Equal(TestMeshes.AssertClosedShells(outcome.Repaired!), TestMeshes.AssertClosedShells(again.Repaired!), 6);
        return outcome;
    }

    [Fact]
    public void Gap_bridge_next_to_a_slightly_higher_block_is_flushed_with_all_its_roof()
    {
        // B02 (3.2) is bridged to B01 (3.5) and overlaps B03 (3.3): raising the bridge and B02 to 3.3 must go on to
        // 3.5, or a 0.2 m step stays.
        var m = new Mesh();
        AddPrism(m, "B01", Rect(23.1, 32.05, 28.13, 37.55), 0, 3.5);
        AddPrism(m, "B02", Rect(28.534, 31.543, 46.114, 50.983), 0, 3.2);
        AddPrism(m, "B03", Rect(30.157, 21.566, 40.187, 39.816), 0, 3.3);
        var outcome = RepairValid(m, 1);
        Assert.All(outcome.Repaired!.Vertices, v => Assert.True(v.Z == 0 || v.Z == 3.5));
    }

    [Fact]
    public void Roofs_touching_only_at_a_corner_with_a_small_step_are_flushed()
    {
        var m = new Mesh();
        AddPrism(m, "B01", Rect(0, 0, 10, 10), 0, 3.0);
        AddPrism(m, "B02", Rect(10, 10, 20, 20), 0, 3.1);
        RepairValid(m, 1);
    }

    [Fact]
    public void Gap_with_a_facing_overlap_shorter_than_minEdge_is_still_bridged()
    {
        // The walls face each other over 0.033 m only; that strip would vanish when the plan is snapped.
        var m = new Mesh();
        AddPrism(m, "B01", Rect(9.72, 16.95, 25.41, 36.6), 0, 3.5);
        AddPrism(m, "B02", Rect(26.384, 7.893, 33.774, 16.983), 0, 3.6);
        var outcome = RepairValid(m, 1);
        Assert.Contains(outcome.Report.Operations, o => o.Op == "closeGap");
    }

    [Fact]
    public void Thin_ledge_takes_the_roof_it_runs_along_not_a_taller_block_at_its_end()
    {
        // B02's 0.15 m ledge (3.6 m) runs along B02 (7.2 m); its end touches B01 (9.3 m) through a gap bridge.
        var m = new Mesh();
        AddPrism(m, "B01", Rect(0.2, 25.98, 9.04, 42.84), 0, 9.3);
        AddPrism(m, "B02_F01", Rect(9.744, 22.853, 19.254, 27.884), 0, 3.6);
        AddPrism(m, "B02_F02", Rect(9.744, 22.853, 19.254, 27.733), 3.6, 7.2);
        var outcome = RepairValid(m, 1);
        Assert.All(outcome.Repaired!.Vertices.Where(v => v.Z > 7.2 + 1e-9), v => Assert.True(v.X < 9.744 + 0.5, $"fin at {v}"));
    }

    [Fact]
    public void Blocks_corner_to_corner_on_several_levels_settle_into_one_shell()
    {
        // B03 and B04 approach corner to corner (0.62 m) at three levels, and B04 has a 0.17 m ledge.
        var m = new Mesh();
        AddPrism(m, "B03_F01", Rect(25.237, 0.396, 35.047, 7.996), 0, 3.7);
        AddPrism(m, "B03_F02", Rect(25.237, 0.396, 35.047, 7.996), 3.7, 7.4);
        AddPrism(m, "B04_F01", Rect(35.309, -4.426, 50.788, -0.164), 0, 3.2);
        AddPrism(m, "B04_F02", Rect(35.309, -4.426, 50.788, -0.336), 3.2, 6.4);
        AddPrism(m, "B04_F03", Rect(35.309, -4.426, 50.788, -0.336), 6.4, 9.6);
        RepairValid(m, 1);
    }
}
