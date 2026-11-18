using FsCheck;
using FsCheck.Xunit;
using M2W.Geometry;
using M2W.Geometry.Models;

namespace M2W.Repair.Tests;

public class Step3GroundSnapTests
{
    [Property(MaxTest = 50)]
    public Property Block_within_groundSnap_lands_on_ground_with_height_kept(NormalFloat f)
    {
        var dz = Math.Clamp(f.Get, -1, 1) * 0.5 * 0.9; // within ±0.9 × groundSnap
        var outcome = TestMeshes.Repair(TestMeshes.Box(0, 0, dz, 12, 8, dz + 15));
        var b = MeshMeasures.BoundingBox(outcome.Repaired!);
        return (outcome.Report.Status == RepairStatus.Repaired && b.Min.Z == 0 && Math.Abs(b.Max.Z - 15) < 1e-6).ToProperty();
    }

    [Fact]
    public void All_floors_of_a_block_move_together()
    {
        var m = new Mesh();
        TestMeshes.AddBox(m, "B01_F01", 0, 0, 0.3, 10, 10, 3.3);
        TestMeshes.AddBox(m, "B01_F02", 0, 0, 3.3, 10, 10, 6.3);
        TestMeshes.AddBox(m, "B02", 20, 0, -0.2, 30, 10, 9.8);
        var outcome = TestMeshes.Repair(m);
        var snaps = outcome.Report.Operations.Where(o => o.Op == "groundSnap").ToList();
        Assert.Equal(new[] { "B01", "B02" }, snaps.Select(s => s.Block));
        Assert.Equal(-0.3, snaps[0].Extra!["dz_m"].GetDouble(), 9);
        Assert.Equal(0.2, snaps[1].Extra!["dz_m"].GetDouble(), 9);
        var zs = outcome.Repaired!.Vertices.Select(v => v.Z).Distinct().OrderBy(z => z).ToArray();
        Assert.Equal(new[] { 0, 3, 6, 10.0 }, zs.Select(z => Math.Round(z, 6)));
    }

    [Fact]
    public void Block_beyond_groundSnap_is_not_moved()
    {
        // A block standing on another one (podium roof at 4 m) must stay where it is.
        var outcome = TestMeshes.Repair(TestMeshes.Box(0, 0, 4, 10, 10, 10, "B02"));
        Assert.DoesNotContain(outcome.Report.Operations, o => o.Op == "groundSnap");
        Assert.Equal(4, MeshMeasures.BoundingBox(outcome.Repaired!).Min.Z);
    }

    [Fact]
    public void Block_sunk_deeper_than_groundSnap_is_partial_not_hidden()
    {
        var outcome = TestMeshes.Repair(TestMeshes.Box(0, 0, -2, 10, 10, 8));
        Assert.Equal(RepairStatus.Partial, outcome.Report.Status);
        Assert.Contains(outcome.Report.Warnings, w => w.Contains("below ground"));
    }
}
