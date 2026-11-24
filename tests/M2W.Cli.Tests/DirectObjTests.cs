using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Geometry.Stages;
using Xunit;

namespace M2W.Cli.Tests;

/// <summary><c>m2w repair model.obj</c> (no case.json), plus help, version and argument errors (D-056, D-057).</summary>
public class DirectObjTests
{
    private static (int code, string stdout, string stderr) Run(StageSet stages, params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = new CliApp(stages, o, e).Run(args);
        return (code, o.ToString(), e.ToString());
    }

    private static string AddObj(Workspace ws, string name = "tower.obj")
    {
        Directory.CreateDirectory(ws.Cases);
        var path = Path.Combine(ws.Cases, name);
        File.Copy(Path.Combine(Workspace.Example0007, CaseFiles.Input), path);
        return path;
    }

    [Fact]
    public void An_obj_is_repaired_with_the_default_settings_and_named_after_the_file()
    {
        using var ws = new Workspace();
        var solver = new ShiftOnlyRepairer();

        var (code, stdout, _) = Run(new StageSet { Repairer = solver }, "repair", AddObj(ws), "--out", ws.Out);

        Assert.Equal(CliApp.Ok, code);
        var spec = solver.LastRequest!.Case;
        Assert.Equal("tower", spec.CaseId);
        Assert.Equal(Vec3.Zero, spec.Origin);
        Assert.Equal("tower.obj", spec.Files.Input);
        var d = new Tolerances();
        Assert.Equal((d.CellSize, d.MinGap, d.MinEdge, d.Weld, d.MinAngleDeg, d.GroundSnap),
            (spec.Tolerances.CellSize, spec.Tolerances.MinGap, spec.Tolerances.MinEdge, spec.Tolerances.Weld, spec.Tolerances.MinAngleDeg, spec.Tolerances.GroundSnap));
        Assert.True(File.Exists(Path.Combine(ws.Out, "tower", CaseFiles.Repaired)));
        Assert.Contains("repaired: 7 objects, 42 faces -> 1 shell,", stdout);
        Assert.Contains(Path.Combine(ws.Out, "tower", CaseFiles.Report), stdout);
    }

    [Fact]
    public void Options_set_the_origin_and_tolerances_before_or_after_the_file()
    {
        using var ws = new Workspace();
        var solver = new ShiftOnlyRepairer();

        var (code, _, _) = Run(new StageSet { Repairer = solver }, "repair", "--origin", "530000,-180000.5,0", "--min-gap", "0.5",
            AddObj(ws), "--min-edge", "0.2", "--ground-snap", "0.3", "--weld", "0.002", "--min-angle", "7", "--out", ws.Out);

        Assert.Equal(CliApp.Ok, code);
        var spec = solver.LastRequest!.Case;
        Assert.Equal(new Vec3(530000, -180000.5, 0), spec.Origin);
        Assert.Equal((0.5, 0.2, 0.3, 0.002, 7.0),
            (spec.Tolerances.MinGap, spec.Tolerances.MinEdge, spec.Tolerances.GroundSnap, spec.Tolerances.Weld, spec.Tolerances.MinAngleDeg));
    }

    [Theory] // how PowerShell functions pass 530000,180000,0 on
    [InlineData("530000 180000 0")]
    [InlineData("530000", "180000", "0")]
    public void An_origin_split_by_powershell_is_read(params string[] origin)
    {
        using var ws = new Workspace();
        var solver = new ShiftOnlyRepairer();
        var args = new[] { "repair", AddObj(ws), "--origin" }.Concat(origin).Concat(new[] { "--out", ws.Out }).ToArray();
        Assert.Equal(CliApp.Ok, Run(new StageSet { Repairer = solver }, args).code);
        Assert.Equal(new Vec3(530000, 180000, 0), solver.LastRequest!.Case.Origin);
    }

    [Fact]
    public void The_real_engine_gives_the_same_bytes_for_an_obj_as_for_its_case_folder()
    {
        using var ws = new Workspace();
        var obj = AddObj(ws);
        var stages = StageSet.Discover();

        Assert.Equal(CliApp.Ok, Run(stages, "repair", Workspace.Example0007, "--out", ws.Out).code);
        Assert.Equal(CliApp.Ok, Run(stages, "repair", obj, "--origin", "530000,180000,0", "--out", ws.Out).code);

        Assert.Equal(File.ReadAllBytes(Path.Combine(ws.Out, "case_0007", CaseFiles.Repaired)),
            File.ReadAllBytes(Path.Combine(ws.Out, "tower", CaseFiles.Repaired)));
    }

