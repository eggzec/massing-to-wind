using M2W.Geometry;
using M2W.Geometry.Models;
using static M2W.Repair.Tests.Step5GapTests;

namespace M2W.Repair.Tests;

public class Step7CombinedTests
{
    private static readonly Vec3 Origin = new(530000, 180000, 0);

    /// <summary>
    /// Far coordinates + stacked floors with a 0.1 m ledge + a 0.4 m gap + a block floating 0.3 m up with every face
    /// flipped + duplicate vertices, all in one case.
    /// </summary>
    private static Mesh Combined()
    {
        var m = new Mesh();
        (double, double)[] Far((double, double)[] r) => r.Select(p => (p.Item1 + Origin.X, p.Item2 + Origin.Y)).ToArray();
        AddPrism(m, "B01_F01", Far(Rect(0, 0, 20, 10.1)), 0, 3);
        AddPrism(m, "B01_F02", Far(Rect(0, 0, 20, 10)), 3, 6);
        AddPrism(m, "B02", Far(Rect(20.4, 0, 30, 10)), 0.3, 9.3);
        var b02 = m.Objects[^1];
        for (var i = 0; i < b02.Faces.Count; i++) b02.Faces[i] = new Face(b02.Faces[i].Reverse().ToArray());
        // Duplicate every vertex of B01_F02 and point half of its faces at the copies.
        var f02 = m.Objects[1];
        var copy = new Dictionary<int, int>();
        for (var i = 0; i < f02.Faces.Count; i += 2)
            f02.Faces[i] = new Face(f02.Faces[i].Select(v =>
            {
                if (!copy.TryGetValue(v, out var c)) { c = m.Vertices.Count; m.Vertices.Add(m.Vertices[v]); copy[v] = c; }
                return c;
            }).ToArray());
        return m;
    }

    [Fact]
    public void All_faults_in_one_case_give_one_clean_shell()
    {
        var outcome = TestMeshes.Repair(Combined(), Origin);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        var mesh = outcome.Repaired!;
        Assert.Single(mesh.Objects);
        var v = TestMeshes.AssertClosedShells(mesh);
        // B01 with the ledge absorbed (20 × 10.1 × 6), the gap filled to the lower block (0.4 × 10 × 6), B02 snapped
        // down (9.6 × 10 × 9); the 0.1 m step left at the gap's end moves by less than 0.1 m² in plan.
        var expected = 20 * 10.1 * 6 + 0.4 * 10 * 6 + 9.6 * 10 * 9;
        Assert.InRange(v, expected - 0.1 * 9, expected + 0.1 * 9);
        var box = MeshMeasures.BoundingBox(mesh);
        Assert.Equal(0, box.Min.Z);
        Assert.Equal(9, box.Max.Z, 9);
        Assert.InRange(box.Min.X, -1e-9, 1e-9); // local coordinates
        foreach (var op in new[] { "fixOrientation", "groundSnap", "closeGap", "raiseThinRegion", "mergeStackedFloors", "unionBlocks" })
            Assert.Contains(outcome.Report.Operations, o => o.Op == op);
        foreach (var o in mesh.Objects)
            foreach (var f in o.Faces)
                for (var k = 0; k < 3; k++)
                    Assert.True((mesh.Vertices[f[(k + 1) % 3]] - mesh.Vertices[f[k]]).Length >= 0.25 - 1e-6);
    }

    [Fact]
    public void Combined_case_is_deterministic_and_stable_under_a_second_repair()
    {
        var a = TestMeshes.Repair(Combined(), Origin).Repaired!;
        var b = TestMeshes.Repair(Combined(), Origin).Repaired!;
        Assert.Equal(a.Vertices, b.Vertices);
        var again = TestMeshes.Repair(a).Repaired!;
        Assert.Equal(TestMeshes.AssertClosedShells(a), TestMeshes.AssertClosedShells(again), 6);
        Assert.Equal(a.Vertices.Count, again.Vertices.Count);
    }
}
