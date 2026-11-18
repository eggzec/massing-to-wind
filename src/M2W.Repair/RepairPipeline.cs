using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Repair.Model;
using M2W.Repair.Steps;
using M2W.Repair.Union;

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

        // 3. Consistent outward orientation, T-junctions on free edges, hole closing.
        int reversed = 0, tSplits = 0, holes = 0, open = 0;
        foreach (var s in solids)
        {
            var before = new HashSet<Tri>(s.Triangles.Select(t => t.Canonical()));
            Orienter.MakeConsistent(s);
            var hr = HoleFiller.Close(s, tol.Weld);
            tSplits += hr.TJunctionsSplit; holes += hr.HolesFilled; open += hr.HolesLeftOpen;
            if (hr.TJunctionsSplit > 0 || hr.HolesFilled > 0) Orienter.MakeConsistent(s);
            Orienter.MakeOutward(s);
            reversed += s.Triangles.Count(t => !before.Contains(t.Canonical()) && before.Contains(t.Reversed().Canonical()));
            if (hr.HolesLeftOpen > 0) log.Fail($"object {s.Name}: {hr.HolesLeftOpen} holes could not be closed");
        }
        if (reversed > 0) log.Op("fixOrientation", count: reversed);
        if (tSplits > 0) log.Op("splitTJunctions", count: tSplits);
        if (holes > 0) log.Op("closeHoles", count: holes);

        // 4. Ground snap: blocks within groundSnap of Z = 0 are moved onto it.
        foreach (var mv in GroundSnapper.Snap(solids, tol.GroundSnap))
            log.Op("groundSnap", block: mv.Block, extra: new Dictionary<string, object> { ["dz_m"] = Math.Round(mv.Dz, 9) });
        foreach (var g in solids.GroupBy(s => s.Block))
        {
            var minZ = g.SelectMany(s => s.Vertices).Min(v => v.Z);
            if (minZ < -1e-9) log.Fail($"block {g.Key} reaches {minZ:0.###} m below ground, beyond groundSnap; not moved");
        }

        // 5. Union as a 2.5D height field: merges stacked floors, overlaps and touching walls; one shell per footprint.
        var union = new HeightFieldUnion(solids, tol.Weld, tol.MinEdge).Run();
        foreach (var p in union.Problems) log.Fail(p);
        foreach (var (from, to, faces) in union.FlushedSteps)
            log.Op("flushRoofStep", count: faces, extra: new Dictionary<string, object>
            {
                ["from_m"] = from, ["to_m"] = to, ["minEdge_m"] = tol.MinEdge,
            });
        if (union.Shells.Count == 0)
        {
            log.Fail("union produced no shell; writing each object as its own shell instead");
            return PerObjectShells(solids, log);
        }
        foreach (var g in solids.Select((s, i) => (s, i)).GroupBy(p => p.s.Block).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var objs = g.ToList();
            if (objs.Count > 1 && objs.Select(p => union.SolidShell[p.i]).Distinct().Count() == 1)
                log.Op("mergeStackedFloors", block: g.Key, count: objs.Count);
        }
        for (var i = 0; i < union.Shells.Count; i++)
            if (union.Shells[i].Blocks.Count > 1)
                log.Op("unionBlocks", count: union.Shells[i].Blocks.Count,
                    extra: new Dictionary<string, object> { ["shell"] = $"shell_{i:000}", ["blocks"] = union.Shells[i].Blocks });
        log.Op("rebuildShells", count: union.Shells.Count);
        var shells = union.Shells;
        for (var i = 0; i < shells.Count; i++)
        {
            var check = ShellCheck.Check(shells[i].Vertices, shells[i].Triangles);
            if (!check.Ok) log.Fail($"shell_{i:000}: not a valid closed shell ({check})");
        }
        return shells;
    }

    /// <summary>Fallback output: each object as its own shell (status is partial whenever this is used).</summary>
    private static List<OutputShell> PerObjectShells(List<Solid> solids, RepairLog log)
    {
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
