using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Repair.Model;
using M2W.Repair.Steps;

namespace M2W.Repair;

/// <summary>The repair steps in order. Each step logs what it did to the <see cref="RepairLog"/>.</summary>
public static class RepairPipeline
{
    public static List<OutputShell> Run(Mesh input, Vec3 shift, Tolerances tol, RepairLog log)
    {
        // 1. Local coordinates, one independent vertex list per input object.
        var solids = Split(input, shift);
        log.Op("shiftToLocal", count: input.Vertices.Count, extra: new Dictionary<string, object>
        {
            ["shift_m"] = new[] { shift.X, shift.Y, shift.Z },
        });
        if (solids.Count == 0)
        {
            log.Fail("input has no faces");
            return new List<OutputShell>();
        }

        // 2. Weld, drop degenerate polygons, triangulate, drop duplicate triangles.
        int welded = 0, degenerate = 0, split = 0, failedTri = 0, duplicates = 0;
        foreach (var s in solids)
        {
            welded += Welder.Weld(s, tol.Weld);
            degenerate += FaceCleanup.RemoveDegeneratePolygons(s);
            var (sp, f) = FaceCleanup.Triangulate(s);
            split += sp; failedTri += f;
            duplicates += FaceCleanup.RemoveDuplicateTriangles(s);
            FaceCleanup.Compact(s);
        }
        log.Op("weld", count: welded, extra: new Dictionary<string, object> { ["tolerance_m"] = tol.Weld });
        if (degenerate > 0) log.Op("removeDegenerateFaces", count: degenerate);
        if (duplicates > 0) log.Op("removeDuplicateFaces", count: duplicates);
        if (split > 0) log.Op("triangulate", count: split);
        if (failedTri > 0) log.Fail($"{failedTri} input polygons are not simple; fan-triangulated");
        solids.RemoveAll(s => s.Triangles.Count == 0);

        // Until the union step exists, each object becomes one shell.
        var shells = new List<OutputShell>();
        foreach (var s in solids)
        {
            var check = ShellCheck.Check(s.Vertices, s.Triangles);
            if (!check.Ok) log.Fail($"object {s.Name}: not a valid closed shell ({check})");
            shells.Add(new OutputShell { Vertices = s.Vertices, Triangles = s.Triangles, Blocks = new[] { s.Block } });
        }
        return shells;
    }

    /// <summary>One <see cref="Solid"/> per input object with faces, vertices shifted to local coordinates.</summary>
    public static List<Solid> Split(Mesh input, Vec3 shift)
    {
        var result = new List<Solid>();
        foreach (var obj in input.Objects)
        {
            if (obj.Faces.Count == 0) continue;
            var name = obj.Name.Length == 0 ? "unnamed" : obj.Name;
            var s = new Solid(name, Solid.BlockOf(name));
            var map = new Dictionary<int, int>();
            foreach (var f in obj.Faces)
            {
                var poly = new int[f.Count];
                for (var k = 0; k < f.Count; k++)
                {
                    if (!map.TryGetValue(f[k], out var li))
                    {
                        li = s.Vertices.Count;
                        s.Vertices.Add(input.Vertices[f[k]] + shift);
                        map[f[k]] = li;
                    }
                    poly[k] = li;
                }
                s.Polygons.Add(poly);
            }
            result.Add(s);
        }
        return result;
    }
}
