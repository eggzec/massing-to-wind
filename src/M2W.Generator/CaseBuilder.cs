using M2W.Geometry;
using M2W.Geometry.Models;

namespace M2W.Generator;

/// <summary>What to build: a clean control, one fault (with a variant index that spreads boundary values evenly), or
/// a combination of faults.</summary>
public sealed record Recipe(string Kind, IReadOnlyList<string> Faults, int Variant)
{
    public const string Clean = "clean";
    public const string SingleFault = "single";
    public const string Combined = "combined";

    public bool Has(string fault) => Faults.Contains(fault, StringComparer.Ordinal);
}

/// <summary>Everything about one generated case. The files are written from this; tests inspect the rest.</summary>
public sealed class BuiltCase
{
    public required string CaseId { get; init; }
    public required long Seed { get; init; }
    public required Recipe Recipe { get; init; }
    public required int Attempt { get; init; }
    public required CaseSpec Case { get; init; }
    public required ExpectedSpec Expected { get; init; }

    /// <summary>The input as written to input.obj (map coordinates if farCoordinates).</summary>
    public required Mesh Input { get; init; }

    public required Site Site { get; init; }
    public required FaceStyle Style { get; init; }
    public required List<Group> Groups { get; init; }

    /// <summary>The designed blocks (emitted before faults) and the corrected blocks (ground truth), grid frame.</summary>
    public required List<Block> CleanBlocks { get; init; }
    public required List<Block> ExpectedBlocks { get; init; }

    /// <summary>The ground truth expected.json is computed from: the height field of <see cref="ExpectedBlocks"/>
    /// after the roof-step rule (roof steps below minEdge raised to the higher roof, D-037).</summary>
    public required HeightField ExpectedField { get; init; }

    /// <summary>The blocks as emitted: the clean blocks plus geometric faults (tiny steps), grid frame.</summary>
    public required List<Block> InputBlocks { get; init; }

    /// <summary>The objects as emitted from the clean model, before any mesh fault (grid frame, local z).</summary>
    public required List<ObjMesh> CleanObjects { get; init; }

    public required HashSet<string> FloorBlocks { get; init; }
    public required List<string> Notes { get; init; }
    public required DesignRules Rules { get; init; }
}

/// <summary>
/// Builds one case from a seed and a recipe: groups of blocks → site and placement → validation of the clean and
/// corrected models → geometric faults → emission (whole blocks or stacked floors, polygons or triangles) → mesh
/// faults → vertical moves → map coordinates. expected.json is computed from the corrected model's height field.
/// Any draw that breaks a design rule is rejected and redrawn from a forked stream, so the result is a pure function
/// of (seed, recipe).
/// </summary>
public static class CaseBuilder
{
    public const string GeneratorVersion = "0.1.0";

    /// <summary>Volume change allowed for a fault whose repair could reasonably go either way (tiny steps).</summary>
    public const double AmbiguityBudget = 0.003;

    /// <summary>Block pairs closer than this in different shells are listed in mustStaySeparate.</summary>
    public const double NearPair = 10.0;

    /// <summary>roofStep single-fault steps as multiples of minEdge: three flushed, two kept (D-038).</summary>
    public static readonly IReadOnlyList<double> RoofStepRatios = new[] { 0.4, 0.8, 0.96, 1.04, 1.5 };

    /// <summary>Touching roof steps within this share of minEdge from minEdge itself are redrawn, so no answer
    /// depends on rounding at the threshold (D-038).</summary>
    public const double RoofStepBand = 0.025;

    public static BuiltCase Build(string caseId, long seed, Recipe recipe)
    {
        var root = new Rng((ulong)seed);
        var tol = ChooseTolerances(root.Fork("tolerances"), recipe);
        var rules = new DesignRules(
            MinFeature: Math.Max(2.0, Math.Max(2.5 * tol.MinGap, 4 * tol.MinEdge)),
            MinSeparation: Math.Max(4.0, 3 * tol.MinGap));
        string? last = null;
        for (var attempt = 0; attempt < 300; attempt++)
        {
            var built = TryBuild(caseId, seed, recipe, tol, rules, root.Fork($"attempt{attempt}"), attempt, out var why);
            if (built is not null) return built;
            last = why;
        }
        throw new InvalidOperationException($"{caseId}: could not build a valid case for [{string.Join(",", recipe.Faults)}] (last problem: {last}).");
    }

