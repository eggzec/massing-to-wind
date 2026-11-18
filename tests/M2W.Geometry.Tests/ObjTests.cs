using System.Text;
using FsCheck.Xunit;
using M2W.Geometry.Models;
using Xunit;

namespace M2W.Geometry.Tests;

public class ObjRoundTripTests
{
    private static Mesh ReadExample() => ObjReader.ReadFile(Path.Combine(RepoPaths.Example0007, CaseFiles.Input));

    [Fact]
    public void Example_read_write_read_gives_identical_geometry()
    {
        var first = ReadExample();
        var text = ObjWriter.WriteString(first);
        var second = ObjReader.ReadString(text);

        Assert.True(first.GeometryEquals(second));
        Assert.Equal(text, ObjWriter.WriteString(second)); // and the text itself is stable
    }

    [Fact]
    public void Example_round_trips_through_a_file_byte_for_byte()
    {
        var dir = Directory.CreateTempSubdirectory("m2w-obj-");
        try
        {
            var a = Path.Combine(dir.FullName, "a.obj");
            var b = Path.Combine(dir.FullName, "b.obj");
            ObjWriter.WriteFile(ReadExample(), a);
            ObjWriter.WriteFile(ObjReader.ReadFile(a), b);
            Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Example_has_the_documented_structure()
    {
        var m = ReadExample();
        Assert.Equal(56, m.Vertices.Count);
        Assert.Equal(new[] { "B01_F01", "B01_F02", "B01_F03", "B01_F04", "B01_F05", "B01_F06", "B02" },
            m.Objects.Select(o => o.Name));
        Assert.All(m.Objects, o => Assert.Equal(6, o.Faces.Count));
        Assert.All(m.AllFaces, f => Assert.Equal(4, f.Count));
        Assert.Equal(new Vec3(530000, 180000, 0), m.Vertices[0]);
        Assert.Equal(new Vec3(530020.3, 180010, 18), m.Vertices[55]);
    }

    [Fact]
    public void Example_objects_are_closed_outward_boxes()
    {
        var m = ReadExample();
        foreach (var o in m.Objects)
        {
            Assert.All(MeshMeasures.EdgeUseCounts(o.Faces).Values, n => Assert.Equal(2, n));
            var expected = o.Name == "B02" ? 10 * 10 * 18 : 20 * 10 * 3;
            Assert.Equal(expected, MeshMeasures.SignedVolume(m, o.Faces), 6);
        }
        // Input total before the gap is closed: 6 floors × 600 + 1800.
        Assert.Equal(5400, MeshMeasures.SignedVolume(m), 6);
    }

    /// <summary>Any mesh: write → read → write → read gives the same geometry and the same text both times.</summary>
    [Property(MaxTest = 200)]
    public bool Random_meshes_round_trip(int seed)
    {
        var original = RandomMesh(new Random(seed));
        var text1 = ObjWriter.WriteString(original);
        var a = ObjReader.ReadString(text1);
        var text2 = ObjWriter.WriteString(a);
        var b = ObjReader.ReadString(text2);
        // An unnamed first object with no faces has no lines in OBJ, so it is the one thing that cannot survive.
        var representable = original.Objects.Count(o => o.Name.Length > 0 || o.Faces.Count > 0);
        return text1 == text2 && a.GeometryEquals(b) && a.Objects.Count == representable
               && a.AllFaces.SequenceEqual(original.AllFaces) && a.Objects.Select(o => o.Name).SequenceEqual(
                   original.Objects.Where(o => o.Name.Length > 0 || o.Faces.Count > 0).Select(o => o.Name));
    }

    [Fact]
    public void Seed_17_unnamed_empty_first_object_is_dropped_but_nothing_else_changes()
    {
        Assert.True(Random_meshes_round_trip(17));
    }

    private static Mesh RandomMesh(Random r)
    {
        var m = new Mesh();
        var nv = r.Next(3, 60);
        var scale = r.Next(3) switch { 0 => 1.0, 1 => 1000.0, _ => 1e6 };
        for (var i = 0; i < nv; i++)
            m.AddVertex(new Vec3((r.NextDouble() - 0.5) * scale, (r.NextDouble() - 0.5) * scale, r.NextDouble() * 100));
        var no = r.Next(0, 5);
        for (var o = 0; o < no; o++)
        {
            var obj = m.AddObject(r.Next(4) == 0 && o == 0 ? "" : $"B{o:00}_F{r.Next(1, 20):00}");
            var nf = r.Next(0, 8);
            for (var f = 0; f < nf; f++)
                obj.Faces.Add(new Face(Enumerable.Range(0, r.Next(3, 7)).Select(_ => r.Next(nv))));
        }
        return m;
    }
}

public class ObjReaderTests
{
    [Fact]
    public void Reads_ignored_lines_comments_slashes_relative_indices_and_crlf()
    {
        const string text =
            "# header\r\nmtllib x.mtl\r\nv 0 0 0\r\nv 1 0 0 # inline comment\r\nv 0 1 0\r\nvn 0 0 1\r\nvt 0 0\r\n" +
            "g group\r\ns off\r\nusemtl m\r\no A\r\nf 1/1/1 2//1 3\r\nf -3 -2 -1\r\n\r\n";
        var m = ObjReader.ReadString(text);
        Assert.Equal(3, m.Vertices.Count);
        var o = Assert.Single(m.Objects);
        Assert.Equal("A", o.Name);
        Assert.Equal(new Face(0, 1, 2), o.Faces[0]);
        Assert.Equal(new Face(0, 1, 2), o.Faces[1]);
    }

    [Fact]
    public void Faces_before_any_o_line_go_to_an_unnamed_first_object_and_round_trip()
    {
        var m = ObjReader.ReadString("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\no B\nf 3 2 1\n");
        Assert.Equal(new[] { "", "B" }, m.Objects.Select(o => o.Name));
        Assert.True(m.GeometryEquals(ObjReader.ReadString(ObjWriter.WriteString(m))));
    }

    [Fact]
    public void Empty_objects_are_kept()
    {
        var m = ObjReader.ReadString("o A\no B\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        Assert.Equal(new[] { 0, 1 }, m.Objects.Select(o => o.Faces.Count));
        Assert.True(m.GeometryEquals(ObjReader.ReadString(ObjWriter.WriteString(m))));
    }

    [Fact]
    public void Forward_references_are_allowed_if_the_vertex_exists_by_the_end()
    {
        var m = ObjReader.ReadString("f 1 2 3\nv 0 0 0\nv 1 0 0\nv 0 1 0\n");
        Assert.Equal(new Face(0, 1, 2), m.Objects[0].Faces[0]);
    }

    [Theory]
    [InlineData("v 0 0\n", 1)]                                   // too few coordinates
    [InlineData("v 0 0 abc\n", 1)]                               // not a number
    [InlineData("v 0 0 NaN\n", 1)]                               // not finite
    [InlineData("v 0 0 1,5\n", 1)]                               // culture-specific decimal comma
    [InlineData("v 0 0 0\nv 1 0 0\nf 1 2\n", 3)]                 // face with 2 vertices
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 0 1 2\n", 4)]      // index 0
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 4\n", 4)]      // out of range
    [InlineData("v 0 0 0\nf -2 -1 1\n", 2)]                      // relative index before the first vertex
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 x\n", 4)]      // not an index
    [InlineData("o\n", 1)]                                       // o without a name
    [InlineData("v 0 0 0\nl 1 1\n", 2)]                          // unsupported keyword
    public void Rejects_bad_input_with_the_line_number(string text, int line)
    {
        var ex = Assert.Throws<ObjFormatException>(() => ObjReader.ReadString(text));
        Assert.Equal(line, ex.Line);
    }
}

public class ObjWriterTests
{
    [Fact]
    public void Writes_six_decimals_lf_no_bom_and_one_based_indices()
    {
        var m = new Mesh();
        m.AddVertex(new Vec3(530000, 180000.1234567, -0.0));
        m.AddVertex(new Vec3(-1e-9, 1, 2));
        m.AddVertex(new Vec3(0.5, 0.25, 0.125));
        m.AddObject("shell_000").Faces.Add(new Face(0, 1, 2));

        var text = ObjWriter.WriteString(m, new[] { "two\nlines" });
        Assert.Equal(
            "# two\n# lines\n" +
            "v 530000.000000 180000.123457 0.000000\n" +
            "v 0.000000 1.000000 2.000000\n" +
            "v 0.500000 0.250000 0.125000\n" +
            "o shell_000\nf 1 2 3\n",
            text);

        var path = Path.Combine(Path.GetTempPath(), $"m2w-writer-{Guid.NewGuid():N}.obj");
        try
        {
            ObjWriter.WriteFile(m, path);
            var bytes = File.ReadAllBytes(path);
            Assert.NotEqual(0xEF, bytes[0]);              // no UTF-8 BOM
            Assert.DoesNotContain((byte)'\r', bytes);     // LF only
            Assert.Equal(Encoding.UTF8.GetBytes(ObjWriter.WriteString(m)), bytes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Refuses_invalid_meshes()
    {
        var bad = new Mesh();
        bad.AddVertex(Vec3.Zero);
        bad.AddObject("A").Faces.Add(new Face(0, 1, 2));
        Assert.Throws<InvalidOperationException>(() => ObjWriter.WriteString(bad));

        var unnamedSecond = new Mesh(new[] { Vec3.Zero, new Vec3(1, 0, 0), new Vec3(0, 1, 0) },
            new[] { new MeshObject("A"), new MeshObject("") });
        Assert.Throws<InvalidOperationException>(() => ObjWriter.WriteString(unnamedSecond));

        var nan = new Mesh();
        nan.AddVertex(new Vec3(double.NaN, 0, 0));
        Assert.Throws<InvalidOperationException>(() => ObjWriter.WriteString(nan));
    }

}

public class MeshMeasuresTests
{
    /// <summary>Unit cube at the given offset, outward counter-clockwise quads.</summary>
    internal static Mesh Cube(Vec3 offset)
    {
        var m = new Mesh();
        for (var z = 0; z < 2; z++)
            for (var y = 0; y < 2; y++)
                for (var x = 0; x < 2; x++)
                    m.AddVertex(offset + new Vec3(x, y, z));
        // index = x + 2y + 4z
        m.AddObject("B01").Faces.AddRange(new[]
        {
            new Face(0, 2, 3, 1), // bottom (−Z)
            new Face(4, 5, 7, 6), // top (+Z)
            new Face(0, 1, 5, 4), // front (−Y)
            new Face(2, 6, 7, 3), // back (+Y)
            new Face(0, 4, 6, 2), // left (−X)
            new Face(1, 3, 7, 5), // right (+X)
        });
        return m;
    }

    [Fact]
    public void Cube_volume_area_bounds_and_edges()
    {
        var m = Cube(Vec3.Zero);
        Assert.Equal(1.0, MeshMeasures.SignedVolume(m), 12);
        Assert.Equal(6.0, MeshMeasures.SurfaceArea(m), 12);
        Assert.Equal(new Bounds(Vec3.Zero, new Vec3(1, 1, 1)), MeshMeasures.BoundingBox(m));
        var edges = MeshMeasures.EdgeUseCounts(m.AllFaces);
        Assert.Equal(12, edges.Count);
        Assert.All(edges.Values, n => Assert.Equal(2, n));
        Assert.Equal(new Vec3(0, 0, -1), MeshMeasures.AreaVector(m, m.Objects[0].Faces[0]));
    }

    [Fact]
    public void Flipped_cube_has_negative_volume_and_open_cube_has_free_edges()
    {
        var m = Cube(Vec3.Zero);
        var faces = m.Objects[0].Faces;
        for (var i = 0; i < faces.Count; i++) faces[i] = faces[i].Reversed();
        Assert.Equal(-1.0, MeshMeasures.SignedVolume(m), 12);

        faces.RemoveAt(1);
        Assert.Equal(4, MeshMeasures.EdgeUseCounts(faces).Values.Count(n => n == 1));
    }

    [Fact]
    public void Volume_keeps_precision_at_map_coordinates()
    {
        var m = Cube(new Vec3(530000.123, 180000.456, 7));
        Assert.Equal(1.0, MeshMeasures.SignedVolume(m), 9);
    }
}
