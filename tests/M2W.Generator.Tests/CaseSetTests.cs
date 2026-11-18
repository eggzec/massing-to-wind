using M2W.Geometry;
using M2W.Geometry.Json;
using M2W.Geometry.Models;
using Xunit;

namespace M2W.Generator.Tests;

public class PlanTests
{
    [Fact]
    public void Plan_has_the_milestone_counts()
    {
        var plan = CasePlan.Make(1);
        Assert.Equal(10 + 12 * 20 + 40, plan.Count);
        Assert.Equal(10, plan.Count(p => p.Recipe.Kind == Recipe.Clean && p.Recipe.Faults.Count == 0));
        foreach (var f in ExpectedSpec.KnownFaults)
        {
            Assert.Equal(20, plan.Count(p => p.Recipe.Kind == Recipe.SingleFault && p.Recipe.Faults.SequenceEqual(new[] { f })));
            Assert.True(plan.Count(p => p.Recipe.Has(f)) >= 25, $"{f} appears in too few cases");
        }
        var combined = plan.Where(p => p.Recipe.Kind == Recipe.Combined).ToList();
        Assert.Equal(40, combined.Count);
        Assert.All(combined, p => Assert.InRange(p.Recipe.Faults.Count, 2, 4));
        Assert.All(combined, p => Assert.Equal(p.Recipe.Faults.Count, p.Recipe.Faults.Distinct().Count()));
    }

    [Fact]
    public void Ids_are_unique_and_never_collide_with_the_hand_made_example()
    {
        var plan = CasePlan.Make(1);
        Assert.Equal(plan.Count, plan.Select(p => p.CaseId).Distinct().Count());
        Assert.Equal(plan.Count, plan.Select(p => p.Seed).Distinct().Count());
        Assert.All(plan, p => Assert.Matches(@"^case_\d{4}$", p.CaseId));
        Assert.All(plan, p => Assert.True(int.Parse(p.CaseId[5..]) >= 101));
        Assert.DoesNotContain(plan, p => p.CaseId == "case_0007");
    }

    [Fact]
    public void A_different_seed_gives_a_different_order_and_different_seeds()
    {
        var a = CasePlan.Make(1);
        var b = CasePlan.Make(90001);
        Assert.NotEqual(a.Select(p => string.Join(",", p.Recipe.Faults)), b.Select(p => string.Join(",", p.Recipe.Faults)));
        Assert.Empty(a.Select(p => p.Seed).Intersect(b.Select(p => p.Seed)));
        Assert.Equal(a.Select(p => p.CaseId), CasePlan.Make(1).Select(p => p.CaseId));
    }
}

public class GroundTruthTests
{
    public static IEnumerable<object[]> CaseIndexes() => Enumerable.Range(0, CasePlan.Make(1).Count).Select(i => new object[] { i });