    private static Tolerances ChooseTolerances(Rng r, Recipe recipe)
    {
        double minGap = Tolerances.DefaultMinGap, minEdge = Tolerances.DefaultMinEdge, weld = Tolerances.DefaultWeld;
        double minAngle = Tolerances.DefaultMinAngleDeg, snap = Tolerances.DefaultGroundSnap;
        if (recipe.Kind != Recipe.Clean)
        {
            // Some cases move a threshold so a solver cannot hard-code the defaults.
            if (recipe.Has("smallGap") && r.Chance(0.3)) minGap = r.Pick(new[] { 0.5, 0.75, 1.5 });
            if (recipe.Has("shortEdge") && r.Chance(0.3)) minEdge = r.Pick(new[] { 0.15, 0.4 });
            if ((recipe.Has("floatingBlock") || recipe.Has("sunkBlock")) && r.Chance(0.3)) snap = r.Pick(new[] { 0.25, 1.0 });
            if (recipe.Has("duplicateVertices") && r.Chance(0.3)) weld = r.Pick(new[] { 0.0005, 0.005 });
            if (recipe.Has("sliver") && r.Chance(0.3)) minAngle = r.Pick(new[] { 3.0, 10.0 });
            if (recipe.Has("roofStep") && r.Chance(0.3)) minEdge = r.Pick(new[] { 0.15, 0.4 });
        }
        return new Tolerances
        {
            CellSize = Tolerances.DefaultCellSize,
            MinGap = minGap,
            MinEdge = minEdge,
            Weld = weld,
            MinAngleDeg = minAngle,
            GroundSnap = snap,
        };
    }

