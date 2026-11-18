using M2W.Checker;
using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Geometry.Stages;
using Xunit;
using Xunit.Abstractions;
using static M2W.Checker.Tests.Kit;

namespace M2W.Checker.Tests;

/// <summary>
/// The checker must not be too strict: clean, fault-free massings pass every check with zero repair
/// (origin shift, triangulation and shell naming only; see <see cref="Kit.ZeroRepair"/>).
/// </summary>
public class CleanControlTests
{
    private readonly ITestOutputHelper _log;
    public CleanControlTests(ITestOutputHelper log) => _log = log;

    private static readonly Vec3 MapOrigin = new(530000, 180000, 0);

    private static readonly (double, double)[] Bar = { (0, 0), (40, 0), (40, 12), (0, 12) };
    private static readonly (double, double)[] LShape = { (0, 0), (20, 0), (20, 8), (8, 8), (8, 20), (0, 20) };
    private static readonly (double, double)[] UShape = { (0, 0), (30, 0), (30, 20), (22, 20), (22, 8), (8, 8), (8, 20), (0, 20) };
    private static readonly (double, double)[] Acute = { (0, 0), (30, 0), (4, 9) }; // acute-angled plot corner

    private static (double, double)[] Shift((double X, double Y)[] plan, double dx, double dy) =>
        plan.Select(p => (p.X + dx, p.Y + dy)).ToArray();

    public static IEnumerable<object[]> Massings()
    {
        yield return new object[] { "bar", new[] { "bar" } };
        yield return new object[] { "L", new[] { "L" } };
        yield return new object[] { "U", new[] { "U" } };
        yield return new object[] { "acute", new[] { "acute" } };
        yield return new object[] { "courtyard", new[] { "courtyard" } };
        yield return new object[] { "podiumTower", new[] { "podiumTower" } };
        yield return new object[] { "site", new[] { "bar", "L", "U", "courtyard", "podiumTower" } };
    }

    /// <summary>Builds the clean input (map coordinates, polygons, one object per block) and its expected answer.</summary>
    private static (Mesh Input, ExpectedSpec Expected) Build(string[] parts)
    {
        var m = new Mesh();
        var shells = new List<ExpectedShell>();
        var apart = new List<string>();
        for (var i = 0; i < parts.Length; i++)
        {
            var name = $"B{i + 1:00}";
            apart.Add(name);
            var o = m.AddObject(name);
            var dx = 60.0 * i; // blocks 20+ m apart on one site
            switch (parts[i])
            {
                case "bar": AddPrismPolygons(m, o, Shift(Bar, dx, 0), 0, 15); shells.Add(Sh(480 * 15, 480, 15)); break;
                case "L": AddPrismPolygons(m, o, Shift(LShape, dx, 0), 0, 21); shells.Add(Sh(256 * 21, 256, 21)); break;
                case "U": AddPrismPolygons(m, o, Shift(UShape, dx, 0), 0, 12); shells.Add(Sh(432 * 12, 432, 12)); break;
                case "acute": AddPrismPolygons(m, o, Shift(Acute, dx, 0), 0, 9); shells.Add(Sh(135 * 9, 135, 9)); break;
                case "courtyard":
                    AddCourtyardPolygons(m, o, (dx, 0, dx + 36, 30), (dx + 10, 8, dx + 26, 22), 0, 18);
                    shells.Add(Sh((1080 - 224) * 18, 1080 - 224, 18, genus: 1));
                    break;
                case "podiumTower":
                    AddPodiumTowerPolygons(m, o, (dx, 0, dx + 40, 30), (dx + 10, 5, dx + 30, 25), 6, 90);
                    shells.Add(Sh(1200 * 6 + 400 * 84, 1200, 90));
                    break;
                default: throw new ArgumentException(parts[i]);
            }
        }
        var expected = Expect(shells, apart: apart.Count > 1 ? new[] { apart.ToArray() } : null);
        return (Moved(m, MapOrigin), expected);
    }

    [Theory]
    [MemberData(nameof(Massings))]
    public void Clean_massing_passes_with_zero_repair(string label, string[] parts)
    {
        var (input, expected) = Build(parts);
        var spec = Spec(MapOrigin);
        var r = Grade(ZeroRepair(input, MapOrigin), expected, input, spec);
        Assert.True(r.Pass, $"{label}: {r.Failed()}");
    }

    [Fact]
    public void Clean_massing_survives_the_obj_round_trip()
    {
        // Through the 6-decimal file format, as m2w writes it.
        var (input, expected) = Build(new[] { "bar", "L", "U", "courtyard", "podiumTower" });
        var text = ObjWriter.WriteString(ZeroRepair(input, MapOrigin));
        var r = Grade(ObjReader.ReadString(text), expected, ObjReader.ReadString(ObjWriter.WriteString(input)), Spec(MapOrigin));
        Assert.True(r.Pass, r.Failed());
    }

