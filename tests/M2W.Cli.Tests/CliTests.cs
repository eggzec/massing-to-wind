using System.Text.Json.Nodes;
using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Geometry.Stages;
using Xunit;

namespace M2W.Cli.Tests;

/// <summary>A temporary workspace with a cases/ and an out/ folder; deleted on dispose.</summary>
public sealed class Workspace : IDisposable
{
    public static string RepoRoot { get; } = FindRoot();
    public static string Example0007 => Path.Combine(RepoRoot, "cases", "_example", "case_0007");

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("m2w-cli-");

    public string Cases => Path.Combine(_dir.FullName, "cases");
    public string Out => Path.Combine(_dir.FullName, "out");

    /// <summary>Copies case_0007 to cases/<paramref name="rel"/>, optionally changing its caseId and faults.</summary>
    public string AddCase(string rel, string? caseId = null, string[]? faults = null)
    {
        var dest = Directory.CreateDirectory(Path.Combine(Cases, rel)).FullName;
        foreach (var f in new[] { CaseFiles.Case, CaseFiles.Expected, CaseFiles.Input })
            File.Copy(Path.Combine(Example0007, f), Path.Combine(dest, f));
        if (caseId is not null)
            foreach (var f in new[] { CaseFiles.Case, CaseFiles.Expected })
                Edit(Path.Combine(dest, f), j => j["caseId"] = caseId);
        if (faults is not null)
            Edit(Path.Combine(dest, CaseFiles.Expected), j => j["faultsInjected"] = new JsonArray(faults.Select(x => (JsonNode)x!).ToArray()));
        return dest;
    }

    public static void Edit(string path, Action<JsonObject> change)
    {
        var j = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        change(j);
        File.WriteAllText(path, j.ToJsonString());
    }

    public void Dispose() => _dir.Delete(recursive: true);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGREEMENT.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

/// <summary>Shifts the input to local coordinates and calls that "repaired". Only for exercising the CLI.</summary>
internal sealed class ShiftOnlyRepairer : ICaseRepairer
{
    public RepairRequest? LastRequest { get; private set; }

    public RepairOutcome Repair(RepairRequest request)
    {
        LastRequest = request;
        var shift = -request.Case.Origin;
        var m = new Mesh(request.Input.Vertices.Select(v => v + shift), request.Input.Objects);
        return new RepairOutcome
        {
            Report = new RepairReport
            {
                SchemaVersion = SchemaVersions.Current, CaseId = request.Case.CaseId, SolverVersion = "test",
                Status = RepairStatus.Repaired, ShiftApplied = shift, ShellCount = 1,
                Operations = new[] { new RepairOperation { Op = "shift" } }, Warnings = Array.Empty<string>(), Runtime_ms = 0,
            },
            Repaired = m,
        };
    }
}

internal sealed class FixedChecker : ICaseChecker
{
    private readonly bool _pass;
    public FixedChecker(bool pass) => _pass = pass;
    public CheckRequest? LastRequest { get; private set; }

    public CheckResult Check(CheckRequest request)
    {
        LastRequest = request;
        return new CheckResult
        {
            SchemaVersion = SchemaVersions.Current, CaseId = request.Case.CaseId, Pass = _pass,
            Checks = new[] { new CheckEntry { Name = "watertight", Pass = _pass, Value = _pass ? 0 : 4, Limit = 0 } },
        };
    }
}

internal sealed class RecordingGenerator : ICaseGenerator
{
    public GenerateRequest? LastRequest { get; private set; }
    public int Generate(GenerateRequest request) { LastRequest = request; return 3; }
}

public class CliTests
{
    private static (int code, string stdout, string stderr) Run(StageSet stages, params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = new CliApp(stages, o, e).Run(args);
        return (code, o.ToString(), e.ToString());
    }

    private static readonly StageSet Stubs = new();

    // ------------------------------------------------------------ run-all