    private static BuiltCase? TryBuild(string caseId, long seed, Recipe recipe, Tolerances tol, DesignRules rules, Rng r, int attempt, out string? why)
    {
        why = null;
        var notes = new List<string>();

        // ---------------------------------------------------------------- groups
        var groups = new List<Group>();
        if (recipe.Has("overlap")) groups.Add(Layouts.Overlap(r.Fork("overlap"), rules));
        if (recipe.Has("touchingWalls")) groups.Add(Layouts.Touching(r.Fork("touching"), rules));
        if (recipe.Has("smallGap"))
        {
            var (below, ratio) = GapChoice(r.Fork("gapChoice"), recipe);
            var gap = Math.Round(ratio * tol.MinGap, 4);
            groups.Add(Layouts.Gap(r.Fork("gap"), rules, gap, below));
        }
        if (recipe.Has("roofStep"))
        {
            var (ratio, kind) = RoofStepChoice(r.Fork("roofStepChoice"), recipe);
            var step = Math.Round(ratio * tol.MinEdge, 3);
            groups.Add(Layouts.RoofStep(r.Fork("roofStep"), rules, step, kind));
        }
        var layoutGroups = groups.Count;
        var singles = layoutGroups > 0 ? r.IntIn(0, 2) : r.IntIn(1, 4);
        var needBlocks = recipe.Has("floatingBlock") && recipe.Has("sunkBlock") ? 2 : 1;
        while (groups.Sum(g => g.Clean.Count) + singles < needBlocks) singles++;
        for (var k = 0; k < singles; k++) groups.Add(Layouts.Single(r.Fork($"single{k}"), rules));
        r.Shuffle(groups);

        var n = 0;
        foreach (var g in groups)
        {
            foreach (var (c, e) in g.Clean.Zip(g.Expected))
            {
                n++;
                c.Name = $"B{n:00}";
                e.Name = c.Name;
            }
        }

        // ---------------------------------------------------------------- site and placement
        var bounds = groups.Select(g => g.Bounds).ToList();
        var area = bounds.Sum(b => (b.Width + rules.MinSeparation) * (b.Depth + rules.MinSeparation));
        var maxW = bounds.Max(b => b.Width);
        var maxD = bounds.Max(b => b.Depth);
        var aligned = r.Chance(0.35);
        Site? site = null;
        List<(double U, double V)>? offsets = null;
        for (var s = 0; s < 8 && offsets is null; s++)
        {
            var k = r.Range(1.6, 2.6) * (1 + 0.3 * s);
            var w = Math.Round(Math.Max(maxW + 16, Math.Sqrt(area * k * 1.5)), 1);
            var h = Math.Round(Math.Max(maxD + 16, area * k / w), 1);
            if (h > w) w = h;
            site = Site.Make(r, w, h, aligned);
            offsets = site.Place(r, groups, rules.MinSeparation);
        }
        if (offsets is null || site is null) { why = "groups do not fit on the site"; return null; }
        for (var gi = 0; gi < groups.Count; gi++)
        {
            var (du, dv) = offsets[gi];
            groups[gi].Transform(b => b.Translate(du, dv));
        }
        var clean = groups.SelectMany(g => g.Clean).ToList();
        var expected = groups.SelectMany(g => g.Expected).ToList();

        // ---------------------------------------------------------------- validation
        var design = HeightField.Build(expected);
        why = Validate(groups, clean, expected, design, rules, tol);
        if (why is not null) return null;
        var hf = FlushRoofs(design, tol.MinEdge, out why);
        if (hf is null) return null;
        var shells = hf.Shells();
        var blockComp = hf.BlockComponents();

        // ---------------------------------------------------------------- geometric faults (change the emitted solid)
        var input = clean.Select(b => b.Clone()).ToList();
        var shortEdgeKind = "";
        if (recipe.Has("shortEdge"))
        {
            shortEdgeKind = ShortEdgeKind(r.Fork("shortEdgeKind"), recipe);
            if (shortEdgeKind != "split")
            {
                var shellVolume = new Dictionary<string, double>();
                foreach (var b in expected) shellVolume[b.Name] = shells[blockComp[b.Name]].Volume;
                // Blocks of a closing gap are left alone: a step on the facing wall could widen the gap past minGap.
                var exclude = groups.Where(g => g.Relation == Relation.GapBelow).SelectMany(g => g.Clean.Select(b => b.Name)).ToHashSet();
                if (!Misalign(r.Fork("misalign"), input, shortEdgeKind, tol, shellVolume, exclude, notes, ref shortEdgeKind))
                {
                    why = "no misalignment fits the volume budget";
                    return null;
                }
                var hfIn = HeightField.Build(input);
                if (hfIn.FindNonManifoldVertex() is string p) { why = "misaligned input: " + p; return null; }
            }
        }

        // ---------------------------------------------------------------- emission
        var style = recipe.Kind == Recipe.Clean ? FaceStyle.Triangles : (r.Chance(0.55) ? FaceStyle.Polygons : FaceStyle.Triangles);
        // A roof deck lower than minEdge would be triangulated into slivers along its walls; slivers are their own fault.
        if (groups.Any(g => g.Relation == Relation.RoofStep && g.Variant.StartsWith("tier", StringComparison.Ordinal))) style = FaceStyle.Polygons;
        var floorBlocks = new HashSet<string>(StringComparer.Ordinal);
        if (recipe.Has("stackedFloors"))
        {
            var all = r.Chance(0.5);
            foreach (var b in input) if (all || r.Chance(0.5)) floorBlocks.Add(b.Name);
            if (floorBlocks.Count == 0) floorBlocks.Add(r.Pick(input).Name);
            notes.Add($"stackedFloors: {string.Join(", ", floorBlocks.OrderBy(x => x, StringComparer.Ordinal))} emitted floor by floor");
        }
        var objects = new List<ObjMesh>();
        foreach (var b in input)
        {
            if (floorBlocks.Contains(b.Name)) objects.AddRange(Mesher.MeshFloors(b, style));
            else objects.Add(Mesher.MeshBlock(b, style));
        }
        var cleanObjects = objects.Select(o => o.Clone()).ToList();

        // ---------------------------------------------------------------- mesh faults
        var touched = objects.ToDictionary(o => o, _ => new HashSet<int>());
        if (recipe.Has("sliver"))
        {
            var fr = r.Fork("sliver");
            var degenerate = fr.Chance(0.35);
            var total = 0;
            foreach (var o in PickObjects(fr, objects, fr.IntIn(1, 2)))
            {
                var made = MeshFaults.InsertSlivers(o, fr, fr.IntIn(1, 4), tol.MinAngleDeg, degenerate, style, touched[o]);
                if (made > 0) notes.Add($"sliver: {made} {(degenerate ? "zero-area" : "thin")} triangle(s) in {o.Name}");
                total += made;
            }
            if (total == 0) { why = "no sliver inserted"; return null; }
        }
        if (recipe.Has("shortEdge") && shortEdgeKind == "split")
        {
            var fr = r.Fork("shortEdge");
            var total = 0;
            foreach (var o in PickObjects(fr, objects, fr.IntIn(1, 2)))
            {
                var lengths = new List<double>();
                var made = MeshFaults.InsertShortEdges(o, fr, fr.IntIn(1, 3), () =>
                {
                    var len = Math.Round(fr.Range(0.1, 0.8) * tol.MinEdge, 3);
                    len = Math.Max(len, Math.Max(0.01, 10 * tol.Weld));
                    lengths.Add(len);
                    return len;
                }, style, touched[o]);
                if (made > 0) notes.Add($"shortEdge: split vertex in {o.Name}, edge(s) {string.Join(", ", lengths.Take(made).Select(x => x.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)))} m");
                total += made;
            }
            if (total == 0) { why = "no short edge inserted"; return null; }
        }
        if (recipe.Has("openSurface"))
        {
            var fr = r.Fork("openSurface");
            var bottoms = fr.Chance(0.4);
            var total = 0;
            foreach (var o in PickObjects(fr, objects, fr.IntIn(1, 2)))
            {
                var made = bottoms ? MeshFaults.DeleteBottom(o, touched[o]) : MeshFaults.DeleteFaces(o, fr, fr.IntIn(1, 3), touched[o]);
                if (made > 0) notes.Add($"openSurface: {made} face(s) deleted from {o.Name}{(bottoms ? " (ground face)" : "")}");
                total += made;
            }
            if (total == 0) { why = "no face deleted"; return null; }
        }
        if (recipe.Has("flippedFaces"))
        {
            var fr = r.Fork("flipped");
            foreach (var o in PickObjects(fr, objects, fr.IntIn(1, 2)))
            {
                var insideOut = fr.Chance(0.3);
                var made = MeshFaults.FlipFaces(o, fr, insideOut ? 1.0 : fr.Range(0.1, 0.4));
                notes.Add($"flippedFaces: {made} face(s) in {o.Name}{(insideOut ? " (inside out)" : "")}");
            }
        }
        if (recipe.Has("duplicateVertices"))
        {
            var fr = r.Fork("duplicates");
            var soup = fr.Chance(0.4);
            var jitter = fr.Chance(0.6) ? Math.Round(0.2 * tol.Weld, 6) : 0;
            var targets = soup ? objects : PickObjects(fr, objects, fr.IntIn(1, Math.Max(1, objects.Count))).ToList();
            foreach (var o in targets)
            {
                var made = MeshFaults.SplitVertices(o, fr, soup ? 1.0 : fr.Range(0.3, 0.8), jitter, 0.5);
                notes.Add($"duplicateVertices: {made} copies in {o.Name}{(jitter > 0 ? $" (up to {jitter} m apart per axis)" : " (exact)")}");
            }
        }

        // ---------------------------------------------------------------- vertical moves (whole blocks)
        var moved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (fault, sign) in new[] { ("floatingBlock", 1.0), ("sunkBlock", -1.0) })
        {
            if (!recipe.Has(fault)) continue;
            var fr = r.Fork(fault);
            var candidates = input.Select(b => b.Name).Where(x => !moved.Contains(x)).ToList();
            var name = fr.Pick(candidates);
            moved.Add(name);
            var ratio = recipe.Kind == Recipe.SingleFault && recipe.Variant % 4 == 0 ? 0.9 : fr.Step(0.1, 0.9, 0.01);
            var dz = Math.Round(sign * ratio * tol.GroundSnap, 3);
            foreach (var o in objects.Where(o => o.Block == name)) MeshFaults.MoveZ(o, dz);
            notes.Add($"{fault}: {name} moved {dz:+0.000;-0.000} m ({ratio:0.00} × groundSnap); repair moves it back");
        }

