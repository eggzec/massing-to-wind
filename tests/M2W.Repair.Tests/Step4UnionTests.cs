using FsCheck;
using FsCheck.Xunit;
using M2W.Geometry;
using M2W.Geometry.Models;

namespace M2W.Repair.Tests;

public class Step4UnionTests
{
    private static Mesh Stacked(int floors, double h = 3)
    {
        var m = new Mesh();
        for (var f = 0; f < floors; f++) TestMeshes.AddBox(m, $"B01_F{f + 1:00}", 0, 0, f * h, 20, 10, (f + 1) * h);
        return m;
    }

    private static int ShellCount(Mesh m) => m.Objects.Count;

    [Fact]
    public void Stacked_floors_become_one_shell_without_internal_floors()
    {
        var outcome = TestMeshes.Repair(Stacked(6));
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(1, ShellCount(outcome.Repaired!));
        Assert.Equal(20 * 10 * 18, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.Contains(outcome.Report.Operations, o => o.Op == "mergeStackedFloors" && o.Block == "B01" && o.Count == 6);
        // No vertex at an internal floor height survives.
        Assert.All(outcome.Repaired!.Vertices, v => Assert.True(v.Z == 0 || v.Z == 18));
    }

    [Fact]
    public void Crossing_bars_union_to_one_shell_with_the_union_volume()
    {
        var m = new Mesh();
        TestMeshes.AddBox(m, "B01", 0, 10, 0, 40, 20, 12);
        TestMeshes.AddBox(m, "B02", 15, 0, 0, 25, 30, 12);
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(1, ShellCount(outcome.Repaired!));
        Assert.Equal((400 + 300 - 100) * 12, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.Contains(outcome.Report.Operations, o => o.Op == "unionBlocks" && o.Count == 2);
    }

    [Fact]
    public void Tower_through_podium_keeps_both_heights()
    {
        var m = new Mesh();
        TestMeshes.AddBox(m, "B01", 0, 0, 0, 40, 30, 6);
        TestMeshes.AddBox(m, "B02", 10, 10, 0, 20, 20, 45);
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(1, ShellCount(outcome.Repaired!));
        Assert.Equal(1200 * 6 + 100 * 39, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.Equal(45, MeshMeasures.BoundingBox(outcome.Repaired!).Max.Z);
    }

    [Fact]
    public void Touching_walls_with_different_heights_merge()
    {
        var m = new Mesh();
        TestMeshes.AddBox(m, "B01", 0, 0, 0, 10, 10, 9);
        TestMeshes.AddBox(m, "B02", 10, 0, 0, 25, 10, 15);
        TestMeshes.AddBox(m, "B03", 25, 0, 0, 30, 10, 9); // terrace of three
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(1, ShellCount(outcome.Repaired!));
        Assert.Equal(100 * 9 + 150 * 15 + 50 * 9, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
    }

    [Fact]
    public void Separate_blocks_stay_separate()
    {
        var m = new Mesh();
        TestMeshes.AddBox(m, "B01", 0, 0, 0, 10, 10, 9);
        TestMeshes.AddBox(m, "B02", 15, 0, 0, 25, 10, 12);
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(2, ShellCount(outcome.Repaired!));
        Assert.DoesNotContain(outcome.Report.Operations, o => o.Op == "unionBlocks");
    }

    [Fact]
    public void Courtyard_union_keeps_its_hole()
    {
        var outcome = TestMeshes.Repair(Step2OrientationAndHolesTests.Courtyard());
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        var m = outcome.Repaired!;
        Assert.Equal(4800, TestMeshes.AssertClosedShells(m), 6);
        // Euler characteristic of a torus-like shell is 0 (genus 1).
        var faces = m.Objects[0].Faces.Count;
        var edges = MeshMeasures.EdgeUseCounts(m.Objects[0].Faces).Count;
        var verts = m.Objects[0].Faces.SelectMany(f => f).Distinct().Count();
        Assert.Equal(0, verts - edges + faces);
    }

    [Fact]
    public void Roof_step_lower_than_minEdge_is_made_flush_upwards()
    {
        var m = new Mesh();
        TestMeshes.AddBox(m, "B01", 0, 0, 0, 20, 10, 17.4);
        TestMeshes.AddBox(m, "B02", 20, 0, 0, 30, 10, 17.5);
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(300 * 17.5, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.Contains(outcome.Report.Operations, o => o.Op == "flushRoofStep");
    }

    [Fact]
    public void Roof_step_of_minEdge_or_more_is_kept()
    {
        var m = new Mesh();
        TestMeshes.AddBox(m, "B01", 0, 0, 0, 20, 10, 17.0);
        TestMeshes.AddBox(m, "B02", 20, 0, 0, 30, 10, 17.3);
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(200 * 17 + 100 * 17.3, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.DoesNotContain(outcome.Report.Operations, o => o.Op == "flushRoofStep");
    }

    [Fact]
    public void Wall_split_at_a_neighbours_height_has_no_T_junctions()
    {
        // B02 (15 m) touches the middle part of B01's (9 m) east wall: B01's wall column must carry a vertex at 9 m
        // where B02's wall starts above it, and the shell must still close.
        var m = new Mesh();
        TestMeshes.AddBox(m, "B01", 0, 0, 0, 10, 30, 9);
        TestMeshes.AddBox(m, "B02", 10, 10, 0, 20, 20, 15);
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(300 * 9 + 100 * 15, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
    }

    // ------------------------------------------------------------------ properties

    private static Mesh RandomSite(System.Random rnd, out double rotation)
    {
        var m = new Mesh();
        var n = rnd.Next(1, 5);
        for (var b = 0; b < n; b++)
        {
            var x = rnd.Next(0, 40); var y = rnd.Next(0, 40);
            var w = rnd.Next(4, 20); var d = rnd.Next(4, 20);
            var floors = rnd.Next(1, 5);
            for (var f = 0; f < floors; f++)
                TestMeshes.AddBox(m, $"B{b + 1:00}_F{f + 1:00}", x, y, f * 3.0, x + w, y + d, (f + 1) * 3.0);
        }
        rotation = rnd.NextDouble() * 2 * Math.PI;
        return m;
    }

    private static Mesh Transform(Mesh m, double angle, Vec3 offset)
    {
        var c = Math.Cos(angle); var s = Math.Sin(angle);
        var r = new Mesh();
        r.Vertices.AddRange(m.Vertices.Select(v =>
            new Vec3(Math.Round(c * v.X - s * v.Y, 6), Math.Round(s * v.X + c * v.Y, 6), v.Z) + offset));
        foreach (var o in m.Objects) r.Objects.Add(new MeshObject(o.Name, o.Faces));
        return r;
    }

    private static (int shells, double volume, int euler) Measure(Mesh m)
    {
        var vol = TestMeshes.AssertClosedShells(m);
        var euler = 0;
        foreach (var o in m.Objects)
            euler += o.Faces.SelectMany(f => f).Distinct().Count() - MeshMeasures.EdgeUseCounts(o.Faces).Count + o.Faces.Count;
        return (m.Objects.Count, vol, euler);
    }

    /// <summary>Moving and rotating the input does not change shell count, volume or topology.</summary>
    [Property(MaxTest = 40)]
    public Property Rotating_and_moving_the_input_keeps_volume_and_topology(PositiveInt seed)
    {
        var rnd = new System.Random(seed.Get);
        var site = RandomSite(rnd, out var angle);
        var origin = new Vec3(530000 + rnd.Next(0, 1000), 180000 + rnd.Next(0, 1000), rnd.Next(0, 50));
        var a = TestMeshes.Repair(site);
        var b = TestMeshes.Repair(Transform(site, angle, origin), origin);
        var ma = Measure(a.Repaired!);
        var mb = Measure(b.Repaired!);
        return (a.Report.Status == RepairStatus.Repaired && b.Report.Status == RepairStatus.Repaired
                && ma.shells == mb.shells && ma.euler == mb.euler
                && Math.Abs(ma.volume - mb.volume) <= 1e-4 * Math.Max(1, ma.volume))
            .Label($"plain {ma} vs moved {mb}, {string.Join("; ", b.Report.Warnings)}");
    }

    /// <summary>Repairing the repaired output again changes nothing (idempotent).</summary>
    [Property(MaxTest = 40)]
    public Property Repair_twice_equals_repair_once(PositiveInt seed)
    {
        var rnd = new System.Random(seed.Get);
        var site = Transform(RandomSite(rnd, out var angle), angle, Vec3.Zero);
        var once = TestMeshes.Repair(site);
        var twice = TestMeshes.Repair(once.Repaired!);
        var m1 = Measure(once.Repaired!);
        var m2 = Measure(twice.Repaired!);
        return (twice.Report.Status == RepairStatus.Repaired && m1.shells == m2.shells && m1.euler == m2.euler
                && Math.Abs(m1.volume - m2.volume) <= 1e-6 * Math.Max(1, m1.volume))
            .Label($"once {m1} vs twice {m2}");
    }
}
