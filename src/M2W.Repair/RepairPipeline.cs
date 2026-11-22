using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Repair.Model;
using M2W.Repair.Planar;
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

        // 1b. An object holding several separate closed solids becomes one part per solid (D-041).
        var parts = new List<Solid>();
        foreach (var s in solids)
        {
            var pieces = PartSplitter.Split(s, tol.Weld);
            if (pieces.Count > 1)
                log.Note($"object {s.Name} holds {pieces.Count} separate closed solids; each is a part of its own " +
                         $"({pieces[0].Name} to {pieces[^1].Name})");
            parts.AddRange(pieces);
        }
        solids = parts;

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
        var leftOpen = new List<Dictionary<string, object>>();
        foreach (var s in solids)
        {
            var before = new HashSet<Tri>(s.Triangles.Select(t => t.Canonical()));
            Orienter.MakeConsistent(s);
            var hr = HoleFiller.Close(s, tol.Weld);
            tSplits += hr.TJunctionsSplit; holes += hr.HolesFilled; open += hr.HolesLeftOpen;
            if (hr.TJunctionsSplit > 0 || hr.HolesFilled > 0) Orienter.MakeConsistent(s);
            Orienter.MakeOutward(s);
            reversed += s.Triangles.Count(t => !before.Contains(t.Canonical()) && before.Contains(t.Reversed().Canonical()));
            if (hr.HolesLeftOpen > 0)
            {
                // Where: the free edges that are left (each is on the rim of an open hole).
                var free = Orienter.EdgeTriangles(s.Triangles).Where(kv => kv.Value.Count == 1).Select(kv => kv.Key).ToList();
                var box = PlanBox.Of(free.SelectMany(e => new[] { s.Vertices[e.Item1], s.Vertices[e.Item2] }).Select(Plan));
                log.Fail($"object {s.Name}: {hr.HolesLeftOpen} holes could not be closed ({free.Count} open edges, at {box})");
                leftOpen.Add(new Dictionary<string, object>
                {
                    ["object"] = s.Name, ["count"] = hr.HolesLeftOpen, ["openEdges"] = free.Count, ["bbox_m"] = box.ToArray(),
                });
            }
        }
        if (reversed > 0) log.Op("fixOrientation", count: reversed);
        if (tSplits > 0) log.Op("splitTJunctions", count: tSplits);
        if (holes > 0 || leftOpen.Count > 0)
            log.Op("closeHoles", count: holes, extra: leftOpen.Count == 0 ? null : new Dictionary<string, object>
            {
                ["leftOpen"] = leftOpen.Sum(x => (int)x["count"]), ["leftOpenRegions"] = leftOpen,
            });

        // 4. Ground snap: blocks within groundSnap of Z = 0 are moved onto it.
        foreach (var mv in GroundSnapper.Snap(solids, tol.GroundSnap))
            log.Op("groundSnap", block: mv.Block, extra: new Dictionary<string, object> { ["dz_m"] = Math.Round(mv.Dz, 9) });
        foreach (var g in solids.GroupBy(s => s.Group))
        {
            var minZ = g.SelectMany(s => s.Vertices).Min(v => v.Z);
            if (minZ < -1e-9)
                log.Fail($"block {g.Key} reaches {minZ:0.###} m below ground, beyond groundSnap; not moved (at " +
                         $"{PlanBox.Of(g.SelectMany(s => s.Vertices).Select(Plan))})");
        }

        // 5. Union as a 2.5D height field: merges stacked floors, overlaps and touching walls; one shell per footprint.
        // 6. Gaps narrower than minGap between separate parts are bridged inside the union (closing per height level).
        var union = new HeightFieldUnion(solids, tol.Weld, tol.MinEdge, tol.MinGap).Run();
        foreach (var p in union.Problems) log.Fail(p);
        // Real gaps (width above the output precision) are closeGap; parts that touch (meet at a point, so the slice
        // has them apart with no width between) are filled at the contact too, and reported with their union.
        var gaps = union.ClosedGaps.Where(g => g.width >= GapCloser.Slack).ToList();
        var touching = union.ClosedGaps.Where(g => g.width < GapCloser.Slack).ToList();
        foreach (var g in gaps.GroupBy(g => (g.a, g.b)).OrderBy(g => g.Key.a, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.b, StringComparer.Ordinal))
            log.Op("closeGap", pair: new[] { g.Key.a, g.Key.b }, width: Math.Round(g.Min(x => x.width), 6),
                extra: new Dictionary<string, object>
                {
                    ["fillHeight_m"] = g.Max(x => x.z), ["minGap_m"] = tol.MinGap,
                    ["bbox_m"] = g.Aggregate(PlanBox.Empty, (b, x) => b.Union(x.box)).ToArray(),
                });
        foreach (var (block, regions) in union.Unsupported)
            log.Op("fillUnsupported", block: block, count: regions.Count, extra: new Dictionary<string, object>
            {
                ["area_m2"] = Math.Round(regions.Sum(r => r.area), 3),
                ["bbox_m"] = regions.Aggregate(PlanBox.Empty, (b, r) => b.Union(r.box)).ToArray(),
                ["regions"] = regions.Select(r => new Dictionary<string, object>
                {
                    ["bbox_m"] = r.box.ToArray(), ["area_m2"] = Math.Round(r.area, 3),
                }).ToList(),
            });
        foreach (var g in union.ThinRaised.GroupBy(t => t).OrderBy(g => g.Key.from).ThenBy(g => g.Key.to))
            log.Op("raiseThinRegion", count: g.Count(), extra: new Dictionary<string, object>
            {
                ["from_m"] = g.Key.from, ["to_m"] = g.Key.to, ["minEdge_m"] = tol.MinEdge,
            });
        foreach (var (kind, n) in union.OutlineFills)
            log.Op(kind, count: n, extra: new Dictionary<string, object> { ["minEdge_m"] = tol.MinEdge });
        if (union.ShortEdgeSnaps > 0)
            log.Op("collapseShortEdges", count: union.ShortEdgeSnaps,
                extra: new Dictionary<string, object> { ["minEdge_m"] = tol.MinEdge });
        foreach (var (from, to, faces) in union.FlushedSteps)
            log.Op("flushRoofStep", count: faces, extra: new Dictionary<string, object>
            {
                ["from_m"] = from, ["to_m"] = to, ["minEdge_m"] = tol.MinEdge,
            });
        if (union.Shells.Count == 0)
        {
            var all = PlanBox.Of(solids.SelectMany(s => s.Vertices).Select(Plan));
            log.Fail($"union produced no shell; writing each of the {solids.Count} objects as its own shell instead (within {all})");
            log.Op("rebuildShells", count: solids.Count, extra: new Dictionary<string, object>
            {
                ["fallback"] = "perObject", ["bbox_m"] = all.ToArray(),
            });
            return PerObjectShells(solids, log);
        }
        foreach (var g in solids.Select((s, i) => (s, i)).GroupBy(p => p.s.Block).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var objs = g.ToList();
            if (objs.Count > 1 && objs.Select(p => union.SolidShell[p.i]).Distinct().Count() == 1)
                log.Op("mergeStackedFloors", block: g.Key, count: objs.Count);
        }
        // Touching contacts, by the shell of the first block on one side.
        var shellOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < union.Shells.Count; i++)
            foreach (var b in union.Shells[i].Blocks) shellOf.TryAdd(b, i);
        var contacts = new int[union.Shells.Count];
        foreach (var t in touching.Select(t => (t.a, t.b)).Distinct())
            if (shellOf.TryGetValue(t.a.Split('+')[0], out var si)) contacts[si]++;
        for (var i = 0; i < union.Shells.Count; i++)
            if (union.Shells[i].Blocks.Count > 1)
            {
                var extra = new Dictionary<string, object> { ["shell"] = $"shell_{i:000}", ["blocks"] = union.Shells[i].Blocks };
                if (contacts[i] > 0) extra["pointContacts"] = contacts[i];
                log.Op("unionBlocks", count: union.Shells[i].Blocks.Count, extra: extra);
            }
        log.Op("rebuildShells", count: union.Shells.Count);
        var shells = union.Shells;
        for (var i = 0; i < shells.Count; i++)
        {
            var check = ShellCheck.Check(shells[i].Vertices, shells[i].Triangles);
            if (!check.Ok)
                log.Fail($"shell_{i:000}: not a valid closed shell ({check}), at {PlanBox.Of(shells[i].Vertices.Select(Plan))}");
        }
        // Own check of the output against minEdge (a failure here is reported, never hidden) and a count of
        // triangles thinner than minAngleDeg, which v0.1 does not grade (D-025).
        var (shortest, where) = (double.PositiveInfinity, "");
        var thin = 0;
        for (var i = 0; i < shells.Count; i++)
        {
            var v = shells[i].Vertices.Select(OutputShell.Round6).ToList();
            foreach (var t in shells[i].Triangles)
            {
                var (a, b, c) = (v[t.A], v[t.B], v[t.C]);
                foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
                {
                    var len = (q - p).Length;
                    if (len < shortest) (shortest, where) = (len, $"shell_{i:000} at {PlanBox.Of(new[] { Plan(p), Plan(q) })}");
                }
                if (MinAngleDeg(a, b, c) < tol.MinAngleDeg) thin++;
            }
        }
        if (shortest < tol.MinEdge - GapCloser.Slack)
            log.Fail($"{where}: an edge of {shortest:0.######} m is shorter than minEdge {tol.MinEdge} m");
        if (thin > 0)
            log.Note($"{thin} output triangles have an angle below minAngleDeg {tol.MinAngleDeg} deg (long low or tall " +
                     "narrow walls, thin roof pieces); not graded in v0.1 (D-025)");
        return shells;
    }

    private static P2 Plan(Vec3 v) => new(v.X, v.Y);

    /// <summary>Smallest angle of a triangle, in degrees.</summary>
    public static double MinAngleDeg(Vec3 a, Vec3 b, Vec3 c)
    {
        static double At(Vec3 p, Vec3 q, Vec3 r)
        {
            var u = q - p; var w = r - p;
            var n = u.Length * w.Length;
            return n == 0 ? 0 : Math.Acos(Math.Clamp(Vec3.Dot(u, w) / n, -1, 1)) * 180 / Math.PI;
        }
        return Math.Min(At(a, b, c), Math.Min(At(b, c, a), At(c, a, b)));
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