        foreach (var o in objects) MeshFaults.Compact(o);

        // ---------------------------------------------------------------- map coordinates
        var origin = Vec3.Zero;
        if (recipe.Has("farCoordinates"))
        {
            var fr = r.Fork("far");
            var ox = fr.Step(150000, 850000, 0.001);
            var oy = fr.Chance(0.5) ? fr.Step(100000, 990000, 0.001) : fr.Step(4000000, 6500000, 0.001);
            var oz = fr.Chance(0.3) ? fr.Step(5, 120, 0.01) : 0;
            origin = new Vec3(ox, oy, oz);
            notes.Add($"farCoordinates: origin ({ox:0.000}, {oy:0.000}, {oz:0.00})");
        }
        var mesh = new Mesh();
        foreach (var o in objects)
        {
            var baseIndex = mesh.Vertices.Count;
            foreach (var v in o.V)
            {
                var p = site.ToPlan(v.X, v.Y);
                mesh.Vertices.Add(new Vec3(p.X + origin.X, p.Y + origin.Y, v.Z + origin.Z));
            }
            mesh.Objects.Add(new MeshObject(o.Name, o.F.Select(f => new Face(f.Select(i => i + baseIndex)))));
        }

        // ---------------------------------------------------------------- relations and expected answer
        foreach (var g in groups.Where(g => g.Relation != Relation.Single))
        {
            var names = string.Join("+", g.Clean.Select(b => b.Name));
            notes.Add(g.Relation switch
            {
                Relation.Overlap => $"overlap: {names} ({g.Variant})",
                Relation.Touching => $"touchingWalls: {names} ({g.Variant})",
                Relation.GapBelow => $"smallGap: {names} gap {g.Gap:0.000} m = {g.Gap / tol.MinGap:0.00} × minGap, closed and merged",
                Relation.RoofStep => $"roofStep: {names} ({g.Variant}) roofs {g.Roofs.Low:0.000} and {g.Roofs.High:0.000} m, step {g.Step:0.000} m = {g.Step / tol.MinEdge:0.00} × minEdge, " +
                    (g.Step < tol.MinEdge ? "lower roof raised to the higher one" : "kept"),
                _ => $"smallGap: {names} gap {g.Gap:0.000} m = {g.Gap / tol.MinGap:0.00} × minGap, left open",
            });
        }

