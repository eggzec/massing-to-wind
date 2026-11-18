using M2W.Geometry;
using M2W.Geometry.Models;
using Xunit;

namespace M2W.Generator.Tests;

public class HeightFieldTests
{
    private static Block Box(string name, double u0, double v0, double u1, double v1, int floors, double fh = 3.0) =>
        new("bar", new[] { new Rect(u0, v0, u1, v1) }, Enumerable.Repeat(fh, floors)) { Name = name };

    [Fact]
    public void Box_volume_area_height()
    {
        var hf = HeightField.Build(new[] { Box("B01", 0, 0, 20, 10, 6) });
        var s = Assert.Single(hf.Shells());
        Assert.Equal(3600, s.Volume, 9);
        Assert.Equal(200, s.FootprintArea, 9);
        Assert.Equal(18, s.MaxHeight, 9);
        Assert.Equal(0, s.Genus);
    }

    [Fact]
    public void Case_0007_scenario_gives_the_agreement_numbers()
    {
        // AGREEMENT.md: B01 20 x 10 m, six 3 m floors; B02 10 x 10 m, 18 m; 0.3 m gap -> one shell 5454 m3, 303 m2.
        var a = Box("B01", 0, 0, 20, 10, 6);
        var b = Box("B02", 20.3, 0, 30.3, 10, 6);
        var closed = b.MoveEdge(0, 20.3, 20);
        var hf = HeightField.Build(new[] { a, closed });
        var s = Assert.Single(hf.Shells());
        Assert.Equal(5454, s.Volume, 6);
        Assert.Equal(303, s.FootprintArea, 6);
        Assert.Equal(18, s.MaxHeight, 9);
        Assert.Equal(0, s.Genus);
        Assert.Equal(new[] { "B01", "B02" }, s.Blocks);

        var open = HeightField.Build(new[] { a, b });
        Assert.Equal(2, open.ComponentCount);
        Assert.Equal(0.3, open.Distance(0, 1), 9);
    }

    [Fact]
    public void Courtyard_has_genus_one_and_a_pavilion_inside_is_separate()
    {
        var ring = new Block("courtyard", new[]
        {
            new Rect(0, 0, 40, 10), new Rect(0, 30, 40, 40), new Rect(0, 10, 10, 30), new Rect(30, 10, 40, 30),
        }, Enumerable.Repeat(3.0, 4)) { Name = "B01" };
        var pavilion = Box("B02", 17, 17, 23, 23, 1);
        var hf = HeightField.Build(new[] { ring, pavilion });
        var shells = hf.Shells();
        Assert.Equal(2, shells.Count);
        var big = shells.Single(s => s.Blocks.Contains("B01"));
        Assert.Equal(1, big.Genus);
        Assert.Equal(1200, big.FootprintArea, 9);
        Assert.Equal(0, shells.Single(s => s.Blocks.Contains("B02")).Genus);
    }

    [Fact]
    public void Overlapping_blocks_are_a_union_not_a_sum()
    {
        var podium = Box("B01", 0, 0, 40, 30, 3, 4.0);
        var tower = Box("B02", 10, 20, 30, 40, 20, 3.0);
        var hf = HeightField.Build(new[] { podium, tower });
        var s = Assert.Single(hf.Shells());
        // Podium 40x30x12 + tower 20x20x60 - overlap 20x10x12.
        Assert.Equal(14400 + 24000 - 2400, s.Volume, 6);
        Assert.Equal(1200 + 400 - 200, s.FootprintArea, 6);
        Assert.Equal(60, s.MaxHeight, 9);
    }

    [Fact]
    public void Setback_tiers_are_nested()
    {
        var b = new Block("bar", new[] { new Rect(0, 0, 30, 12) }, Enumerable.Repeat(3.0, 6),
            new[] { new Tier(3, Rect.Everything), new Tier(6, Rect.Everything with { U1 = 24 }) }) { Name = "B01" };
        Assert.Equal(30 * 12 * 9 + 24 * 12 * 9, b.Volume(), 6);
        Assert.Equal(18, b.TopAt(1, 1));
        Assert.Equal(9, b.TopAt(27, 1));
    }

    [Fact]
    public void Diagonal_contact_and_narrow_steps_are_detected()
    {
        var hf = HeightField.Build(new[] { Box("B01", 0, 0, 10, 10, 2), Box("B02", 10, 10, 20, 20, 2) });
        Assert.NotNull(hf.FindNonManifoldVertex());
        var step = HeightField.Build(new[] { Box("B01", 0, 0, 10, 10, 2), Box("B02", 10, 0.5, 20, 10, 2) });
        Assert.Null(step.FindNonManifoldVertex());
        Assert.NotNull(step.FindNarrowFeature(2.0));
        Assert.Null(HeightField.Build(new[] { Box("B01", 0, 0, 10, 10, 2), Box("B02", 10, 0, 20, 10, 4) }).FindNarrowFeature(2.0));
    }
}

public class MesherTests
{
    public static IEnumerable<object[]> Shapes()
    {
        var d = new DesignRules(2.5, 4.0);
        foreach (var kind in M2W.Generator.Shapes.Kinds)
            for (var seed = 0; seed < 12; seed++)
                foreach (var style in new[] { FaceStyle.Polygons, FaceStyle.Triangles })
                    yield return new object[] { kind, seed, style };
    }

    private static Block Make(string kind, int seed)
    {
        var b = M2W.Generator.Shapes.Make(kind, new Rng((ulong)seed * 7919 + 13), new DesignRules(2.5, 4.0));
        b.Name = "B01";
        return b;
    }

