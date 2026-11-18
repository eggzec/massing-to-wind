using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using M2W.Geometry;
using M2W.Geometry.Models;

namespace M2W.Checker;

/// <summary>The check names written to <c>result.json</c>, in the order they are written.</summary>
public static class CheckNames
{
    public const string Parse = "parse";
    public const string Watertight = "watertight";
    public const string Manifold = "manifold";
    public const string OutwardNormals = "outwardNormals";
    public const string Euler = "euler";
    public const string NoSelfIntersections = "noSelfIntersections";
    public const string MinGap = "minGap_m";
    public const string MinEdge = "minEdge_m";
    public const string ShellCount = "shellCount";
    public const string Volume = "volume_m3";
    public const string Footprint = "footprintArea_m2";
    public const string MaxHeight = "maxHeight_m";
    public const string GroundContact = "groundContact";
    public const string MustMerge = "mustMerge";
    public const string MustStaySeparate = "mustStaySeparate";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Parse, Watertight, Manifold, OutwardNormals, Euler, NoSelfIntersections, MinGap, MinEdge, ShellCount,
        Volume, Footprint, MaxHeight, GroundContact, MustMerge, MustStaySeparate,
    };
}

/// <summary>
/// Grades a repaired mesh against <c>expected.json</c> and the validity rules of AGREEMENT.md. Pure and
/// deterministic: the same inputs give the same <see cref="CheckResult"/>. Never reads <c>report.json</c>.
/// </summary>
public static class Grader
{
    /// <summary>
    /// Allowance for the 6-decimal OBJ format on <c>minGap_m</c> and <c>minEdge_m</c>: a length written to 6 dp
    /// can read back up to 1e-6 m short (two rounded endpoints). Without it an exactly-on-limit edge could fail.
    /// </summary>
    public const double ObjPrecision = 1e-6;

    private static readonly Regex ShellName = new(@"^shell_\d{3}$", RegexOptions.CultureInvariant);
    private const int MaxDetailItems = 6;

    /// <param name="caseId">Case id for the result.</param>
    /// <param name="repaired">The repaired mesh, or null if it could not be read.</param>
    /// <param name="readError">Why the repaired mesh could not be read (when <paramref name="repaired"/> is null).</param>
    /// <param name="loadInput">Loads <c>input.obj</c> (as in the case folder); called only if block groups need it.
    /// Returns the mesh or an error message.</param>
    public static CheckResult Grade(string caseId, Mesh? repaired, string? readError, Func<(Mesh? Mesh, string? Error)> loadInput,
        CaseSpec spec, ExpectedSpec expected)
    {
        var checks = repaired is null
            ? Unreadable(readError ?? "repaired.obj could not be read")
            : GradeMesh(repaired, loadInput, spec, expected);
        return new CheckResult
        {
            SchemaVersion = SchemaVersions.Current,
            CaseId = caseId,
            Pass = checks.Count > 0 && checks.All(c => c.Pass),
            Checks = checks,
        };
    }

    private static List<CheckEntry> Unreadable(string error)
    {
        var list = new List<CheckEntry> { Entry(CheckNames.Parse, false, 1, 0, error) };
        foreach (var name in CheckNames.All.Skip(1))
            list.Add(Entry(name, false, 0, 0, "not checked: repaired.obj did not parse"));
        return list;
    }

    // ------------------------------------------------------------------ the checks

