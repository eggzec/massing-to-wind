using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Geometry.Stages;
using M2W.Repair.Model;

namespace M2W.Repair.Tests;

/// <summary>M5 minor item 1 (D-047): the order of the objects in input.obj does not change repaired.obj.</summary>
public class Step10OrderTests
{
    private const int Seed = 20261123;

    private static string ObjText(RepairOutcome o) => ObjWriter.WriteString(o.Repaired!);

    /// <summary>The same mesh with its objects in a seeded random order (each object keeps its name and faces).</summary>
    private static Mesh ShuffleObjects(Mesh m, int seed)
    {
        var rng = new Random(seed);
        var objs = m.Objects.OrderBy(_ => rng.Next()).ToList();
        return new Mesh(m.Vertices, objs);
    }

    /// <summary>
    /// The same geometry with its vertex list and the faces of each object in a seeded random order, objects
    /// shuffled too: nothing but numbering and order differs.
    /// </summary>
    private static Mesh ShuffleEverything(Mesh m, int seed)
    {
        var rng = new Random(seed);
        var perm = Enumerable.Range(0, m.Vertices.Count).OrderBy(_ => rng.Next()).ToArray(); // old index -> new
        var verts = new Vec3[m.Vertices.Count];
        for (var i = 0; i < perm.Length; i++) verts[perm[i]] = m.Vertices[i];
        var objs = m.Objects.OrderBy(_ => rng.Next())
            .Select(o => new MeshObject(o.Name, o.Faces.OrderBy(_ => rng.Next()).Select(f => new Face(f.Select(k => perm[k])))))
            .ToList();
        return new Mesh(verts, objs);
    }

    /// <summary>One object holding every part, the parts in a seeded random order.</summary>
    private static Mesh OneObjectShuffled(Mesh perPart, int seed)
    {
        var rng = new Random(seed);
        var faces = perPart.Objects.OrderBy(_ => rng.Next()).SelectMany(o => o.Faces);
        return new Mesh(perPart.Vertices, new[] { new MeshObject("city", faces) });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shuffling_the_objects_of_a_city_gives_the_same_bytes(bool rotated)
    {
        var city = SyntheticCity.Build(Seed, 120, rotated);
        var reference = TestMeshes.Repair(city.AsObjects());
        Assert.True(reference.Report.ShellCount > 5, "the city should come out as several shells");
        var expected = ObjText(reference);
        foreach (var shuffle in new[] { 1, 2, 3, 4 })
        {
            var o = TestMeshes.Repair(ShuffleObjects(city.AsObjects(), shuffle));
            Assert.Equal(reference.Report.Status, o.Report.Status);
            Assert.Equal(expected, ObjText(o));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void One_object_or_one_object_per_part_in_any_order_gives_the_same_bytes(bool rotated)
    {
        var city = SyntheticCity.Build(Seed + 1, 120, rotated);
        var expected = ObjText(TestMeshes.Repair(city.AsObjects()));
        Assert.Equal(expected, ObjText(TestMeshes.Repair(city.AsOneObject())));
        foreach (var shuffle in new[] { 5, 6, 7 })
        {
            Assert.Equal(expected, ObjText(TestMeshes.Repair(OneObjectShuffled(city.AsObjects(), shuffle))));
            Assert.Equal(expected, ObjText(TestMeshes.Repair(ShuffleObjects(city.AsObjects(), shuffle))));
        }
    }

    [Fact]
    public void Renumbering_vertices_and_reordering_faces_gives_the_same_bytes()
    {
        var city = SyntheticCity.Build(Seed + 2, 80, rotated: true);
        var expected = ObjText(TestMeshes.Repair(city.AsObjects()));
        foreach (var shuffle in new[] { 8, 9, 10 })
            Assert.Equal(expected, ObjText(TestMeshes.Repair(ShuffleEverything(city.AsObjects(), shuffle))));
    }

    [Fact]
    public void Separate_blocks_in_any_order_come_out_sorted_by_their_lowest_ground_corner()
    {
        var m = new Mesh();
        TestMeshes.AddBox(m, "B03", 20, 0, 0, 25, 5, 9);
        TestMeshes.AddBox(m, "B01", 0, 10, 0, 6, 16, 3);
        TestMeshes.AddBox(m, "B02", 0, 0, 0, 4, 4, 6);
        var o = TestMeshes.Repair(m);
        Assert.Equal(RepairStatus.Repaired, o.Report.Status);
        var firstCorner = o.Repaired!.Objects
            .Select(obj => obj.Faces.SelectMany(f => f).Select(i => o.Repaired.Vertices[i])
                .OrderBy(v => v.X).ThenBy(v => v.Y).ThenBy(v => v.Z).First())
            .ToList();
        Assert.Equal(new[] { new Vec3(0, 0, 0), new Vec3(0, 10, 0), new Vec3(20, 0, 0) }, firstCorner);
        // Within each shell the vertices are written in position order and each triangle starts at its lowest number.
        var mesh = o.Repaired;
        var start = 0;
        foreach (var obj in mesh.Objects)
        {
            var used = obj.Faces.SelectMany(f => f).Distinct().OrderBy(i => i).ToList();
            Assert.Equal(Enumerable.Range(start, used.Count), used);
            var pos = used.Select(i => mesh.Vertices[i]).ToList();
            Assert.Equal(pos.OrderBy(v => v.X).ThenBy(v => v.Y).ThenBy(v => v.Z), pos);
            Assert.All(obj.Faces, f => Assert.True(f[0] < f[1] && f[0] < f[2]));
            start += used.Count;
        }
    }

    [Fact]
    public void Canonical_order_keeps_every_triangle_with_its_orientation()
    {
        var shell = new OutputShell
        {
            Vertices = new List<Vec3> { new(1, 0, 0), new(0, 0, 0), new(0, 1, 0), new(0, 0, 1) },
            Triangles = new List<Tri> { new(1, 2, 0), new(1, 0, 3), new(0, 2, 3), new(2, 1, 3) },
        };
        var r = CanonicalOrder.Renumber(shell);
        // Each oriented triangle by its corner positions, rotated to start at its lowest corner.
        static HashSet<(Vec3, Vec3, Vec3)> Tris(OutputShell s) => s.Triangles.Select(t =>
        {
            var (a, b, d) = (s.Vertices[t.A], s.Vertices[t.B], s.Vertices[t.C]);
            var pts = new[] { a, b, d };
            var k = Array.IndexOf(pts, pts.OrderBy(v => v.X).ThenBy(v => v.Y).ThenBy(v => v.Z).First());
            return (pts[k], pts[(k + 1) % 3], pts[(k + 2) % 3]);
        }).ToHashSet();
        Assert.Equal(Tris(shell), Tris(r));
        Assert.Equal(new Vec3(0, 0, 0), r.Vertices[0]);
    }
}