    [Fact]
    public void RunAll_finds_every_folder_containing_case_json_at_any_depth()
    {
        using var ws = new Workspace();
        ws.AddCase("_example/case_0007");
        ws.AddCase("set_a/deep/nested/case_0100", caseId: "case_0100");
        ws.AddCase("set_b/case_0050", caseId: "case_0050");
        Directory.CreateDirectory(Path.Combine(ws.Cases, "no_case_here"));

        var (code, stdout, _) = Run(Stubs, "run-all", ws.Cases, "--out", ws.Out);

        Assert.Equal(CliApp.Ok, code);
        var s = RunSummary.Load(Path.Combine(ws.Out, CaseFiles.Summary));
        Assert.Equal(new[] { "_example/case_0007", "set_a/deep/nested/case_0100", "set_b/case_0050" }, s.Cases.Select(c => c.Path));
        Assert.Equal(new[] { "case_0007", "case_0100", "case_0050" }, s.Cases.Select(c => c.CaseId));
        Assert.Contains("0/3 cases pass", stdout);
    }

    [Fact]
    public void RunAll_with_the_stubs_reports_honest_failures()
    {
        using var ws = new Workspace();
        ws.AddCase("_example/case_0007");

        Assert.Equal(CliApp.Ok, Run(Stubs, "run-all", ws.Cases, "--out", ws.Out).code);

        var s = RunSummary.Load(Path.Combine(ws.Out, CaseFiles.Summary));
        var c = Assert.Single(s.Cases);
        Assert.False(c.Pass);
        Assert.Equal(CliApp.Failed, c.RepairExit);     // stub repair: failed
        Assert.Equal(CliApp.BadInput, c.CheckExit);    // nothing to check: repaired.obj missing
        Assert.Equal(0, s.CasesPassed);
        Assert.Equal(new[] { "duplicateVertices", "farCoordinates", "smallGap", "stackedFloors" }, s.ByFault.Keys);
        Assert.All(s.ByFault.Values, t => Assert.Equal((1, 0), (t.Total, t.Passed)));

        var caseOut = Path.Combine(ws.Out, "case_0007");
        var report = RepairReport.Load(Path.Combine(caseOut, CaseFiles.Report));
        Assert.Equal(RepairStatus.Failed, report.Status);
        Assert.Equal(new Vec3(-530000, -180000, 0), report.ShiftApplied);
        Assert.Contains(StubRepairer.Warning, report.Warnings);
        Assert.False(File.Exists(Path.Combine(caseOut, CaseFiles.Repaired)));
        Assert.False(File.Exists(Path.Combine(caseOut, CaseFiles.Result)));
    }

    [Fact]
    public void RunAll_output_is_byte_identical_across_runs()
    {
        using var ws = new Workspace();
        ws.AddCase("_example/case_0007");
        ws.AddCase("x/case_0001", caseId: "case_0001", faults: Array.Empty<string>());
        var stages = new StageSet { Repairer = new ShiftOnlyRepairer(), Checker = new FixedChecker(true) };

        string[] Files() => new[]
        {
            Path.Combine(ws.Out, CaseFiles.Summary),
            Path.Combine(ws.Out, "case_0007", CaseFiles.Repaired),
            Path.Combine(ws.Out, "case_0007", CaseFiles.Report),
            Path.Combine(ws.Out, "case_0007", CaseFiles.Result),
        };

        Run(stages, "run-all", ws.Cases, "--out", ws.Out);
        var first = Files().Select(File.ReadAllBytes).ToList();
        Run(stages, "run-all", ws.Cases, "--out", ws.Out);
        var second = Files().Select(File.ReadAllBytes).ToList();

        Assert.Equal(first, second);
        Assert.All(first, b => Assert.DoesNotContain((byte)'\r', b));
    }