    [Fact]
    public void The_input_is_never_overwritten()
    {
        using var ws = new Workspace();
        var dir = Directory.CreateDirectory(Path.Combine(ws.Out, "repaired")).FullName;
        var obj = Path.Combine(dir, CaseFiles.Repaired);
        File.Copy(Path.Combine(Workspace.Example0007, CaseFiles.Input), obj);

        var (code, _, stderr) = Run(new StageSet { Repairer = new ShiftOnlyRepairer() }, "repair", obj, "--out", ws.Out);

        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains("overwrite", stderr);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Workspace.Example0007, CaseFiles.Input)), File.ReadAllBytes(obj));
    }

    [Fact]
    public void A_missing_or_wrong_file_is_bad_input()
    {
        using var ws = new Workspace();
        Directory.CreateDirectory(ws.Cases);
        var broken = Path.Combine(ws.Cases, "broken.obj");
        File.WriteAllText(broken, "v 0 0 0\nf 1 2 3\n");
        var text = Path.Combine(ws.Cases, "notes.txt");
        File.WriteAllText(text, "hello");

        foreach (var path in new[] { broken, text, Path.Combine(ws.Cases, "missing.obj") })
        {
            var (code, _, stderr) = Run(new StageSet { Repairer = new ShiftOnlyRepairer() }, "repair", path, "--out", ws.Out);
            Assert.Equal(CliApp.BadInput, code);
            Assert.Contains("bad input", stderr);
        }
        Assert.False(Directory.Exists(ws.Out));
    }

    [Fact]
    public void A_tolerance_out_of_range_is_bad_input()
    {
        using var ws = new Workspace();
        var (code, _, stderr) = Run(new StageSet { Repairer = new ShiftOnlyRepairer() }, "repair", AddObj(ws), "--min-gap", "-1", "--out", ws.Out);
        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains("tolerances.minGap", stderr);
    }

    [Fact]
    public void Model_options_are_refused_for_a_case_folder()
    {
        using var ws = new Workspace();
        var dir = ws.AddCase("case_0007");
        var (code, _, stderr) = Run(new StageSet { Repairer = new ShiftOnlyRepairer() }, "repair", dir, "--min-gap", "0.5", "--out", ws.Out);
        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains("case.json", stderr);
        Assert.False(Directory.Exists(ws.Out));
    }

    [Fact]
    public void A_folder_without_case_json_points_to_the_obj_form()
    {
        using var ws = new Workspace();
        AddObj(ws);
        var (code, _, stderr) = Run(Stubs, "repair", ws.Cases, "--out", ws.Out);
        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains("give the .obj file", stderr);
    }

    // ------------------------------------------------------------ help, version, argument errors

    private static readonly StageSet Stubs = new();

    [Theory]
    [InlineData("developer commands", "--help")]
    [InlineData("developer commands", "help")]
    [InlineData("developer commands", "-h")]
    [InlineData("--min-gap", "repair", "--help")]
    [InlineData("--min-gap", "help", "repair")]
    [InlineData("--frame", "render", "-h")]
    [InlineData("--seed", "generate", "--help")]
    [InlineData("expected.json", "check", "--help")]
    [InlineData("summary.json", "run-all", "--help")]
    public void Help_goes_to_stdout_and_exits_0(string expected, params string[] args)
    {
        var (code, stdout, stderr) = Run(Stubs, args);
        Assert.Equal(CliApp.Ok, code);
        Assert.Contains(expected, stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public void Version_is_printed_without_the_commit_hash()
    {
        var (code, stdout, _) = Run(Stubs, "--version");
        Assert.Equal(CliApp.Ok, code);
        Assert.Matches(@"^m2w \d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?\s*$", stdout);
    }

    [Theory]
    [InlineData("unknown command 'repiar'", "repiar", "x")]
    [InlineData("missing <model.obj> or <caseDir>", "repair")]
    [InlineData("--out needs a value", "repair", "x", "--out")]
    [InlineData("--out needs a value", "repair", "--out", "--min-gap", "1", "x")]
    [InlineData("--min-gap needs a number", "repair", "x", "--min-gap", "wide")]
    [InlineData("--min-gap needs a number", "repair", "x", "--min-gap", "NaN")]
    [InlineData("--origin needs X,Y,Z", "repair", "x", "--origin", "1,2")]
    [InlineData("unknown option '--seed'", "repair", "x", "--seed", "1")]
    [InlineData("unexpected argument 'y'", "repair", "x", "y")]
    [InlineData("missing <caseDir>", "check")]
    public void Argument_errors_say_what_is_wrong(string expected, params string[] args)
    {
        var (code, _, stderr) = Run(Stubs, args);
        Assert.Equal(CliApp.BadInput, code);
        Assert.Contains(expected, stderr);
        Assert.Contains("usage:", stderr);
    }
}
