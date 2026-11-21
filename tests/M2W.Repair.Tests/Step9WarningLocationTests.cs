using System.Text.Json;
using M2W.Geometry;
using M2W.Geometry.Models;
using static M2W.Repair.Tests.Step5GapTests;

namespace M2W.Repair.Tests;

/// <summary>M5: warnings and operations about a region say where it is (plan box, local coordinates) and how big.</summary>
public class Step9WarningLocationTests
{
    private static double[] Box(RepairOperation op) =>
        op.Extra!["bbox_m"].EnumerateArray().Select(e => e.GetDouble()).ToArray();

    [Fact]
    public void A_canopy_filled_down_to_the_ground_is_located_in_the_warning_and_the_operation()
    {
        // A slab at 4.0–4.4 m over the front of B01 that sticks out 3 m: the 6 × 3 m overhang has nothing below it.
        var m = new Mesh();
        TestMeshes.AddBox(m, "B01", 0, 0, 0, 10, 10, 10);
        TestMeshes.AddBox(m, "B02", 2, 8, 4.0, 8, 13, 4.4);
        var o = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Partial, o.Report.Status);
        var w = Assert.Single(o.Report.Warnings, x => x.Contains("has no support"));
        Assert.Contains("block B02", w);
        Assert.Contains("1 region of 18 m² at [2, 10, 8, 13]", w);
        var op = Assert.Single(o.Report.Operations, x => x.Op == "fillUnsupported");
        Assert.Equal("B02", op.Block);
        Assert.Equal(1, op.Count);
        Assert.Equal(new[] { 2.0, 10, 8, 13 }, Box(op));
        Assert.Equal(18, op.Extra!["area_m2"].GetDouble(), 6);
        Assert.Equal(1, op.Extra!["regions"].GetArrayLength());
    }

    [Fact]
    public void Map_coordinates_are_reported_in_local_coordinates()
    {
        var m = new Mesh();
        TestMeshes.AddBox(m, "B01", 530000, 180000, 0, 530010, 180010, 10);
        TestMeshes.AddBox(m, "B02", 530002, 180008, 4.0, 530008, 180013, 4.4);
        var o = TestMeshes.Repair(m, origin: new Vec3(530000, 180000, 0));
        var op = Assert.Single(o.Report.Operations, x => x.Op == "fillUnsupported");
        Assert.Equal(new[] { 2.0, 10, 8, 13 }, Box(op));
    }

    [Fact]
    public void Two_canopies_of_one_block_are_two_regions()
    {
        var m = new Mesh();
        TestMeshes.AddBox(m, "B01", 0, 0, 0, 30, 10, 10);
        // Two objects of the same name: one block with two slabs.
        TestMeshes.AddBox(m, "B02", 2, 8, 4.0, 8, 13, 4.4);
        TestMeshes.AddBox(m, "B02", 20, 8, 4.0, 24, 12, 4.4);
        var o = TestMeshes.Repair(m);
        var op = Assert.Single(o.Report.Operations, x => x.Op == "fillUnsupported");
        Assert.Equal(2, op.Count);
        Assert.Equal(18 + 8, op.Extra!["area_m2"].GetDouble(), 6);
        Assert.Equal(new[] { 2.0, 10, 24, 13 }, Box(op));
        Assert.Contains(o.Report.Warnings, x => x.Contains("2 regions, 26 m² in all, within [2, 10, 24, 13]"));
    }

    [Fact]
    public void A_closed_gap_reports_its_width_and_where_it_was_bridged()
    {
        var m = new Mesh();
        AddPrism(m, "B01", Rect(0, 0, 20, 10), 0, 18);
        AddPrism(m, "B02", Rect(20.3, 0, 30.3, 10), 0, 18);
        var o = TestMeshes.Repair(m);
        var op = Assert.Single(o.Report.Operations, x => x.Op == "closeGap");
        Assert.Equal(0.3, op.Width_m!.Value, 6);
        Assert.Equal(new[] { 20.0, 0, 20.3, 10 }, Box(op));
    }

    [Fact]
    public void Blocks_touching_at_a_corner_are_a_union_not_a_gap()
    {
        var m = new Mesh();
        AddPrism(m, "B01", Rect(0, 0, 10, 10), 0, 9);
        AddPrism(m, "B02", Rect(10, 10, 20, 20), 0, 9);
        var o = TestMeshes.Repair(m);
        Assert.DoesNotContain(o.Report.Operations, x => x.Op == "closeGap");
        var u = Assert.Single(o.Report.Operations, x => x.Op == "unionBlocks");
        Assert.Equal(2, u.Count);
        Assert.Equal(1, u.Extra!["pointContacts"].GetInt32());
        TestMeshes.AssertClosedShells(o.Repaired!);
    }

    [Fact]
    public void An_object_holding_two_solids_is_reported_as_two_parts()
    {
        var m = new Mesh();
        var o1 = TestMeshes.AddBox(m, "B01", 0, 0, 0, 10, 10, 6);
        var tmp = new Mesh();
        TestMeshes.AddBox(tmp, "x", 10.4, 0, 0, 20, 10, 6);
        var b = m.Vertices.Count;
        m.Vertices.AddRange(tmp.Vertices);
        foreach (var f in tmp.Objects[0].Faces) o1.Faces.Add(new Face(f.Select(i => i + b)));
        var o = TestMeshes.Repair(m);
        var gap = Assert.Single(o.Report.Operations, x => x.Op == "closeGap");
        Assert.Equal(new[] { "B01#1", "B01#2" }, gap.Pair);
        Assert.Equal(0.4, gap.Width_m!.Value, 6);
        Assert.Contains(o.Report.Warnings, w => w.Contains("object B01 holds 2 separate closed solids"));
        Assert.Equal(1, o.Repaired!.Objects.Count);
    }

    [Fact]
    public void Location_fields_serialise_as_plain_numbers()
    {
        var m = new Mesh();
        TestMeshes.AddBox(m, "B01", 0, 0, 0, 10, 10, 10);
        TestMeshes.AddBox(m, "B02", 2, 8, 4.0, 8, 13, 4.4);
        var json = JsonSerializer.Serialize(TestMeshes.Repair(m).Report.Operations.Single(x => x.Op == "fillUnsupported"));
        Assert.Contains("\"bbox_m\":[2,10,8,13]", json);
    }
}
