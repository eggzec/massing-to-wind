using M2W.Checker;
using M2W.Geometry;
using Xunit;

namespace M2W.Checker.Tests;

public class GeomTests
{
    private static Vec3 V(double x, double y, double z) => new(x, y, z);
    private static readonly Tri Ground = new(V(0, 0, 0), V(10, 0, 0), V(0, 10, 0));

    [Fact]
    public void Point_triangle_distance_regions()
    {
        Assert.Equal(5, Geom.PointTriangleDistance(V(1, 1, 5), Ground), 12);      // above the face
        Assert.Equal(5, Geom.PointTriangleDistance(V(-3, -4, 0), Ground), 12);    // nearest the corner
        Assert.Equal(2, Geom.PointTriangleDistance(V(5, -2, 0), Ground), 12);     // nearest an edge
        Assert.Equal(0, Geom.PointTriangleDistance(V(2, 2, 0), Ground), 12);      // inside
    }

    [Fact]
    public void Segment_segment_distance()
    {
        Assert.Equal(1, Geom.SegmentSegmentDistance(V(0, 0, 0), V(1, 0, 0), V(0.5, -1, 1), V(0.5, 1, 1)), 12); // skew
        Assert.Equal(2, Geom.SegmentSegmentDistance(V(0, 0, 0), V(1, 0, 0), V(0, 2, 0), V(1, 2, 0)), 12);       // parallel
        Assert.Equal(0, Geom.SegmentSegmentDistance(V(0, 0, 0), V(2, 0, 0), V(1, -1, 0), V(1, 1, 0)), 12);      // crossing
    }

    [Fact]
    public void Crossing_triangles_touch()
    {
        var pierce = new Tri(V(2, 2, -1), V(3, 2, 1), V(2, 3, 1));
        Assert.True(Geom.TrianglesTouch(Ground, pierce));
        Assert.Equal(0, Geom.TriangleTriangleDistance(Ground, pierce));
    }

    [Fact]
    public void Separated_triangles_do_not_touch_and_distance_is_exact()
    {
        var above = new Tri(V(1, 1, 0.7), V(2, 1, 0.7), V(1, 2, 0.7));
        Assert.False(Geom.TrianglesTouch(Ground, above));
        Assert.Equal(0.7, Geom.TriangleTriangleDistance(Ground, above), 12);
    }

    [Fact]
    public void Coplanar_overlap_and_containment_touch_but_coplanar_apart_does_not()
    {
        var inside = new Tri(V(1, 1, 0), V(2, 1, 0), V(1, 2, 0));       // contained, no edge crossings
        var overlap = new Tri(V(-1, 1, 0), V(3, 1, 0), V(1, -3, 0));
        var apart = new Tri(V(20, 0, 0), V(30, 0, 0), V(20, 10, 0));
        Assert.True(Geom.TrianglesTouch(Ground, inside));
        Assert.True(Geom.TrianglesTouch(Ground, overlap));
        Assert.False(Geom.TrianglesTouch(Ground, apart));
        Assert.Equal(10, Geom.TriangleTriangleDistance(Ground, apart), 12);
    }

    [Fact]
    public void Winding_number_is_one_inside_a_closed_box_and_zero_outside()
    {
        var s = Shells.Of(Kit.Boxes((0, 0, 0, 2, 2, 2)))[0];
        Assert.Equal(1, s.Winding(V(1, 1, 1)), 9);
        Assert.Equal(0, s.Winding(V(3, 1, 1)), 9);
        Assert.Equal(0, s.SolidDistance(V(1, 1, 1)));
        Assert.Equal(1, s.SolidDistance(V(3, 1, 1)), 12);
    }

    [Fact]
    public void Assignment_finds_the_cheapest_matching()
    {
        var cost = new double[,] { { 4, 1, 3 }, { 2, 0, 5 }, { 3, 2, 2 } };
        Assert.Equal(new[] { 1, 0, 2 }, Assignment.Solve(cost)); // 1 + 2 + 2 = 5
        Assert.Equal(new[] { -1, 0 }, Assignment.Solve(new double[,] { { 6 }, { 1 } })); // more rows than columns
    }
}

internal static class Shells
{
    /// <summary>One Shell per object, triangles as written.</summary>
    public static List<Shell> Of(Mesh m) =>
        m.Objects.Select(o => new Shell(o.Name, m.Vertices, o.Faces.Select(f => (f[0], f[1], f[2])).ToList())).ToList();

}