    private static List<CheckEntry> GradeMesh(Mesh mesh, Func<(Mesh? Mesh, string? Error)> loadInput, CaseSpec spec, ExpectedSpec expected)
    {
        var list = new List<CheckEntry>();
        var shells = BuildShells(mesh, out var parseProblems);
        list.Add(Count(CheckNames.Parse, parseProblems, "triangles only, every object named shell_NNN"));

        var topo = shells.Select(Topology.Of).ToArray();
        var required = expected.Checks;

        // watertight: free edges
        {
            var bad = Enumerable.Range(0, shells.Count).Where(i => topo[i].FreeEdges > 0)
                .Select(i => $"{shells[i].Name}: {topo[i].FreeEdges} free edges");
            list.Add(Count(CheckNames.Watertight, topo.Sum(t => t.FreeEdges), bad, required.Watertight));
        }

        // manifold: edges used by more than two faces, vertices whose faces form more than one fan
        {
            var bad = Enumerable.Range(0, shells.Count).Where(i => topo[i].NonManifoldEdges + topo[i].NonManifoldVertices > 0)
                .Select(i => $"{shells[i].Name}: {topo[i].NonManifoldEdges} non-manifold edges, {topo[i].NonManifoldVertices} non-manifold vertices");
            list.Add(Count(CheckNames.Manifold, topo.Sum(t => t.NonManifoldEdges + t.NonManifoldVertices), bad));
        }

        // outwardNormals: consistent winding and positive volume
        var volumes = shells.Select(s => s.SignedVolume).ToArray();
        {
            var items = new List<string>();
            var n = 0;
            for (var i = 0; i < shells.Count; i++)
            {
                n += topo[i].InconsistentEdges;
                if (topo[i].InconsistentEdges > 0) items.Add($"{shells[i].Name}: {topo[i].InconsistentEdges} edges with inconsistent winding");
                if (!(volumes[i] > 0))
                {
                    n++;
                    items.Add($"{shells[i].Name}: signed volume {F(volumes[i])} m3 is not positive");
                }
            }
            list.Add(Count(CheckNames.OutwardNormals, n, items, required.OutwardNormals));
        }

        // Matching expected shells to repaired shells: by anchor where expected.json gives one, else best fit (D-020).
        var measures = shells.Select((s, i) => Measure(s, topo[i], volumes[i], spec.Tolerances.Weld)).ToArray();
        var exp = expected.Expected.Shells;
        var match = Match(exp, measures, expected.Tolerances, shells);

        // euler: V − E + F = 2 − 2·genus for each matched shell
        {
            var items = new List<string>();
            for (var e = 0; e < exp.Count; e++)
            {
                var j = match[e];
                if (j < 0) continue; // unmatched: the measure checks fail it
                var want = 2 - 2 * exp[e].Genus;
                if (topo[j].Euler != want)
                    items.Add($"{shells[j].Name}: V-E+F = {topo[j].V}-{topo[j].E}+{topo[j].F} = {topo[j].Euler}, expected {want} (genus {exp[e].Genus})");
            }
            list.Add(Count(CheckNames.Euler, items.Count, items));
        }

        // noSelfIntersections
        {
            var items = new List<string>();
            var n = 0;
            foreach (var s in shells)
            {
                var k = SelfIntersection.CountIntersectingPairs(s);
                n += k;
                if (k > 0) items.Add($"{s.Name}: {k} intersecting triangle pairs");
            }
            list.Add(Count(CheckNames.NoSelfIntersections, n, items, required.NoSelfIntersections));
        }

        // minGap_m: smallest distance between any two shells
        {
            var limit = required.MinGap_m;
            var closest = ShellGap.Closest(shells);
            if (closest is null)
                list.Add(Entry(CheckNames.MinGap, true, 0, limit, "fewer than two shells: no gap to measure"));
            else
            {
                var c = closest.Value;
                list.Add(Entry(CheckNames.MinGap, c.Distance >= limit - ObjPrecision, c.Distance, limit,
                    $"closest pair {shells[c.ShellA].Name} / {shells[c.ShellB].Name}: {F(c.Distance)} m"));
            }
        }

        // minEdge_m: shortest edge
        {
            var limit = required.MinEdge_m;
            var best = double.PositiveInfinity;
            var where = "";
            foreach (var s in shells)
                foreach (var (a, b, c) in s.Triangles)
                    foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
                    {
                        var len = Vec3.Distance(s.Vertices[p], s.Vertices[q]);
                        if (len < best) { best = len; where = $"{s.Name}: edge {p + 1}-{q + 1} is {F(len)} m"; }
                    }
            if (double.IsPositiveInfinity(best)) list.Add(Entry(CheckNames.MinEdge, false, 0, limit, "no edges"));
            else list.Add(Entry(CheckNames.MinEdge, best >= limit - ObjPrecision, best, limit, "shortest " + where));
        }

        // shellCount
        list.Add(Entry(CheckNames.ShellCount, shells.Count == expected.Expected.ShellCount, shells.Count, expected.Expected.ShellCount,
            $"{shells.Count} shells in repaired.obj, {expected.Expected.ShellCount} expected"));

        // volume, footprint, height of matched shells
        list.Add(Compare(CheckNames.Volume, exp, match, shells, x => x.Volume_m3, j => measures[j].Volume,
            e => expected.Tolerances.VolumeRel * exp[e].Volume_m3));
        list.Add(Compare(CheckNames.Footprint, exp, match, shells, x => x.FootprintArea_m2, j => measures[j].Footprint,
            e => expected.Tolerances.AreaRel * exp[e].FootprintArea_m2));
        list.Add(Compare(CheckNames.MaxHeight, exp, match, shells, x => x.MaxHeight_m, j => measures[j].MaxZ,
            _ => expected.Tolerances.Height_m));

        // groundContact: every shell's lowest Z is 0 within weld
        {
            var weld = spec.Tolerances.Weld;
            var worst = 0.0;
            var items = new List<string>();
            for (var i = 0; i < shells.Count; i++)
            {
                var off = Math.Abs(measures[i].MinZ);
                worst = Math.Max(worst, off);
                if (off > weld) items.Add($"{shells[i].Name}: lowest Z is {F(measures[i].MinZ)} m");
            }
            list.Add(Entry(CheckNames.GroundContact, items.Count == 0 && shells.Count > 0, worst, weld,
                items.Count == 0 ? (shells.Count > 0 ? "every shell stands on Z = 0" : "no shells") : Join(items)));
        }

        // mustMerge / mustStaySeparate: where did each input block go?
        var groupsMerge = expected.Expected.MustMerge;
        var groupsApart = expected.Expected.MustStaySeparate;
        BlockLocator? locator = null;
        string? inputError = null;
        if (groupsMerge.Count + groupsApart.Count > 0)
        {
            var (input, error) = loadInput();
            if (input is null) inputError = error ?? "input.obj could not be read";
            else
            {
                var t = spec.Tolerances;
                locator = new BlockLocator(input, spec.Origin, shells, t.MinGap + t.GroundSnap + t.MinEdge);
            }
        }
        list.Add(Groups(CheckNames.MustMerge, groupsMerge, locator, inputError, shells, merge: true));
        list.Add(Groups(CheckNames.MustStaySeparate, groupsApart, locator, inputError, shells, merge: false));

        return list;
    }

