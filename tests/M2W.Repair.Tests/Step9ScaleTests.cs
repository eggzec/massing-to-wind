using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Geometry.Stages;
using M2W.Repair.Union;
using Xunit.Abstractions;

namespace M2W.Repair.Tests;

/// <summary>M5: runtime on a district, and report detail that does not depend on how the input is split.</summary>
public class Step9ScaleTests(ITestOutputHelper output)
{
    private const int Seed = 20261122;

    /// <summary>Target 5 s for about 500 blocks (owner, 2026-11-21); the limit leaves 3× headroom for slow runs.</summary>
    private const double LimitSeconds = 15;

    private static readonly Regex PartName = new(@"city#(\d+)");

    private static string ObjText(RepairOutcome o) => ObjWriter.WriteString(o.Repaired!);

    /// <summary>The operations as JSON, part names of the one-object form written as the matching object names.</summary>
    private static List<string> Ops(RepairOutcome o) => o.Report.Operations
        .Select(op => PartName.Replace(JsonSerializer.Serialize(op), m => $"B{int.Parse(m.Groups[1].Value):0000}"))
        .ToList();

    private (RepairOutcome outcome, double seconds) TimedRepair(Mesh m)
    {
        var sw = Stopwatch.StartNew();
        var o = TestMeshes.Repair(m);
        return (o, sw.Elapsed.TotalSeconds);
    }

    private static void AssertValidCity(RepairOutcome o, SyntheticCity city)
    {
        Assert.NotEqual(RepairStatus.Failed, o.Report.Status);
        TestMeshes.AssertClosedShells(o.Repaired!);
        // Partial only because the raised slabs are filled down to the ground (documented); nothing else is wrong.
        Assert.All(o.Report.Warnings, w => Assert.True(
            w.Contains("has no support below part of it") || w.Contains("below minAngleDeg") || w.Contains("separate closed solids"),
            w));
        Assert.Equal(city.Slabs > 0, o.Report.Status == RepairStatus.Partial);
        Assert.All(o.Repaired!.Vertices, v => Assert.True(v.Z >= 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_district_of_500_blocks_repairs_within_the_time_limit_as_one_object_and_as_many(bool rotated)
    {
        var city = SyntheticCity.Build(Seed, 500, rotated);
        Assert.InRange(city.Parts.Count, 500, 530);
        Assert.True(city.SmallGaps > 20 && city.Tiers > 50 && city.Slabs > 0);

        var (many, tMany) = TimedRepair(city.AsObjects());
        var (one, tOne) = TimedRepair(city.AsOneObject());
        output.WriteLine($"{city.Parts.Count} parts ({city.Houses} houses, {city.Tiers} tiers, {city.Slabs} slabs, " +
                         $"{city.SmallGaps} gaps below 1 m), rotated={rotated}: one object per part {tMany:0.00} s, " +
                         $"one object {tOne:0.00} s; {many.Report.ShellCount} shells");
        AssertValidCity(many, city);
        AssertValidCity(one, city);
        Assert.True(tMany < LimitSeconds, $"one object per part took {tMany:0.0} s");
        Assert.True(tOne < LimitSeconds, $"one object took {tOne:0.0} s");
        Assert.Equal(ObjText(many), ObjText(one));
        Assert.Equal(Ops(many), Ops(one));
    }

    [Theory]
    [InlineData(120, false)]
    [InlineData(120, true)]
    public void The_same_geometry_as_one_object_or_as_many_gives_the_same_output_and_operations(int parts, bool rotated)
    {
        var city = SyntheticCity.Build(Seed + 1, parts, rotated);
        var many = TestMeshes.Repair(city.AsObjects());
        var one = TestMeshes.Repair(city.AsOneObject());

        Assert.Equal(ObjText(many), ObjText(one));
        Assert.Equal(Ops(many), Ops(one)); // op names, counts and every field; only the block names differ
        Assert.Equal(many.Report.Status, one.Report.Status);
        Assert.Contains(one.Report.Warnings, w => w.Contains($"holds {city.Parts.Count} separate closed solids"));
        // Touching parts are unions, never a closeGap of width 0.
        Assert.DoesNotContain(many.Report.Operations, op => op.Op == "closeGap" && op.Width_m < 1e-6);
        Assert.Contains(many.Report.Operations, op => op.Op == "closeGap" && op.Width_m > 0);
        Assert.Contains(many.Report.Operations, op => op.Op == "unionBlocks");
    }

    [Theory]
    [InlineData(150, false)]
    [InlineData(150, true)]
    public void Gap_closing_near_close_walls_only_agrees_with_scanning_every_level(int parts, bool rotated)
    {
        var mesh = SyntheticCity.Build(Seed + 2, parts, rotated).AsObjects();
        var fast = ObjText(TestMeshes.Repair(mesh));
        try
        {
            GapCloser.FullScan = true;
            Assert.Equal(fast, ObjText(TestMeshes.Repair(mesh)));
        }
        finally
        {
            GapCloser.FullScan = false;
        }
    }

    [Fact]
    public void A_city_repairs_to_the_same_bytes_twice()
    {
        var mesh = SyntheticCity.Build(Seed + 3, 100, rotated: true).AsOneObject();
        Assert.Equal(ObjText(TestMeshes.Repair(mesh)), ObjText(TestMeshes.Repair(mesh)));
    }
}