    /// <summary>
    /// The emitted clean objects, measured by vertical ray casting at a point inside every grid cell, enclose exactly
    /// the height field of the input blocks. So the meshes really are the model the answers are computed from.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Emitted_meshes_enclose_exactly_the_modelled_solid(int index)
    {
        var c = Cases.Seed1[index];
        var hf = HeightField.Build(c.InputBlocks);
        for (var i = 0; i < hf.Nx; i++)
            for (var j = 0; j < hf.Ny; j++)
            {
                var x = hf.Xs[i] + 0.3719 * (hf.Xs[i + 1] - hf.Xs[i]);
                var y = hf.Ys[j] + 0.6127 * (hf.Ys[j + 1] - hf.Ys[j]);
                var len = Topo.ColumnLength(c.CleanObjects, x, y);
                Assert.True(Math.Abs(len - hf.Top[i, j]) < 1e-6, $"{c.CaseId}: column at ({x}, {y}) is {len}, model says {hf.Top[i, j]}");
            }
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Every_clean_object_is_a_closed_outward_manifold_shell(int index)
    {
        var c = Cases.Seed1[index];
        foreach (var o in c.CleanObjects)
        {
            Assert.Equal((0, 0, 0), Topo.EdgeStats(o));
            Assert.True(Topo.Volume(o) > 0, $"{c.CaseId} {o.Name}");
            Assert.True(o.V.Min(v => v.Z) >= 0);
        }
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Expected_answer_matches_the_corrected_model(int index)
    {
        var c = Cases.Seed1[index];
        var e = c.Expected.Expected;
        var hf = HeightField.Build(c.ExpectedBlocks);
        Assert.Equal(hf.ComponentCount, e.ShellCount);
        Assert.Equal(hf.TotalVolume(), e.Shells.Sum(s => s.Volume_m3), 4);
        Assert.Equal(c.ExpectedBlocks.Max(b => b.Height), e.Shells.Max(s => s.MaxHeight_m), 9);

        // Shells are listed largest first.
        Assert.Equal(e.Shells.OrderByDescending(s => s.Volume_m3).Select(s => s.Volume_m3), e.Shells.Select(s => s.Volume_m3));

        // A merge is expected exactly where the design put blocks together (overlap, touching, closed gap).
        var merged = c.Groups.Where(g => g.Relation is Relation.Overlap or Relation.Touching or Relation.GapBelow)
            .Select(g => string.Join(",", g.Expected.Select(b => b.Name).OrderBy(n => n, StringComparer.Ordinal))).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(merged, e.MustMerge.Select(m => string.Join(",", m)).OrderBy(x => x, StringComparer.Ordinal));
        foreach (var g in c.Groups.Where(g => g.Relation == Relation.GapAbove))
            Assert.Contains(e.MustStaySeparate, p => p.SequenceEqual(g.Expected.Select(b => b.Name).OrderBy(n => n, StringComparer.Ordinal)));

        Assert.Equal(c.Case.Tolerances.MinGap, c.Expected.Checks.MinGap_m);
        Assert.Equal(c.Case.Tolerances.MinEdge, c.Expected.Checks.MinEdge_m);
        Assert.Equal(ExpectedSpec.KnownFaults.Where(c.Recipe.Has), c.Expected.FaultsInjected);
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Geometric_faults_stay_inside_the_ambiguity_budget(int index)
    {
        var c = Cases.Seed1[index];
        var clean = HeightField.Build(c.CleanBlocks).TotalVolume();
        var input = HeightField.Build(c.InputBlocks).TotalVolume();
        var expected = c.Expected.Expected.Shells.Sum(s => s.Volume_m3);
        Assert.True(Math.Abs(input - clean) <= CaseBuilder.AmbiguityBudget * expected + 1e-6, $"{c.CaseId}: {input} vs {clean}");
        if (!c.Recipe.Has("shortEdge")) Assert.Equal(clean, input, 9);
    }

    /// <summary>blocks and anchor_m (D-017): every block is in exactly one shell, and each anchor (with 0.5 m to spare
    /// in every direction) lies inside that shell's blocks and outside every other block.</summary>
    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Shell_blocks_and_anchors_identify_each_shell(int index)
    {
        var c = Cases.Seed1[index];
        var shells = c.Expected.Expected.Shells;
        var all = shells.SelectMany(s => s.Blocks!).ToList();
        Assert.Equal(c.ExpectedBlocks.Select(b => b.Name).OrderBy(n => n, StringComparer.Ordinal), all.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(c.Expected.Expected.MustMerge.Select(m => string.Join(",", m)).OrderBy(x => x, StringComparer.Ordinal),
            shells.Where(s => s.Blocks!.Count > 1).Select(s => string.Join(",", s.Blocks!)).OrderBy(x => x, StringComparer.Ordinal));
        foreach (var s in shells)
        {
            Assert.Equal(2, s.Anchor_m!.Count);
            foreach (var (dx, dy) in new[] { (0.0, 0.0), (0.5, 0.0), (-0.5, 0.0), (0.0, 0.5), (0.0, -0.5) })
            {
                var g = c.Site.ToGrid(new P2(s.Anchor_m[0] + dx, s.Anchor_m[1] + dy));
                foreach (var b in c.ExpectedBlocks)
                {
                    var inside = b.TopAt(g.X, g.Y) > 0;
                    if (!s.Blocks!.Contains(b.Name)) Assert.False(inside, $"{c.CaseId}: anchor of {string.Join("+", s.Blocks!)} is in {b.Name}");
                }
                Assert.True(s.Blocks!.Any(n => c.ExpectedBlocks.Single(b => b.Name == n).TopAt(g.X, g.Y) > 0),
                    $"{c.CaseId}: anchor {s.Anchor_m[0]}, {s.Anchor_m[1]} (+{dx}, {dy}) is not inside {string.Join("+", s.Blocks!)}");
            }
        }
    }

    private static string Describe(Block b) =>
        $"{b.Name}|{string.Join(";", b.Rects)}|{string.Join(";", b.Tiers)}|{string.Join(";", b.FloorHeights)}";

    /// <summary>The designed (emitted) model and the corrected model differ only where a gap below minGap is closed.</summary>
    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Clean_and_expected_models_differ_only_by_closed_gaps(int index)
    {
        var c = Cases.Seed1[index];
        foreach (var g in c.Groups)
        {
            Assert.Equal(g.Clean.Count, g.Expected.Count);
            for (var k = 0; k < g.Clean.Count; k++)
            {
                var same = Describe(g.Clean[k]) == Describe(g.Expected[k]);
                if (g.Relation == Relation.GapBelow && k == 1) Assert.False(same, c.CaseId);
                else Assert.True(same, $"{c.CaseId} {g.Clean[k].Name}");
            }
        }
        // Relations hold in the emitted model itself.
        foreach (var g in c.Groups)
        {
            var hf = HeightField.Build(g.Clean);
            var expectComponents = g.Relation is Relation.GapBelow or Relation.GapAbove ? 2 : 1;
            Assert.Equal(expectComponents, hf.ComponentCount);
        }
    }

    [Fact]
    public void Closed_gaps_add_exactly_the_filled_strip()
    {
        foreach (var c in Cases.Seed1)
            foreach (var g in c.Groups.Where(g => g.Relation == Relation.GapBelow))
            {
                var a = g.Expected[0];
                var b = g.Clean[1];
                var fill = HeightField.Build(g.Expected).TotalVolume() - HeightField.Build(g.Clean).TotalVolume();
                Assert.True(fill > 0, c.CaseId);
                // The strip is the gap times B's facing wall area (B is no taller than A along the face).
                Assert.True(fill <= g.Gap * Math.Max(b.Bounds.Width, b.Bounds.Depth) * b.Height + 1e-6, c.CaseId);
                Assert.True(b.Height <= a.Height + 1e-9, c.CaseId);
            }
    }
}

public class FaultPresenceTests
{
    private static IEnumerable<BuiltCase> With(string fault) => Cases.Seed1.Where(c => c.Recipe.Has(fault));

    private static IEnumerable<BuiltCase> Only(string fault) =>
        Cases.Seed1.Where(c => c.Recipe.Kind == Recipe.SingleFault && c.Recipe.Has(fault));

    private static Vec3 Local(BuiltCase c, Vec3 v) => v - c.Case.Origin;

    [Fact]
    public void Clean_controls_already_meet_the_output_rules()
    {
        var clean = Cases.Seed1.Where(c => c.Recipe.Kind == Recipe.Clean).ToList();
        Assert.Equal(10, clean.Count);
        foreach (var c in clean)
        {
            var mesh = c.Input;
            var t = c.Case.Tolerances;
            Assert.Equal(Vec3.Zero, c.Case.Origin);
            Assert.Empty(c.Expected.FaultsInjected);
            Assert.Empty(c.Expected.Expected.MustMerge);
            Assert.Equal(c.Expected.Expected.ShellCount, mesh.Objects.Count);
            var volumes = new List<double>();
            foreach (var o in mesh.Objects)
            {
                Assert.All(o.Faces, f => Assert.Equal(3, f.Count));
                Assert.Equal((0, 0, 0), Topo.EdgeStats(o.Faces));
                Assert.All(o.Faces, f => Assert.True(Topo.MinTriangleAngle(mesh.Vertices, f) >= t.MinAngleDeg));
                Assert.True(Topo.MinEdge(mesh.Vertices, o.Faces) >= t.MinEdge);
                var b = MeshMeasures.BoundingBox(mesh, o.Faces);
                Assert.Equal(0, b.Min.Z);
                volumes.Add(MeshMeasures.SignedVolume(mesh, o.Faces));
            }
            Assert.Equal(c.Expected.Expected.Shells.Select(s => s.Volume_m3).OrderBy(v => v), volumes.OrderBy(v => v).Select(v => Math.Round(v, 6)),
                new Tol(1e-4));
        }
    }

    private sealed class Tol(double eps) : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) <= eps * Math.Max(1, Math.Abs(a));
        public int GetHashCode(double v) => 0;
    }

    [Fact]
    public void Each_fault_has_at_least_20_cases_and_shows_in_the_input()
    {
        foreach (var f in ExpectedSpec.KnownFaults) Assert.True(With(f).Count() >= 20, f);

        Assert.All(With("farCoordinates"), c => Assert.True(Math.Abs(c.Case.Origin.X) > 1e5 && Math.Abs(c.Case.Origin.Y) > 1e5));
        Assert.All(Cases.Seed1.Where(c => !c.Recipe.Has("farCoordinates")), c => Assert.Equal(Vec3.Zero, c.Case.Origin));
        Assert.All(Cases.Seed1, c => Assert.All(c.Input.Vertices, v => Assert.True(Local(c, v).Length < 2000)));

        Assert.All(With("stackedFloors"), c => Assert.Contains(c.Input.Objects, o => o.Name.Contains("_F")));
        Assert.All(Cases.Seed1.Where(c => !c.Recipe.Has("stackedFloors")), c => Assert.DoesNotContain(c.Input.Objects, o => o.Name.Contains("_F")));

        foreach (var (fault, sign) in new[] { ("floatingBlock", 1), ("sunkBlock", -1) })
            Assert.All(With(fault), c =>
            {
                var snap = c.Case.Tolerances.GroundSnap;
                var mins = BlockMinZ(c);
                Assert.Contains(mins.Values, z => sign * z > 0.05 * snap - 1e-3 && sign * z < snap);
            });
        Assert.All(Cases.Seed1.Where(c => !c.Recipe.Has("floatingBlock") && !c.Recipe.Has("sunkBlock")),
            c => Assert.All(BlockMinZ(c).Values, z => Assert.True(Math.Abs(z) <= c.Case.Tolerances.Weld)));
    }

    private static Dictionary<string, double> BlockMinZ(BuiltCase c)
    {
        var d = new Dictionary<string, double>();
        foreach (var o in c.Input.Objects)
        {
            var block = o.Name.Split('_')[0];
            var z = o.Faces.SelectMany(f => f).Min(i => c.Input.Vertices[i].Z) - c.Case.Origin.Z;
            d[block] = d.TryGetValue(block, out var prev) ? Math.Min(prev, z) : z;
        }
        return d;
    }

    /// <summary>Objects with vertices welded by position (to 1e-5 m), so topology can be read from the file as written.</summary>
    private static List<List<int[]>> Welded(BuiltCase c)
    {
        var result = new List<List<int[]>>();
        foreach (var o in c.Input.Objects)
        {
            var ids = new Dictionary<(long, long, long), int>();
            int Id(Vec3 v)
            {
                var k = ((long)Math.Round(v.X * 1e5), (long)Math.Round(v.Y * 1e5), (long)Math.Round(v.Z * 1e5));
                if (!ids.TryGetValue(k, out var id)) { id = ids.Count; ids[k] = id; }
                return id;
            }
            result.Add(o.Faces.Select(f => f.Select(i => Id(c.Input.Vertices[i])).ToArray()).ToList());
        }
        return result;
    }

    [Fact]
    public void Topology_faults_show_in_the_written_input()
    {
        Assert.All(Only("openSurface"), c => Assert.Contains(Welded(c), o => Topo.EdgeStats(o).Free > 0));
        Assert.All(Only("flippedFaces"), c =>
        {
            var welded = Welded(c);
            Assert.True(c.Input.Objects.Where((o, i) =>
                Topo.EdgeStats(welded[i]).Misoriented > 0 || MeshMeasures.SignedVolume(c.Input, o.Faces) < 0).Any(), c.CaseId);
        });
        Assert.All(Only("duplicateVertices"), c => Assert.True(HasNearDuplicates(c), c.CaseId));
        Assert.All(Only("sliver"), c => Assert.Contains(c.Input.Objects, o =>
            o.Faces.Any(f => f.Count == 3 && Topo.MinTriangleAngle(c.Input.Vertices, f) < c.Case.Tolerances.MinAngleDeg)));
        Assert.All(With("shortEdge"), c => Assert.True(
            c.Input.Objects.Any(o => Topo.MinEdge(c.Input.Vertices, o.Faces) < c.Case.Tolerances.MinEdge), c.CaseId));

        // Faults that are not injected do not appear by accident.
        foreach (var c in Cases.Seed1.Where(c => c.Recipe.Kind == Recipe.Clean || c.Recipe.Kind == Recipe.SingleFault))
        {
            var welded = Welded(c);
            if (!c.Recipe.Has("openSurface") && !c.Recipe.Has("duplicateVertices"))
                Assert.All(welded, o => Assert.Equal(0, Topo.EdgeStats(o).Free));
            if (!c.Recipe.Has("flippedFaces") && !c.Recipe.Has("duplicateVertices") && !c.Recipe.Has("openSurface"))
                Assert.All(welded, o => Assert.Equal(0, Topo.EdgeStats(o).Misoriented));
            if (!c.Recipe.Has("shortEdge") && !c.Recipe.Has("sliver"))
                Assert.All(c.Input.Objects, o => Assert.True(Topo.MinEdge(c.Input.Vertices, o.Faces) >= c.Case.Tolerances.MinEdge, c.CaseId));
            if (!c.Recipe.Has("duplicateVertices")) Assert.False(HasNearDuplicates(c), c.CaseId);
        }
    }

    private static bool HasNearDuplicates(BuiltCase c)
    {
        var weld = c.Case.Tolerances.Weld;
        foreach (var o in c.Input.Objects)
        {
            var used = o.Faces.SelectMany(f => f).Distinct().Select(i => c.Input.Vertices[i]).ToList();
            var buckets = new HashSet<(long, long, long)>();
            foreach (var v in used)
            {
                var k = ((long)Math.Floor(v.X / weld), (long)Math.Floor(v.Y / weld), (long)Math.Floor(v.Z / weld));
                if (!buckets.Add(k)) return true;
            }
            for (var i = 0; i < used.Count; i++)
                for (var j = i + 1; j < used.Count; j++)
                    if (Vec3.Distance(used[i], used[j]) < weld) return true;
        }
        return false;
    }

    [Fact]
    public void Layout_faults_are_in_the_model()
    {
        foreach (var c in With("overlap"))
        {
            var g = Assert.Single(c.Groups, x => x.Relation == Relation.Overlap);
            var sum = g.Clean.Sum(b => b.Volume());
            Assert.True(HeightField.Build(g.Clean).TotalVolume() < sum - 1, c.CaseId);
        }
        foreach (var c in With("touchingWalls"))
        {
            var g = Assert.Single(c.Groups, x => x.Relation == Relation.Touching);
            Assert.Equal(g.Clean.Sum(b => b.Volume()), HeightField.Build(g.Clean).TotalVolume(), 6);
            Assert.Equal(1, HeightField.Build(g.Clean).ComponentCount);
        }
        var ratios = new List<double>();
        foreach (var c in With("smallGap"))
        {
            var g = Assert.Single(c.Groups, x => x.Relation is Relation.GapBelow or Relation.GapAbove);
            var hf = HeightField.Build(g.Clean);
            Assert.Equal(2, hf.ComponentCount);
            Assert.Equal(g.Gap, hf.Distance(0, 1), 6);
            var ratio = g.Gap / c.Case.Tolerances.MinGap;
            Assert.True(g.Relation == Relation.GapBelow ? ratio < 1 : ratio > 1, c.CaseId);
            if (c.Recipe.Kind == Recipe.SingleFault) ratios.Add(Math.Round(ratio, 3));
        }
        Assert.True(ratios.Count(r => r == 0.9) >= 4, "need 0.9 x minGap boundary cases");
        Assert.True(ratios.Count(r => r == 1.1) >= 4, "need 1.1 x minGap boundary cases");
        Assert.True(ratios.Count(r => r < 1) >= 10 && ratios.Count(r => r > 1) >= 10);
    }
}

public class FileTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "m2w-gen-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static Dictionary<string, byte[]> Snapshot(string dir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), File.ReadAllBytes);

    [Fact]
    public void Same_seed_gives_byte_identical_files_and_they_read_back()
    {
        var a = TempDir();
        var b = TempDir();
        try
        {
            var n = new CaseGenerator().Generate(new M2W.Geometry.Stages.GenerateRequest { CasesDir = a, Seed = 1 });
            new CaseGenerator().Generate(new M2W.Geometry.Stages.GenerateRequest { CasesDir = b, Seed = 1 });
            Assert.Equal(290, n);
            var sa = Snapshot(a);
            var sb = Snapshot(b);
            Assert.Equal(sa.Keys.OrderBy(k => k), sb.Keys.OrderBy(k => k));
            foreach (var k in sa.Keys) Assert.True(sa[k].AsSpan().SequenceEqual(sb[k]), $"{k} differs");
            Assert.Equal(290 * 3 + 1, sa.Count);

            var index = CaseIndex.Load(Path.Combine(a, "index.json"));
            Assert.Equal(290, index.Cases.Count);
            foreach (var e in index.Cases)
            {
                var dir = Path.Combine(a, e.CaseId);
                var spec = CaseSpec.Load(Path.Combine(dir, CaseFiles.Case));
                var exp = ExpectedSpec.Load(Path.Combine(dir, CaseFiles.Expected));
                var mesh = ObjReader.ReadFile(Path.Combine(dir, CaseFiles.Input));
                Assert.Equal(e.CaseId, spec.CaseId);
                Assert.Equal(e.Faults, exp.FaultsInjected);
                Assert.Equal(e.Seed, exp.Seed);
                Assert.NotEmpty(mesh.Objects);
                Assert.All(mesh.Objects, o => Assert.Matches(@"^B\d{2}(_F\d{2})?$", o.Name));
                var text = File.ReadAllText(Path.Combine(dir, CaseFiles.Input));
                foreach (var f in ExpectedSpec.KnownFaults) Assert.DoesNotContain(f, text); // no answers in the solver's file
            }
        }
        finally
        {
            Directory.Delete(a, true);
            Directory.Delete(b, true);
        }
    }

    [Fact]
    public void Regenerating_removes_old_cases_but_keeps_other_folders()
    {
        var a = TempDir();
        try
        {
            var stale = Path.Combine(a, "case_9999");
            Directory.CreateDirectory(stale);
            File.WriteAllText(Path.Combine(stale, "case.json"), "{}");
            var keep = Path.Combine(a, "_example", "case_0007");
            Directory.CreateDirectory(keep);
            File.WriteAllText(Path.Combine(keep, "case.json"), "{}");
            new CaseGenerator().Generate(new M2W.Geometry.Stages.GenerateRequest { CasesDir = a, Seed = 3 });
            Assert.False(Directory.Exists(stale));
            Assert.True(File.Exists(Path.Combine(keep, "case.json")));
        }
        finally
        {
            Directory.Delete(a, true);
        }
    }

    /// <summary>The committed cases/ folder is exactly what `m2w generate cases` (seed 1) produces today.</summary>
    [Fact]
    public void Committed_cases_match_the_generator()
    {
        var repo = FindRepo();
        var cases = Path.Combine(repo, "cases");
        if (!File.Exists(Path.Combine(cases, "index.json"))) return; // not generated yet
        var a = TempDir();
        try
        {
            new CaseGenerator().Generate(new M2W.Geometry.Stages.GenerateRequest { CasesDir = a, Seed = 1 });
            var fresh = Snapshot(a);
            var committed = Snapshot(cases).Where(kv => !kv.Key.StartsWith("_example/") && kv.Key != "README.md")
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            Assert.Equal(fresh.Keys.OrderBy(k => k, StringComparer.Ordinal), committed.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var k in fresh.Keys) Assert.True(fresh[k].AsSpan().SequenceEqual(committed[k]), $"cases/{k} is out of date: run m2w generate cases");
        }
        finally
        {
            Directory.Delete(a, true);
        }
    }

    private static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "M2W.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