        foreach (var (before, after) in design.Shells().Zip(shells))
        {
            var dv = after.Volume - before.Volume;
            if (dv > 1e-6)
                notes.Add($"roofFlush: {string.Join("+", after.Blocks)} roof steps below minEdge raised (AGREEMENT rule), volume {before.Volume:0.00} -> {after.Volume:0.00} m³ (+{100 * dv / before.Volume:0.000} %)");
        }

        var expectedSpec = ExpectedFrom(caseId, seed, recipe, tol, hf, shells, expected, site);
        var caseSpec = new CaseSpec
        {
            SchemaVersion = SchemaVersions.Current,
            CaseId = caseId,
            Units = "m",
            Origin = origin,
            Tolerances = tol,
            Files = new CaseFileNames { Input = CaseFiles.Input },
        };

        return new BuiltCase
        {
            CaseId = caseId,
            Seed = seed,
            Recipe = recipe,
            Attempt = attempt,
            Case = caseSpec,
            Expected = expectedSpec,
            Input = mesh,
            Site = site,
            Style = style,
            Groups = groups,
            CleanBlocks = clean,
            ExpectedBlocks = expected,
            ExpectedField = hf,
            InputBlocks = input,
            CleanObjects = cleanObjects,
            FloorBlocks = floorBlocks,
            Notes = notes,
            Rules = rules,
        };
    }

    private static IEnumerable<ObjMesh> PickObjects(Rng r, List<ObjMesh> objects, int count)
    {
        var order = objects.ToList();
        r.Shuffle(order);
        return order.Take(Math.Min(count, order.Count)).OrderBy(o => objects.IndexOf(o)).ToList();
    }

    /// <summary>smallGap side and ratio. Single-fault cases: variants 0–9 below minGap, 10–19 above; the first four of
    /// each half sit right at the threshold (0.9× and 1.1×).</summary>
    private static (bool Below, double Ratio) GapChoice(Rng r, Recipe recipe)
    {
        bool below, boundary;
        if (recipe.Kind == Recipe.SingleFault)
        {
            below = recipe.Variant % 20 < 10;
            boundary = recipe.Variant % 10 < 4;
        }
        else
        {
            below = r.Chance(0.7);
            boundary = r.Chance(0.4);
        }
        if (below) return (true, boundary ? 0.9 : r.Step(0.2, 0.85, 0.05));
        return (false, boundary ? 1.1 : r.Step(1.2, 2.0, 0.05));
    }

    /// <summary>roofStep step and kind. Single-fault cases: variants 0–3 at 0.4 × minEdge, 4–7 at 0.8 ×, 8–11 at
    /// 0.96 ×, 12–15 at 1.04 ×, 16–19 at 1.5 ×; kinds rotate through touching, overlap and tier.</summary>
    private static (double Ratio, string Kind) RoofStepChoice(Rng r, Recipe recipe)
    {
        if (recipe.Kind == Recipe.SingleFault)
            return (RoofStepRatios[recipe.Variant / 4 % RoofStepRatios.Count], Layouts.RoofStepKinds[recipe.Variant % Layouts.RoofStepKinds.Count]);
        return (r.Pick(RoofStepRatios), r.Pick(Layouts.RoofStepKinds));
    }

    /// <summary>
    /// Applies the roof-step rule of AGREEMENT.md (D-037) to the corrected model. The agreement does not fix the order
    /// in which regions are raised, so the flush is computed three ways and a layout whose answer depends on the order
    /// is redrawn; so is one with a touching step within <see cref="RoofStepBand"/> of minEdge (D-038).
    /// </summary>
    private static HeightField? FlushRoofs(HeightField design, double minEdge, out string? why)
    {
        why = null;
        var flushed = design.FlushRoofSteps(minEdge, HeightField.FlushOrder.Simultaneous);
        foreach (var order in new[] { HeightField.FlushOrder.LowestFirst, HeightField.FlushOrder.HighestFirst })
            if (!flushed.SameRoofs(design.FlushRoofSteps(minEdge, order)))
            {
                why = "roof flush depends on the order regions are raised";
                return null;
            }
        foreach (var field in new[] { design, flushed })
            foreach (var step in field.TouchingSteps())
                if (Math.Abs(step - minEdge) < RoofStepBand * minEdge)
                {
                    why = $"touching roofs differ by {step} m, too close to minEdge {minEdge}";
                    return null;
                }
        if (flushed.FindNonManifoldVertex() is string p) { why = "flushed: " + p; return null; }
        if (flushed.FindRoofStep(minEdge) is string q) throw new InvalidOperationException("roof flush left a step: " + q);
        return flushed;
    }

    private static string ShortEdgeKind(Rng r, Recipe recipe)
    {
        var kinds = new[] { "split", "ledge", "jog" };
        return recipe.Kind == Recipe.SingleFault ? kinds[recipe.Variant % 3] : r.Pick(kinds);
    }

    /// <summary>
    /// Geometric short edges: a tiny step left where two parts should be flush. "ledge": the floors above some level
    /// are set back by ε on one side; "jog": one footprint rectangle is moved ε off its neighbour's line.
    /// ε is below minEdge. The expected answer stays the flush (clean) design; the step must change each shell's
    /// volume by less than <see cref="AmbiguityBudget"/> so either way of removing it passes.
    /// </summary>
    private static bool Misalign(Rng r, List<Block> blocks, string kind, Tolerances tol, Dictionary<string, double> shellVolume,
        HashSet<string> exclude, List<string> notes, ref string usedKind)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var bi = r.Int(0, blocks.Count);
            var b = blocks[bi];
            if (exclude.Contains(b.Name)) continue;
            var eps = Math.Round(r.Range(0.1, 0.8) * tol.MinEdge, 3);
            eps = Math.Max(eps, Math.Max(0.01, 10 * tol.Weld));
            Block? moved = null;
            string what;
            var tryJog = kind == "jog" && attempt < 40;
            if (tryJog)
            {
                // A rectangle side that lines up with another rectangle's same side.
                var cands = new List<(int R, int Side, double C)>();
                for (var i = 0; i < b.Rects.Count; i++)
                    for (var j = 0; j < b.Rects.Count; j++)
                    {
                        if (i == j) continue;
                        var ri = b.Rects[i];
                        var rj = b.Rects[j];
                        if (ri.U0 == rj.U0) cands.Add((i, 0, ri.U0));
                        if (ri.V0 == rj.V0) cands.Add((i, 1, ri.V0));
                        if (ri.U1 == rj.U1) cands.Add((i, 2, ri.U1));
                        if (ri.V1 == rj.V1) cands.Add((i, 3, ri.V1));
                    }
                if (cands.Count == 0) continue;
                var (ri2, side, c) = r.Pick(cands);
                var dir = r.Chance(0.5) ? 1 : -1;
                var rects = b.Rects.ToList();
                var rc = rects[ri2];
                var nc = Math.Round(c + dir * eps, 6);
                rects[ri2] = side switch
                {
                    0 => rc with { U0 = nc },
                    1 => rc with { V0 = nc },
                    2 => rc with { U1 = nc },
                    _ => rc with { V1 = nc },
                };
                moved = b.WithRects(rects);
                what = $"jog {eps:0.000} m in {b.Name}'s footprint";
            }
            else
            {
                if (b.FloorCount < 2) continue;
                var f = r.IntIn(1, b.FloorCount - 1);
                var tiers = new List<Tier>();
                var splitIndex = -1;
                foreach (var t in b.Tiers)
                {
                    var start = tiers.Count == 0 ? 0 : tiers[^1].TopFloor;
                    if (f > start && f < t.TopFloor)
                    {
                        tiers.Add(new Tier(f, t.Clip));
                        splitIndex = tiers.Count;
                        tiers.Add(t with { Clip = Rect.Everything });
                    }
                    else
                    {
                        if (start == f) splitIndex = tiers.Count;
                        tiers.Add(t);
                    }
                }
                if (splitIndex < 0) continue;
                // Footprint extent of the tier below the step.
                var fb = b.Bounds;
                for (var k = 0; k < splitIndex; k++) fb = fb.Intersect(tiers[k].Clip);
                var side = r.Int(0, 4);
                var clip = tiers[splitIndex].Clip;
                clip = side switch
                {
                    // Never widen a setback that is already deeper than the step.
                    0 => clip with { U0 = Math.Max(clip.U0, Math.Round(fb.U0 + eps, 6)) },
                    1 => clip with { V0 = Math.Max(clip.V0, Math.Round(fb.V0 + eps, 6)) },
                    2 => clip with { U1 = Math.Min(clip.U1, Math.Round(fb.U1 - eps, 6)) },
                    _ => clip with { V1 = Math.Min(clip.V1, Math.Round(fb.V1 - eps, 6)) },
                };
                tiers[splitIndex] = tiers[splitIndex] with { Clip = clip };
                moved = b.WithTiers(tiers);
                what = $"ledge {eps:0.000} m on {b.Name} from floor {f + 1} ({new[] { "west", "south", "east", "north" }[side]} side)";
                usedKind = "ledge";
            }
            var dv = Math.Abs(moved.Volume() - b.Volume());
            if (dv < 1e-6 || dv > AmbiguityBudget * shellVolume[b.Name]) continue;
            if (tryJog) usedKind = "jog";
            blocks[bi] = moved;
            notes.Add($"shortEdge: {what}; volume change {dv:0.00} m³");
            return true;
        }
        return false;
    }

    private static string? Validate(List<Group> groups, List<Block> clean, List<Block> expected, HeightField hf, DesignRules rules, Tolerances tol)
    {
        if (hf.FindNonManifoldVertex() is string a) return "expected: " + a;
        if (hf.FindNarrowFeature(rules.MinFeature) is string b) return "expected: " + b;
        var comp = hf.BlockComponents();
        if (comp.Values.Any(c => c < 0)) return "a block is split over two shells";

        var groupOfComp = new Dictionary<int, int>();
        for (var gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            var comps = g.Expected.Select(x => comp[x.Name]).Distinct().ToList();
            var merged = g.Relation is Relation.Overlap or Relation.Touching or Relation.GapBelow or Relation.RoofStep;
            if (merged && comps.Count != 1) return $"{g.Relation} group did not merge";
            if (g.Relation == Relation.GapAbove && comps.Count != 2) return "gap-above pair merged";
            foreach (var c in comps)
            {
                if (groupOfComp.TryGetValue(c, out var other) && other != gi) return "two groups merged";
                groupOfComp[c] = gi;
            }
        }

        for (var x = 0; x < hf.ComponentCount; x++)
            for (var y = x + 1; y < hf.ComponentCount; y++)
            {
                var d = hf.Distance(x, y);
                var g = groups[groupOfComp[x]];
                if (groupOfComp[x] == groupOfComp[y] && g.Relation == Relation.GapAbove)
                {
                    if (Math.Abs(d - g.Gap) > 1e-6) return $"gap-above pair is {d} apart, not {g.Gap}";
                    if (d < 1.05 * tol.MinGap) return "gap-above pair too close to minGap";
                }
                else if (d < rules.MinSeparation - 1e-6) return $"shells {x} and {y} only {d:0.###} m apart";
            }

        foreach (var blk in clean.Concat(expected))
        {
            var own = HeightField.Build(new[] { blk });
            if (own.FindNonManifoldVertex() is string p) return $"{blk.Name}: {p}";
            if (own.FindNarrowFeature(rules.MinFeature) is string q) return $"{blk.Name}: {q}";
            if (own.ComponentCount != 1) return $"{blk.Name} is not one piece";
        }
        var hfClean = HeightField.Build(clean);
        if (hfClean.FindNonManifoldVertex() is string c2) return "clean: " + c2;
        return null;
    }

    private static ExpectedSpec ExpectedFrom(string caseId, long seed, Recipe recipe, Tolerances tol, HeightField hf, List<ShellStats> shells, List<Block> blocks, Site site)
    {
        var sorted = shells
            .OrderByDescending(s => Math.Round(s.Volume, 6))
            .ThenByDescending(s => Math.Round(s.FootprintArea, 6))
            .ThenBy(s => s.Blocks[0], StringComparer.Ordinal)
            .ToList();
        var mustMerge = shells.Where(s => s.Blocks.Count > 1)
            .Select(s => (IReadOnlyList<string>)s.Blocks.ToList())
            .OrderBy(g => g[0], StringComparer.Ordinal)
            .ToList();
        var comp = hf.BlockComponents();
        var names = blocks.Select(b => b.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var separate = new List<IReadOnlyList<string>>();
        for (var i = 0; i < names.Count; i++)
            for (var j = i + 1; j < names.Count; j++)
            {
                int a = comp[names[i]], b = comp[names[j]];
                if (a != b && hf.Distance(a, b) <= NearPair) separate.Add(new[] { names[i], names[j] });
            }
        var faults = ExpectedSpec.KnownFaults.Where(recipe.Has).ToList();
        return new ExpectedSpec
        {
            SchemaVersion = SchemaVersions.Current,
            CaseId = caseId,
            Seed = seed,
            FaultsInjected = faults,
            Expected = new ExpectedGeometry
            {
                ShellCount = sorted.Count,
                Shells = sorted.Select(s =>
                {
                    // Anchor (D-017): local plan coordinates, i.e. input minus origin, like repaired.obj.
                    var (u, v, _) = hf.InteriorPoint(s.Component);
                    var p = site.ToPlan(u, v);
                    return new ExpectedShell
                    {
                        Volume_m3 = Math.Round(s.Volume, 6),
                        FootprintArea_m2 = Math.Round(s.FootprintArea, 6),
                        MaxHeight_m = Math.Round(s.MaxHeight, 6),
                        Genus = s.Genus,
                        Blocks = s.Blocks.ToList(),
                        Anchor_m = new[] { Math.Round(p.X, 3) + 0.0, Math.Round(p.Y, 3) + 0.0 },
                    };
                }).ToList(),
                MustMerge = mustMerge,
                MustStaySeparate = separate,
            },
            Checks = new ExpectedChecks
            {
                Watertight = true,
                OutwardNormals = true,
                NoSelfIntersections = true,
                MinGap_m = tol.MinGap,
                MinEdge_m = tol.MinEdge,
            },
            Tolerances = new CheckTolerances { VolumeRel = 0.01, AreaRel = 0.01, Height_m = 0.01 },
        };
    }
}