    /// <summary>
    /// Every generated clean control in <c>cases/</c> (empty <c>faultsInjected</c>), graded through the real
    /// <see cref="CaseChecker"/> from files. No clean cases yet (before the generator's M2 cases land) is not a failure.
    /// </summary>
    [Fact]
    public void Generated_clean_controls_pass_with_zero_repair()
    {
        var checker = new CaseChecker();
        var tmp = Directory.CreateTempSubdirectory("m2w-check-clean-");
        var n = 0;
        var failures = new List<string>();
        try
        {
            foreach (var dir in CaseDiscovery.FindCaseDirs(CasesDir))
            {
                var expected = ExpectedSpec.Load(Path.Combine(dir, CaseFiles.Expected));
                if (expected.FaultsInjected.Count > 0) continue;
                var spec = CaseSpec.Load(Path.Combine(dir, CaseFiles.Case));
                var input = ObjReader.ReadFile(Path.Combine(dir, spec.Files.Input));
                var repairedPath = Path.Combine(tmp.FullName, spec.CaseId + ".obj");
                ObjWriter.WriteFile(ZeroRepair(input, spec.Origin), repairedPath);
                var r = checker.Check(new CheckRequest { CaseDir = dir, Case = spec, Expected = expected, RepairedObjPath = repairedPath });
                n++;
                if (!r.Pass) failures.Add($"{spec.CaseId}: {r.Failed()}");
            }
        }
        finally
        {
            tmp.Delete(true);
        }
        _log.WriteLine($"clean controls in cases/: {n}, failing: {failures.Count}");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}

/// <summary>The hand-made reference case from AGREEMENT.md, graded through files like m2w does.</summary>
public class Example0007Tests
{
    private static CheckResult CheckWith(Mesh repaired)
    {
        var spec = CaseSpec.Load(Path.Combine(Example0007, CaseFiles.Case));
        var expected = ExpectedSpec.Load(Path.Combine(Example0007, CaseFiles.Expected));
        var tmp = Path.Combine(Path.GetTempPath(), $"m2w-check-0007-{Guid.NewGuid():N}.obj");
        try
        {
            ObjWriter.WriteFile(repaired, tmp);
            return new CaseChecker().Check(new CheckRequest { CaseDir = Example0007, Case = spec, Expected = expected, RepairedObjPath = tmp });
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void The_correct_repair_passes()
    {
        var r = CheckWith(Boxes((0, 0, 0, 30.3, 10, 18)));
        Assert.True(r.Pass, r.Failed());
        Assert.Equal("case_0007", r.CaseId);
        Assert.Equal(5454.0, r.Get(CheckNames.Volume).Limit);
    }

    [Fact]
    public void The_unrepaired_input_fails()
    {
        var spec = CaseSpec.Load(Path.Combine(Example0007, CaseFiles.Case));
        var input = ObjReader.ReadFile(Path.Combine(Example0007, CaseFiles.Input));
        var r = CheckWith(ZeroRepair(input, spec.Origin));
        Assert.False(r.Pass);
        foreach (var name in new[] { CheckNames.ShellCount, CheckNames.MinGap, CheckNames.MustMerge, CheckNames.Euler })
            Assert.False(r.Get(name).Pass, name);
    }

    [Fact]
    public void Two_unmerged_shells_fail_merge_gap_and_count()
    {
        var r = CheckWith(Boxes((0, 0, 0, 20, 10, 18), (20.3, 0, 0, 30.3, 10, 18)));
        Assert.False(r.Get(CheckNames.MustMerge).Pass);
        Assert.False(r.Get(CheckNames.MinGap).Pass);
        Assert.False(r.Get(CheckNames.ShellCount).Pass);
        Assert.True(r.Get(CheckNames.Watertight).Pass);
    }

    [Fact]
    public void A_file_that_does_not_parse_fails_without_throwing()
    {
        var spec = CaseSpec.Load(Path.Combine(Example0007, CaseFiles.Case));
        var expected = ExpectedSpec.Load(Path.Combine(Example0007, CaseFiles.Expected));
        var tmp = Path.Combine(Path.GetTempPath(), $"m2w-check-bad-{Guid.NewGuid():N}.obj");
        File.WriteAllText(tmp, "o shell_000\nv 0 0 0\nf 1 2 3\n");
        try
        {
            var r = new CaseChecker().Check(new CheckRequest { CaseDir = Example0007, Case = spec, Expected = expected, RepairedObjPath = tmp });
            Assert.False(r.Get(CheckNames.Parse).Pass);
            Assert.Contains("repaired.obj", r.Get(CheckNames.Parse).Detail());
            Assert.Empty(r.Validate());
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void The_report_is_never_read()
    {
        // A report claiming success that does not even parse must change nothing.
        var spec = CaseSpec.Load(Path.Combine(Example0007, CaseFiles.Case));
        var expected = ExpectedSpec.Load(Path.Combine(Example0007, CaseFiles.Expected));
        var obj = Path.Combine(Path.GetTempPath(), $"m2w-check-rep-{Guid.NewGuid():N}.obj");
        var rep = Path.ChangeExtension(obj, ".json");
        ObjWriter.WriteFile(Boxes((0, 0, 0, 20, 10, 18)), obj);
        File.WriteAllText(rep, "{ \"status\": \"repaired\", garbage");
        try
        {
            var with = new CaseChecker().Check(new CheckRequest { CaseDir = Example0007, Case = spec, Expected = expected, RepairedObjPath = obj, ReportPath = rep });
            var without = new CaseChecker().Check(new CheckRequest { CaseDir = Example0007, Case = spec, Expected = expected, RepairedObjPath = obj });
            Assert.Equal(M2W.Geometry.Json.M2WJson.Serialize(without), M2W.Geometry.Json.M2WJson.Serialize(with));
        }
        finally
        {
            File.Delete(obj);
            File.Delete(rep);
        }
    }
}