    // ------------------------------------------------------------------ shells from the mesh

    /// <summary>
    /// One <see cref="Shell"/> per object that has usable faces. Parse problems (counted, never fatal so the other
    /// checks still report): bad or repeated names, empty objects, faces that are not triangles (fan-triangulated
    /// for the other checks), faces that repeat a vertex (left out of the other checks).
    /// </summary>
    private static List<Shell> BuildShells(Mesh mesh, out List<string> problems)
    {
        problems = new List<string>();
        var shells = new List<Shell>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var nameUses = mesh.Objects.GroupBy(o => o.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        for (var oi = 0; oi < mesh.Objects.Count; oi++)
        {
            var obj = mesh.Objects[oi];
            // Details must tell shells apart even when the solver repeated a name.
            var label = obj.Name.Length == 0 ? "(faces before any o line)" : nameUses[obj.Name] > 1 ? $"{obj.Name} (object {oi + 1})" : obj.Name;
            if (!ShellName.IsMatch(obj.Name)) problems.Add($"object {label} is not named shell_NNN");
            else if (!seen.Add(obj.Name)) problems.Add($"object name {label} is used twice");
            if (obj.Faces.Count == 0) { problems.Add($"object {label} has no faces"); continue; }

            var tris = new List<(int, int, int)>();
            int polys = 0, repeated = 0;
            foreach (var f in obj.Faces)
            {
                if (f.Distinct().Count() != f.Count) { repeated++; continue; }
                if (!f.IsTriangle) polys++;
                for (var k = 1; k + 1 < f.Count; k++) tris.Add((f[0], f[k], f[k + 1]));
            }
            if (polys > 0) problems.Add($"object {label} has {polys} faces that are not triangles");
            if (repeated > 0) problems.Add($"object {label} has {repeated} faces that repeat a vertex");
            if (tris.Count > 0) shells.Add(new Shell(obj.Name.Length == 0 ? "(unnamed)" : label, mesh.Vertices, tris));
        }
        if (mesh.Objects.Count == 0) problems.Add("no objects");
        return shells;
    }

    // ------------------------------------------------------------------ measures and matching

    public readonly record struct ShellMeasure(double Volume, double Footprint, double MinZ, double MaxZ, int? Genus);

    /// <summary>
    /// Volume (signed), footprint = area of the downward-facing faces lying at the shell's lowest Z (within
    /// <paramref name="weld"/>), lowest and highest Z, and genus from the Euler number if it is a valid one.
    /// </summary>
    public static ShellMeasure Measure(Shell s, Topology topo, double volume, double weld)
    {
        double minZ = s.Bounds.Min.Z, maxZ = s.Bounds.Max.Z;
        double footprint = 0;
        foreach (var t in s.Tris)
        {
            var n = t.Normal;
            if (n.Z >= 0) continue;
            if (t.A.Z - minZ > weld || t.B.Z - minZ > weld || t.C.Z - minZ > weld) continue;
            footprint += -n.Z * 0.5; // projected (plan) area
        }
        var chi = topo.Euler;
        int? genus = chi <= 2 && chi % 2 == 0 ? (2 - chi) / 2 : null;
        return new ShellMeasure(volume, footprint, minZ, maxZ, genus);
    }

    /// <summary>
    /// For each expected shell, the repaired shell it is compared with (−1: none), one-to-one. An expected shell
    /// with an <c>anchor_m</c> may only pair with a repaired shell that contains the anchor in plan (D-017); if none
    /// does, it stays unmatched. Among the allowed pairs (all pairs for shells without an anchor), the total of the
    /// errors in tolerance units (volume, footprint, height) plus one per genus mismatch is minimised.
    /// </summary>
    public static int[] Match(IReadOnlyList<ExpectedShell> exp, IReadOnlyList<ShellMeasure> got, CheckTolerances tol,
        IReadOnlyList<Shell> shells)
    {
        var cost = new double[exp.Count, got.Count];
        var allowed = new bool[exp.Count, got.Count];
        for (var e = 0; e < exp.Count; e++)
            for (var j = 0; j < got.Count; j++)
            {
                var x = exp[e];
                var g = got[j];
                allowed[e, j] = x.Anchor_m is not { Count: 2 } a || shells[j].ContainsInPlan(a[0], a[1]);
                cost[e, j] = !allowed[e, j]
                    ? Forbidden
                    : Units(g.Volume - x.Volume_m3, tol.VolumeRel * x.Volume_m3) +
                      Units(g.Footprint - x.FootprintArea_m2, tol.AreaRel * x.FootprintArea_m2) +
                      Units(g.MaxZ - x.MaxHeight_m, tol.Height_m) +
                      (g.Genus == x.Genus ? 0 : 1);
            }
        var match = Assignment.Solve(cost);
        for (var e = 0; e < match.Length; e++)
            if (match[e] >= 0 && !allowed[e, match[e]]) match[e] = -1;
        return match;
    }

    /// <summary>Cost of a pair the anchor rules out: above any sum of allowed costs (each term is capped at 1e6).</summary>
    private const double Forbidden = 1e12;

    private static double Units(double error, double tolerance)
    {
        var u = Math.Abs(error) / Math.Max(tolerance, 1e-9);
        return Math.Min(u, 1e6);
    }

    private static CheckEntry Compare(string name, IReadOnlyList<ExpectedShell> exp, int[] match, IReadOnlyList<Shell> shells,
        Func<ExpectedShell, double> want, Func<int, double> got, Func<int, double> allowed)
    {
        if (exp.Count == 0) return Entry(name, true, 0, 0, "no shells expected");
        var items = new List<string>();
        double worstRatio = -1, worstValue = 0, worstLimit = 0;
        var pass = true;
        for (var e = 0; e < exp.Count; e++)
        {
            var w = want(exp[e]);
            var j = match[e];
            double value, ratio;
            bool ok;
            if (j < 0)
            {
                value = 0;
                ok = false;
                ratio = double.PositiveInfinity;
                items.Add(exp[e].Anchor_m is { Count: 2 } a
                    ? $"expected shell {e} ({F(w)}): no repaired shell left that contains its anchor ({F(a[0])}, {F(a[1])})"
                    : $"expected shell {e} ({F(w)}): no repaired shell to compare");
            }
            else
            {
                value = got(j);
                var err = Math.Abs(value - w);
                var tol = allowed(e);
                ok = err <= tol + 1e-12 * Math.Max(1, Math.Abs(w));
                ratio = tol > 0 ? err / tol : (err > 0 ? double.PositiveInfinity : 0);
                items.Add($"{shells[j].Name}: {F(value)} vs {F(w)} (tol {F(tol)}){(ok ? "" : " FAIL")}");
            }
            pass &= ok;
            if (ratio > worstRatio) { worstRatio = ratio; worstValue = value; worstLimit = w; }
        }
        return Entry(name, pass, worstValue, worstLimit, Join(items));
    }

    // ------------------------------------------------------------------ block groups

    private static CheckEntry Groups(string name, IReadOnlyList<IReadOnlyList<string>> groups, BlockLocator? locator,
        string? inputError, IReadOnlyList<Shell> shells, bool merge)
    {
        if (groups.Count == 0) return Entry(name, true, 0, 0, "no groups");
        if (locator is null) return Entry(name, false, groups.Count, 0, $"cannot locate blocks: {inputError}");

        var items = new List<string>();
        var violated = 0;
        foreach (var g in groups)
        {
            var locs = g.Select(b => (Block: b, Loc: locator.Locate(b))).ToList();
            var missing = locs.Where(l => !l.Loc.Found).Select(l => l.Block).ToList();
            var where = string.Join(", ", locs.Select(l => $"{l.Block} in {(l.Loc.Found ? shells[l.Loc.Shell].Name : "not found")}"));
            bool ok;
            if (missing.Count > 0) ok = false;
            else if (merge) ok = locs.Select(l => l.Loc.Shell).Distinct().Count() == 1;
            else ok = locs.Select(l => l.Loc.Shell).Distinct().Count() == locs.Count;
            if (!ok) violated++;
            items.Add($"[{string.Join(",", g)}] {where}{(ok ? "" : " FAIL")}");
        }
        return Entry(name, violated == 0, violated, 0, Join(items));
    }

    // ------------------------------------------------------------------ entries

    private static CheckEntry Count(string name, IReadOnlyCollection<string> problems, string okText) =>
        Entry(name, problems.Count == 0, problems.Count, 0, problems.Count == 0 ? okText : Join(problems));

    private static CheckEntry Count(string name, int value, IEnumerable<string> items, bool required = true)
    {
        var list = items.ToList();
        var detail = list.Count == 0 ? "none" : Join(list);
        if (!required) detail = "not required by expected.json; " + detail;
        return Entry(name, value == 0 || !required, value, 0, detail);
    }

    private static string Join(IReadOnlyCollection<string> items) =>
        items.Count <= MaxDetailItems
            ? string.Join("; ", items)
            : string.Join("; ", items.Take(MaxDetailItems)) + $"; ... {items.Count - MaxDetailItems} more";

    public static CheckEntry Entry(string name, bool pass, double value, double limit, string detail) => new()
    {
        Name = name,
        Pass = pass,
        Value = Clean(value),
        Limit = Clean(limit),
        Extra = new Dictionary<string, JsonElement> { ["detail"] = JsonSerializer.SerializeToElement(detail) },
    };

    /// <summary>Finite, rounded to 9 decimals so result.json stays readable and byte-stable.</summary>
    private static double Clean(double v)
    {
        if (!double.IsFinite(v)) return 0;
        var r = Math.Round(v, 9);
        return r == 0 ? 0 : r; // no "-0" in result.json
    }

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}
