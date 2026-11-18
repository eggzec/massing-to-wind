using FsCheck;
using FsCheck.Xunit;
using M2W.Geometry;
using M2W.Geometry.Models;

namespace M2W.Repair.Tests;

public class Step2OrientationAndHolesTests
{
    /// <summary>A square courtyard block: outer 30 × 30, inner 10 × 10 hole, all quads, closed and outward.</summary>
    internal static Mesh Courtyard(double h = 6)
    {
        var m = new Mesh();
        P(0, 0); P(30, 0); P(30, 30); P(0, 30); P(10, 10); P(20, 10); P(20, 20); P(10, 20);
        void P(double x, double y) { m.Vertices.Add(new Vec3(x, y, 0)); }
        for (var i = 0; i < 8; i++) m.Vertices.Add(m.Vertices[i] with { Z = h });
        var o = m.AddObject("B01");
        int O(int k) => k % 4; int I(int k) => 4 + k % 4; const int T = 8;
        for (var k = 0; k < 4; k++)
        {
            o.Faces.Add(new Face(O(k) + T, O(k + 1) + T, I(k + 1) + T, I(k) + T)); // top
            o.Faces.Add(new Face(I(k), I(k + 1), O(k + 1), O(k)));                 // bottom
            o.Faces.Add(new Face(O(k), O(k + 1), O(k + 1) + T, O(k) + T));         // outer wall
            o.Faces.Add(new Face(I(k + 1), I(k), I(k) + T, I(k + 1) + T));         // courtyard wall
        }
        return m;
    }

    [Fact]
    public void Courtyard_helper_is_valid()
    {
        var outcome = TestMeshes.Repair(Courtyard());
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(800 * 6, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
    }

    [Fact]
    public void Inside_out_object_is_turned_outward()
    {
        var m = TestMeshes.Box(0, 0, 0, 10, 10, 3);
        var o = m.Objects[0];
        for (var i = 0; i < o.Faces.Count; i++) o.Faces[i] = o.Faces[i].Reversed();
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(300, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.Contains(outcome.Report.Operations, x => x.Op == "fixOrientation" && x.Count == 12);
    }

    [Property(MaxTest = 60)]
    public Property Any_subset_of_flipped_faces_is_fixed(PositiveInt seed)
    {
        var rnd = new System.Random(seed.Get);
        var m = Courtyard();
        var o = m.Objects[0];
        for (var i = 0; i < o.Faces.Count; i++)
            if (rnd.NextDouble() < 0.4) o.Faces[i] = o.Faces[i].Reversed();
        var outcome = TestMeshes.Repair(m);
        return (outcome.Report.Status == RepairStatus.Repaired
                && Math.Abs(TestMeshes.AssertClosedShells(outcome.Repaired!) - 4800) < 1e-6).ToProperty();
    }

    [Fact]
    public void Deleted_roof_and_ground_faces_are_closed()
    {
        // Non-adjacent faces, as the openSurface fault deletes them. (Two adjacent deleted faces leave one bent loop,
        // which a single planar fill cannot close exactly; see PROGRESS 2026-11-19.)
        var m = TestMeshes.Box(0, 0, 0, 20, 10, 9);
        m.Objects[0].Faces.RemoveAt(1); // roof
        m.Objects[0].Faces.RemoveAt(0); // ground
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(1800, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.Contains(outcome.Report.Operations, x => x.Op == "closeHoles" && x.Count == 2);
    }

    [Fact]
    public void Deleted_courtyard_ground_face_is_closed_as_a_ring_not_two_disks()
    {
        var m = Courtyard();
        m.Objects[0].Faces.RemoveAll(f => f.All(i => m.Vertices[i].Z == 0)); // all four bottom quads
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(4800, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
    }

    [Fact]
    public void Split_vertex_on_a_neighbours_edge_is_joined_not_filled()
    {
        // The roof quad gets an extra vertex on its south edge; the south wall does not have it.
        var m = TestMeshes.Box(0, 0, 0, 20, 10, 9);
        var mid = m.AddVertex(new Vec3(7, 0, 9));
        var faces = m.Objects[0].Faces;
        faces[1] = new Face(4, mid, 5, 6, 7);
        var outcome = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, outcome.Report.Status);
        Assert.Equal(1800, TestMeshes.AssertClosedShells(outcome.Repaired!), 6);
        Assert.Contains(outcome.Report.Operations, x => x.Op == "splitTJunctions" && x.Count == 1);
        Assert.DoesNotContain(outcome.Report.Operations, x => x.Op == "closeHoles");
    }
}