    [Fact]
    public void RunAll_counts_passes_per_fault_and_clean_controls()
    {
        using var ws = new Workspace();
        ws.AddCase("a/case_0007");
        ws.AddCase("b/case_0001", caseId: "case_0001", faults: Array.Empty<string>());
        ws.AddCase("c/case_0002", caseId: "case_0002", faults: new[] { "smallGap" });
        var stages = new StageSet { Repairer = new ShiftOnlyRepairer(), Checker = new FixedChecker(true) };

        Assert.Equal(CliApp.Ok, Run(stages, "run-all", ws.Cases, "--out", ws.Out).code);

        var s = RunSummary.Load(Path.Combine(ws.Out, CaseFiles.Summary));
        Assert.Equal((3, 3), (s.CasesTotal, s.CasesPassed));
        Assert.Equal((1, 1), (s.ByFault[RunSummary.CleanKey].Total, s.ByFault[RunSummary.CleanKey].Passed));
        Assert.Equal((2, 2), (s.ByFault["smallGap"].Total, s.ByFault["smallGap"].Passed));
        Assert.All(s.Cases, c => Assert.Equal((0, 0), (c.RepairExit, c.CheckExit)));
    }

    [Fact]
    public void RunAll_lists_failed_check_names()
    {
        using var ws = new Workspace();
        ws.AddCase("a/case_0007");
        var stages = new StageSet { Repairer = new ShiftOnlyRepairer(), Checker = new FixedChecker(false) };

        Assert.Equal(CliApp.Ok, Run(stages, "run-all", ws.Cases, "--out", ws.Out).code);

        var c = Assert.Single(RunSummary.Load(Path.Combine(ws.Out, CaseFiles.Summary)).Cases);
        Assert.False(c.Pass);
        Assert.Equal(CliApp.Failed, c.CheckExit);
        Assert.Equal(new[] { "watertight" }, c.FailedChecks);
    }

    [Fact]
    public void RunAll_rejects_duplicate_case_ids()
    {
        using var ws = new Workspace();
        ws.AddCase("a/case_0007");
        ws.AddCase("b/case_0007");
        var (code, _, stderr) = Run(Stubs, "run-all", ws.Cases, "--out", ws.Out);
        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains("case_0007", stderr);
        Assert.False(File.Exists(Path.Combine(ws.Out, CaseFiles.Summary)));
    }

    [Fact]
    public void RunAll_with_no_cases_or_no_folder_is_bad_input()
    {
        using var ws = new Workspace();
        Directory.CreateDirectory(Path.Combine(ws.Cases, "empty"));
        Assert.Equal(CliApp.BadInput, Run(Stubs, "run-all", ws.Cases, "--out", ws.Out).code);
        Assert.Equal(CliApp.BadInput, Run(Stubs, "run-all", Path.Combine(ws.Cases, "missing"), "--out", ws.Out).code);
    }

    [Fact]
    public void RunAll_keeps_going_past_a_bad_case()
    {
        using var ws = new Workspace();
        ws.AddCase("a/case_0007");
        var bad = ws.AddCase("b/case_0008", caseId: "case_0008");
        File.WriteAllText(Path.Combine(bad, CaseFiles.Input), "v 0 0 0\nf 1 2 3\n");
        var stages = new StageSet { Repairer = new ShiftOnlyRepairer(), Checker = new FixedChecker(true) };

        Assert.Equal(CliApp.Ok, Run(stages, "run-all", ws.Cases, "--out", ws.Out).code);

        var s = RunSummary.Load(Path.Combine(ws.Out, CaseFiles.Summary));
        Assert.Equal(new[] { true, false }, s.Cases.Select(c => c.Pass));
        Assert.Equal(CliApp.BadInput, s.Cases[1].RepairExit);
    }

    // ------------------------------------------------------------ repair