    private static int GenusOf(Block b) => HeightField.Build(new[] { b }).Genus(0);

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Whole_block_is_a_closed_outward_shell_with_the_block_volume(string kind, int seed, FaceStyle style)
    {
        var b = Make(kind, seed);
        var m = Mesher.MeshBlock(b, style);
        Assert.Equal((0, 0, 0), Topo.EdgeStats(m));
        Assert.Equal(b.Volume(), Topo.Volume(m), 6);
        Assert.Equal(2 - 2 * GenusOf(b), Topo.Euler(m));
        if (style == FaceStyle.Triangles) Assert.All(m.F, f => Assert.Equal(3, f.Length));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Floors_are_closed_shells_that_add_up_to_the_block(string kind, int seed, FaceStyle style)
    {
        var b = Make(kind, seed);
        var floors = Mesher.MeshFloors(b, style);
        Assert.Equal(b.FloorCount, floors.Count);
        foreach (var f in floors) Assert.Equal((0, 0, 0), Topo.EdgeStats(f));
        Assert.Equal(b.Volume(), floors.Sum(Topo.Volume), 6);
        Assert.Equal("B01_F01", floors[0].Name);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Triangles_have_no_slivers_and_no_short_edges(string kind, int seed, FaceStyle style)
    {
        if (style != FaceStyle.Triangles) return;
        var b = Make(kind, seed);
        var m = Mesher.MeshBlock(b, style);
        Assert.All(m.F, t => Assert.True(Topo.MinTriangleAngle(m.V, t) >= 10.0, $"angle {Topo.MinTriangleAngle(m.V, t)}"));
        Assert.True(Topo.MinEdge(m.V, m.F) >= 2.0);
    }

    [Fact]
    public void Polygon_courtyard_roof_falls_back_to_strips_and_stays_closed()
    {
        var ring = new Block("courtyard", new[]
        {
            new Rect(0, 0, 40, 10), new Rect(0, 30, 40, 40), new Rect(0, 10, 10, 30), new Rect(30, 10, 40, 30),
        }, Enumerable.Repeat(3.0, 4)) { Name = "B01" };
        var m = Mesher.MeshBlock(ring, FaceStyle.Polygons);
        Assert.Equal((0, 0, 0), Topo.EdgeStats(m));
        Assert.Equal(0, Topo.Euler(m)); // genus 1
        Assert.Equal(1200 * 12, Topo.Volume(m), 6);
    }
}

public class MeshFaultTests
{
    private static ObjMesh Box(FaceStyle style)
    {
        var b = new Block("bar", new[] { new Rect(0, 0, 20, 10) }, Enumerable.Repeat(3.0, 4)) { Name = "B01" };
        return Mesher.MeshBlock(b, style);
    }

    [Theory]
    [InlineData(FaceStyle.Polygons, false)]
    [InlineData(FaceStyle.Polygons, true)]
    [InlineData(FaceStyle.Triangles, false)]
    [InlineData(FaceStyle.Triangles, true)]
    public void Slivers_keep_the_shell_closed_and_the_volume(FaceStyle style, bool degenerate)
    {
        var m = Box(style);
        var v = Topo.Volume(m);
        var made = MeshFaults.InsertSlivers(m, new Rng(5), 3, 5.0, degenerate, style, new HashSet<int>());
        Assert.Equal(3, made);
        Assert.Equal((0, 0, 0), Topo.EdgeStats(m));
        Assert.Equal(v, Topo.Volume(m), 6);
        Assert.Contains(m.F, f => f.Length == 3 && Topo.MinTriangleAngle(m.V, f) < 5.0);
    }

    [Theory]
    [InlineData(FaceStyle.Polygons)]
    [InlineData(FaceStyle.Triangles)]
    public void Short_edges_keep_the_shell_closed(FaceStyle style)
    {
        var m = Box(style);
        var v = Topo.Volume(m);
        var made = MeshFaults.InsertShortEdges(m, new Rng(9), 2, () => 0.05, style, new HashSet<int>());
        Assert.Equal(2, made);
        Assert.Equal((0, 0, 0), Topo.EdgeStats(m));
        Assert.Equal(v, Topo.Volume(m), 6);
        Assert.True(Topo.MinEdge(m.V, m.F) < 0.051);
    }

    [Fact]
    public void Deleting_faces_opens_the_shell()
    {
        var m = Box(FaceStyle.Triangles);
        Assert.Equal(3, MeshFaults.DeleteFaces(m, new Rng(3), 3, new HashSet<int>()));
        Assert.True(Topo.EdgeStats(m).Free == 9);
        var p = Box(FaceStyle.Polygons);
        Assert.Equal(1, MeshFaults.DeleteBottom(p, new HashSet<int>()));
        Assert.Equal(4, Topo.EdgeStats(p).Free); // the missing ground rectangle
    }

    [Fact]
    public void Flipping_and_splitting()
    {
        var m = Box(FaceStyle.Triangles);
        MeshFaults.FlipFaces(m, new Rng(1), 1.0);
        Assert.True(Topo.Volume(m) < 0);
        var s = Box(FaceStyle.Polygons);
        var count = MeshFaults.SplitVertices(s, new Rng(2), 1.0, 0.0002, 0.5);
        Assert.Equal(s.F.Sum(f => f.Length), count);
        MeshFaults.Compact(s);
        Assert.Equal(count, s.V.Count);
    }
}
