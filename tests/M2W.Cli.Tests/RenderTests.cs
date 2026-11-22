using System.Text.RegularExpressions;
using M2W.Cli.Render;
using M2W.Geometry;
using Xunit;

namespace M2W.Cli.Tests;

/// <summary>Tests for <c>m2w render</c> and <see cref="SvgRenderer"/>.</summary>
public class RenderTests
{
    // ------------------------------------------------------------ fixtures

    private static readonly Vec3[] CubeCorners =
    {
        new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0),
        new(0, 0, 10), new(10, 0, 10), new(10, 10, 10), new(0, 10, 10),
    };

    /// <summary>Quads, counter-clockwise from outside: bottom, top, south, east, north, west.</summary>
    private static readonly int[][] CubeQuads =
    {
        new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 },
        new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 },
    };

    private static Mesh Cube(string name = "shell_000", Vec3 offset = default, double size = 1,
        int? drop = null, int? flip = null, bool triangles = false, bool insideOut = false, Vec3? stretch = null)
    {
        var m = new Mesh();
        var k = stretch ?? new Vec3(1, 1, 1);
        foreach (var c in CubeCorners) m.AddVertex(new Vec3(c.X * k.X, c.Y * k.Y, c.Z * k.Z) * size + offset);
        var o = m.AddObject(name);
        for (var q = 0; q < CubeQuads.Length; q++)
        {
            if (q == drop) continue;
            var f = CubeQuads[q];
            if (q == flip || insideOut) f = f.Reverse().ToArray();
            if (triangles)
            {
                o.Faces.Add(new Face(f[0], f[1], f[2]));
                o.Faces.Add(new Face(f[0], f[2], f[3]));
            }
            else o.Faces.Add(new Face(f));
        }
        return m;
    }

    private static int Count(string svg, string cls) => Regex.Matches(svg, $"class=\"{cls}\"").Count;

    private static string ViewBox(string svg) => Regex.Match(svg, "viewBox=\"([^\"]*)\"").Groups[1].Value;

    private static (int code, string stdout, string stderr) Run(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = new CliApp(new StageSet(), o, e).Run(args);
        return (code, o.ToString(), e.ToString());
    }

    // ------------------------------------------------------------ required by the M5 brief

    [Fact]
    public void Same_input_gives_a_byte_identical_svg()
    {
        using var ws = new Workspace();
        var input = Path.Combine(Workspace.RepoRoot, "cases", "case_0301", "input.obj"); // 29 objects, stacked floors
        var a = Path.Combine(ws.Out, "a.svg");
        var b = Path.Combine(ws.Out, "b", "b.svg");
        Assert.Equal(CliApp.Ok, Run("render", input, "--out", a, "--title", "same").code);
        Assert.Equal(CliApp.Ok, Run("render", input, "--out", b, "--title", "same").code);
        var bytes = File.ReadAllBytes(a);
        Assert.Equal(bytes, File.ReadAllBytes(b));
        Assert.NotEqual(0xEF, bytes[0]);                    // no BOM
        Assert.DoesNotContain((byte)'\r', bytes);           // LF only

        // In-process too.
        Assert.Equal(SvgRenderer.Render(Cube(triangles: true)).Svg, SvgRenderer.Render(Cube(triangles: true)).Svg);
    }

    [Fact]
    public void A_cube_renders_three_visible_faces()
    {
        var r = SvgRenderer.Render(Cube());
        Assert.Equal(RenderStyle.Output, r.Style);
        Assert.Equal(3, r.Polygons);                         // back faces culled
        Assert.Equal(3, r.FrontPolygons);
        Assert.Equal(3, Count(r.Svg, "f"));

        // Triangulated (as repaired.obj is): coplanar triangles merge back to the same 3 faces.
        var t = SvgRenderer.Render(Cube(triangles: true));
        Assert.Equal(3, t.Polygons);
        Assert.Equal(3, Count(t.Svg, "f"));

        // Input style draws all six, see-through: three in front, three behind.
        var i = SvgRenderer.Render(Cube(name: "B01"));
        Assert.Equal(RenderStyle.Input, i.Style);
        Assert.Equal(3, Count(i.Svg, "f"));
        Assert.Equal(3, Count(i.Svg, "b"));
    }

    [Fact]
    public void A_mesh_with_one_hole_shows_red_free_edges()
    {
        var r = SvgRenderer.Render(Cube(name: "B01", drop: 1)); // no roof
        Assert.Equal(4, r.FreeEdges);
        var free = Regex.Match(r.Svg, "<path class=\"free\" d=\"([^\"]*)\"[^>]*stroke=\"(#[0-9a-f]{6})\"");
        Assert.True(free.Success, "no free-edge path");
        Assert.Equal(SvgRenderer.FreeEdgeColour, free.Groups[2].Value);
        Assert.Equal(4, free.Groups[1].Value.Count(c => c == 'M')); // one segment per free edge

        var closed = SvgRenderer.Render(Cube(name: "B01"));
        Assert.Equal(0, closed.FreeEdges);
        Assert.DoesNotContain("class=\"free\"", closed.Svg);
    }

    [Fact]
    public void One_flipped_face_is_marked()
    {
        var r = SvgRenderer.Render(Cube(name: "B01", flip: 3));
        Assert.Equal(1, r.FlippedFaces);
        Assert.Equal(1, Count(r.Svg, "x"));
        var marked = r.Svg.Split('\n').Single(l => l.Contains("class=\"x\""));
        Assert.Contains($"stroke=\"{SvgRenderer.FlippedColour}\"", marked);

        Assert.Equal(0, SvgRenderer.Render(Cube(name: "B01")).FlippedFaces);
        // A flipped quad split into two triangles: both triangles are marked.
        Assert.Equal(2, SvgRenderer.Render(Cube(name: "B01", flip: 0, triangles: true)).FlippedFaces);
        // In a repaired file too (output style): a flipped face is drawn and marked, not culled.
        Assert.Equal(1, Count(SvgRenderer.Render(Cube(flip: 2)).Svg, "x"));
    }

    [Fact]
    public void Frame_gives_an_identical_viewbox_for_a_before_after_pair()
    {
        using var ws = new Workspace();
        Directory.CreateDirectory(ws.Out);
        var before = Path.Combine(ws.Out, "before.obj");
        var after = Path.Combine(ws.Out, "after.obj");
        var mb = Cube(name: "B01", offset: new Vec3(500, 200, 0), stretch: new Vec3(6, 1, 0.3));                  // 60 × 10 × 3 m slab
        var ma = Cube(name: "shell_000", offset: new Vec3(480, 190, 0), triangles: true, stretch: new Vec3(4, 1, 10)); // 40 × 10 × 100 m
        ObjWriter.WriteFile(mb, before);
        ObjWriter.WriteFile(ma, after);

        var bs = Path.Combine(ws.Out, "before.svg");
        var a = Path.Combine(ws.Out, "after.svg");
        Assert.Equal(CliApp.Ok, Run("render", before, "--out", bs, "--frame", after).code);
        Assert.Equal(CliApp.Ok, Run("render", after, "--out", a, "--frame", before).code);
        string sb = File.ReadAllText(bs), sa = File.ReadAllText(a);
        Assert.Equal(ViewBox(sb), ViewBox(sa));

        // Without --frame each is fitted to itself, and these two shapes would get different frames.
        Assert.NotEqual(ViewBox(SvgRenderer.Render(mb).Svg), ViewBox(SvgRenderer.Render(ma).Svg));

        // Same scale and position: the before polygons land on exactly the pixels they get in a drawing of both meshes.
        var both = new Mesh();
        both.Vertices.AddRange(mb.Vertices);
        both.Vertices.AddRange(ma.Vertices);
        both.Objects.Add(mb.Objects[0]);
        both.Objects.Add(new MeshObject("B02", ma.Objects[0].Faces.Select(f => new Face(f.Select(i => i + 8)))));
        var union = SvgRenderer.Render(both).Svg;
        Assert.Equal(ViewBox(sb), ViewBox(union));
        var paths = Regex.Matches(sb, "class=\"[fb]\" d=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(6, paths.Count);
        Assert.All(paths, d => Assert.Contains(d, union));
    }

    // ------------------------------------------------------------ more behaviour

    [Fact]
    public void An_object_that_is_inside_out_is_marked_as_a_whole()
    {
        var r = SvgRenderer.Render(Cube(name: "B01", insideOut: true));
        Assert.Equal(6, r.FlippedFaces);
        Assert.Equal(0, r.FreeEdges);
    }

    [Fact]
    public void Duplicate_vertices_within_5_mm_do_not_show_as_free_edges()
    {
        var m = Cube(name: "B01");
        // Give the roof its own copies of its four corners, 1 mm off.
        var roof = m.Objects[0].Faces[1];
        var copies = roof.Select(i => m.AddVertex(m.Vertices[i] + new Vec3(0.001, 0, 0))).ToArray();
        m.Objects[0].Faces[1] = new Face(copies);
        var r = SvgRenderer.Render(m);
        Assert.Equal(0, r.FreeEdges);
        Assert.Equal(0, r.FlippedFaces);
    }

    [Fact]
    public void A_zero_area_sliver_triangle_closes_the_surface_and_is_not_drawn()
    {
        // Triangulated cube; the south face is routed through p, the midpoint of edge 0-1, and the zero-area
        // triangle (0, 1, p) closes it against the bottom (the generator's degenerate sliver).
        var m = Cube(triangles: true);
        var p = m.AddVertex((m.Vertices[0] + m.Vertices[1]) / 2);
        var faces = m.Objects[0].Faces;
        var south = faces.FindIndex(f => f.SequenceEqual(new[] { 0, 1, 5 }));
        Assert.True(south >= 0);
        faces[south] = new Face(0, p, 5);
        faces.Add(new Face(p, 1, 5));
        faces.Add(new Face(0, 1, p));

        var r = SvgRenderer.Render(m);
        Assert.Equal(0, r.FreeEdges);
        Assert.Equal(0, r.FlippedFaces);
        Assert.Equal(3, r.Polygons);
    }

    [Fact]
    public void Stacked_floors_show_through_and_merge_their_own_triangles()
    {
        var m = new Mesh();
        var lower = Cube(name: "B01_F01", triangles: true);
        var upper = Cube(name: "B01_F02", offset: new Vec3(0, 0, 10), triangles: true);
        foreach (var part in new[] { lower, upper })
        {
            var baseIndex = m.Vertices.Count;
            m.Vertices.AddRange(part.Vertices);
            var o = m.AddObject(part.Objects[0].Name);
            o.Faces.AddRange(part.Objects[0].Faces.Select(f => new Face(f.Select(i => i + baseIndex))));
        }
        var r = SvgRenderer.Render(m);
        Assert.Equal(RenderStyle.Input, r.Style);
        Assert.Equal(12, r.Polygons);   // 6 per floor: triangles merged per object although the slab edges are shared
        Assert.Equal(0, r.FreeEdges);
        Assert.Equal(0, r.FlippedFaces);
        Assert.Contains("fill-opacity", r.Svg);
    }

    [Fact]
    public void Numbers_are_invariant_with_two_decimals_and_no_ids_or_dates()
    {
        var svg = SvgRenderer.Render(Cube(offset: new Vec3(-1234.5, 678.25, 0)), new RenderOptions { Title = "a < b & c" }).Svg;
        Assert.Contains("a &lt; b &amp; c", svg);
        Assert.DoesNotContain(" id=", svg);
        foreach (Match m in Regex.Matches(svg, @"[ML](-?[0-9.]+) (-?[0-9.]+)"))
            Assert.Matches(@"^-?\d+\.\d\d$", m.Groups[1].Value);
        Assert.DoesNotContain(",", Regex.Match(svg, "d=\"[^\"]*\"").Value);
    }

    [Theory]
    [InlineData("render")]
    [InlineData("render", "x.obj")]
    [InlineData("render", "--out", "x.svg")]
    [InlineData("render", "x.obj", "--out")]
    [InlineData("render", "x.obj", "--out", "x.svg", "--seed", "1")]
    public void Bad_render_arguments_print_usage_and_exit_2(params string[] args)
    {
        var (code, _, stderr) = Run(args);
        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains("usage:", stderr);
    }

    [Fact]
    public void A_missing_or_broken_obj_is_bad_input()
    {
        using var ws = new Workspace();
        Directory.CreateDirectory(ws.Out);
        var broken = Path.Combine(ws.Out, "broken.obj");
        File.WriteAllText(broken, "v 0 0 0\nf 1 2 3\n");
        Assert.Equal(CliApp.BadInput, Run("render", Path.Combine(ws.Out, "none.obj"), "--out", Path.Combine(ws.Out, "x.svg")).code);
        var (code, _, stderr) = Run("render", broken, "--out", Path.Combine(ws.Out, "x.svg"));
        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains("bad input", stderr);
        Assert.False(File.Exists(Path.Combine(ws.Out, "x.svg")));
    }

}