    [Fact]
    public void Repair_gives_the_solver_only_case_json_and_input_obj_and_writes_its_output()
    {
        using var ws = new Workspace();
        var dir = ws.AddCase("case_0007");
        var solver = new ShiftOnlyRepairer();

        Assert.Equal(CliApp.Ok, Run(new StageSet { Repairer = solver }, "repair", dir, "--out", ws.Out).code);

        Assert.Equal("case_0007", solver.LastRequest!.Case.CaseId);
        Assert.Equal(56, solver.LastRequest.Input.Vertices.Count);
        var repaired = ObjReader.ReadFile(Path.Combine(ws.Out, "case_0007", CaseFiles.Repaired));
        Assert.Equal(new Vec3(0, 0, 0), repaired.Vertices[0]);
        Assert.Equal(RepairStatus.Repaired, RepairReport.Load(Path.Combine(ws.Out, "case_0007", CaseFiles.Report)).Status);
    }

    [Fact]
    public void Repair_removes_stale_output_from_a_previous_run()
    {
        using var ws = new Workspace();
        var dir = ws.AddCase("case_0007");
        var caseOut = Directory.CreateDirectory(Path.Combine(ws.Out, "case_0007")).FullName;
        foreach (var f in new[] { CaseFiles.Repaired, CaseFiles.Result }) File.WriteAllText(Path.Combine(caseOut, f), "stale");

        Assert.Equal(CliApp.Failed, Run(Stubs, "repair", dir, "--out", ws.Out).code);

        Assert.False(File.Exists(Path.Combine(caseOut, CaseFiles.Repaired)));
        Assert.False(File.Exists(Path.Combine(caseOut, CaseFiles.Result)));
        Assert.True(File.Exists(Path.Combine(caseOut, CaseFiles.Report)));
    }

    [Theory]
    [InlineData("missingCaseJson")]
    [InlineData("missingInputObj")]
    [InlineData("caseJsonWithoutCaseId")]
    [InlineData("caseJsonWrongSchema")]
    [InlineData("objWithBadIndex")]
    [InlineData("objWithUnknownKeyword")]
    public void Repair_bad_input_exits_2(string problem)
    {
        using var ws = new Workspace();
        var dir = ws.AddCase("case_0007");
        var caseJson = Path.Combine(dir, CaseFiles.Case);
        var obj = Path.Combine(dir, CaseFiles.Input);
        switch (problem)
        {
            case "missingCaseJson": File.Delete(caseJson); break;
            case "missingInputObj": File.Delete(obj); break;
            case "caseJsonWithoutCaseId": Workspace.Edit(caseJson, j => j.Remove("caseId")); break;
            case "caseJsonWrongSchema": Workspace.Edit(caseJson, j => j["schemaVersion"] = "9.9"); break;
            case "objWithBadIndex": File.AppendAllText(obj, "f 1 2 999\n"); break;
            case "objWithUnknownKeyword": File.AppendAllText(obj, "curv 0 1 2\n"); break;
        }

        var (code, _, stderr) = Run(new StageSet { Repairer = new ShiftOnlyRepairer() }, "repair", dir, "--out", ws.Out);
        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains("bad input", stderr);
    }

    [Fact]
    public void Repair_that_breaks_the_output_contract_fails_and_writes_nothing()
    {
        using var ws = new Workspace();
        var dir = ws.AddCase("case_0007");
        var liar = new LyingRepairer();

        var (code, _, stderr) = Run(new StageSet { Repairer = liar }, "repair", dir, "--out", ws.Out);

        Assert.Equal(CliApp.Failed, code);
        Assert.Contains("no repaired mesh", stderr);
        Assert.False(File.Exists(Path.Combine(ws.Out, "case_0007", CaseFiles.Report)));
    }

    private sealed class LyingRepairer : ICaseRepairer
    {
        public RepairOutcome Repair(RepairRequest r) => new()
        {
            Report = new RepairReport
            {
                SchemaVersion = SchemaVersions.Current, CaseId = r.Case.CaseId, SolverVersion = "x",
                Status = RepairStatus.Repaired, ShiftApplied = Vec3.Zero, ShellCount = 1,
                Operations = Array.Empty<RepairOperation>(), Warnings = Array.Empty<string>(), Runtime_ms = 0,
            },
        };
    }

    // ------------------------------------------------------------ check

    [Fact]
    public void Check_without_repaired_obj_is_a_missing_file()
    {
        using var ws = new Workspace();
        var dir = ws.AddCase("case_0007");
        var (code, _, stderr) = Run(new StageSet { Checker = new FixedChecker(true) }, "check", dir, "--out", ws.Out);
        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains("missing file", stderr);
    }

    [Fact]
    public void Check_without_expected_json_is_a_missing_file()
    {
        using var ws = new Workspace();
        var dir = ws.AddCase("case_0007");
        File.Delete(Path.Combine(dir, CaseFiles.Expected));
        Assert.Equal(CliApp.BadInput, Run(new StageSet { Checker = new FixedChecker(true) }, "check", dir, "--out", ws.Out).code);
    }

    [Fact]
    public void Check_without_a_checker_says_not_implemented()
    {
        using var ws = new Workspace();
        var dir = ws.AddCase("case_0007");
        Run(new StageSet { Repairer = new ShiftOnlyRepairer() }, "repair", dir, "--out", ws.Out);
        var (code, _, stderr) = Run(Stubs, "check", dir, "--out", ws.Out);
        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains("not implemented", stderr);
        Assert.False(File.Exists(Path.Combine(ws.Out, "case_0007", CaseFiles.Result)));
    }

    [Theory]
    [InlineData(true, CliApp.Ok)]
    [InlineData(false, CliApp.Failed)]
    public void Check_exit_code_follows_the_result_and_result_json_is_written(bool pass, int expectedCode)
    {
        using var ws = new Workspace();
        var dir = ws.AddCase("case_0007");
        Run(new StageSet { Repairer = new ShiftOnlyRepairer() }, "repair", dir, "--out", ws.Out);
        var checker = new FixedChecker(pass);

        Assert.Equal(expectedCode, Run(new StageSet { Checker = checker }, "check", dir, "--out", ws.Out).code);

        Assert.Equal(pass, CheckResult.Load(Path.Combine(ws.Out, "case_0007", CaseFiles.Result)).Pass);
        Assert.Equal(1, checker.LastRequest!.Expected.Expected.ShellCount);
        Assert.NotNull(checker.LastRequest.ReportPath);
    }

    // ------------------------------------------------------------ generate, usage, discovery

    [Fact]
    public void Generate_without_a_generator_says_not_implemented()
    {
        using var ws = new Workspace();
        var (code, _, stderr) = Run(Stubs, "generate", ws.Cases);
        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains("not implemented", stderr);
    }

    [Fact]
    public void Generate_passes_the_folder_and_seed()
    {
        using var ws = new Workspace();
        var gen = new RecordingGenerator();
        Assert.Equal(CliApp.Ok, Run(new StageSet { Generator = gen }, "generate", ws.Cases, "--seed", "90001").code);
        Assert.Equal(90001, gen.LastRequest!.Seed);
        Assert.Equal(Path.GetFullPath(ws.Cases), gen.LastRequest.CasesDir);
        Assert.True(Directory.Exists(ws.Cases));
    }

    [Theory]
    [InlineData]
    [InlineData("repair")]
    [InlineData("bogus", "x")]
    [InlineData("repair", "x", "--seed", "1")]
    [InlineData("generate", "x", "--seed", "abc")]
    [InlineData("run-all", "x", "--out")]
    public void Bad_arguments_print_usage_and_exit_2(params string[] args)
    {
        var (code, _, stderr) = Run(Stubs, args);
        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains("usage:", stderr);
    }

    [Fact]
    public void Stage_discovery_finds_the_single_implementation_in_an_assembly()
    {
        Assert.IsType<StubRepairer>(StageSet.Find<ICaseRepairer>("m2w")); // the CLI assembly holds exactly one
        Assert.Null(StageSet.Find<ICaseChecker>("m2w"));
        Assert.Null(StageSet.Find<ICaseRepairer>("No.Such.Assembly"));
    }
}
