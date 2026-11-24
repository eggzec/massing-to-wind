using System.Diagnostics;
using System.Reflection;
using M2W.Cli.Render;
using M2W.Geometry;
using M2W.Geometry.Json;
using M2W.Geometry.Models;
using M2W.Geometry.Stages;

namespace M2W.Cli;

/// <summary>
/// The m2w command line. Exit codes follow AGREEMENT.md:
/// <list type="bullet">
/// <item><c>repair</c>: 0 repaired, 1 repair failed (or partial), 2 bad input.</item>
/// <item><c>check</c>: 0 all checks pass, 1 a check failed, 2 missing file (or no checker yet).</item>
/// <item><c>run-all</c>: 0 every case was run and summary.json written (whatever the pass rate), 2 bad input.</item>
/// <item><c>generate</c>: 0 cases written, 2 bad arguments or no generator yet.</item>
/// <item><c>render</c>: 0 SVG written, 2 bad arguments or unreadable OBJ.</item>
/// </list>
/// </summary>
public sealed class CliApp
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int BadInput = 2;
    public const long DefaultSeed = 1;
    public const string DefaultOut = "out";

    private readonly StageSet _stages;
    private readonly TextWriter _out;
    private readonly TextWriter _err;

    public CliApp(StageSet stages, TextWriter stdout, TextWriter stderr)
    {
        _stages = stages;
        _out = stdout;
        _err = stderr;
    }

    public static int RunConsole(string[] args) =>
        new CliApp(StageSet.Discover(), Console.Out, Console.Error).Run(args);

    public int Run(string[] args)
    {
        if (args.Length == 0)
        {
            _err.WriteLine(CliHelp.For(null));
            return BadInput;
        }

        var cmd = args[0];
        if (cmd is "--version")
        {
            _out.WriteLine($"m2w {Version}");
            return Ok;
        }
        if (cmd is "help" || args.Any(a => a is "--help" or "-h"))
        {
            _out.WriteLine(CliHelp.For(args.FirstOrDefault(CliHelp.IsCommand)));
            return Ok;
        }
        if (!CliHelp.IsCommand(cmd))
        {
            _err.WriteLine($"m2w: unknown command '{cmd}'");
            _err.WriteLine(CliHelp.For(null));
            return BadInput;
        }

        if (cmd == "render") return Render(args);
        if (CliArgs.Parse(cmd, args, out var problem) is not { } a) return Usage(cmd, problem);

        try
        {
            return cmd switch
            {
                "generate" => Generate(a.Path, a.Seed),
                "repair" => Repair(a),
                "check" => Check(a.Path, a.Out),
                _ => RunAll(a.Path, a.Out),
            };
        }
        catch (Exception ex)
        {
            // Not hidden: printed in full and reported as a failure.
            _err.WriteLine($"m2w {cmd}: unexpected error: {ex}");
            return Failed;
        }
    }

    /// <summary>The package version, without the commit hash the SDK appends after '+'.</summary>
    private static string Version =>
        typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    private int Usage(string cmd, string problem)
    {
        _err.WriteLine($"m2w {cmd}: {problem}");
        _err.WriteLine(CliHelp.Usage(cmd));
        _err.WriteLine($"m2w {cmd} --help shows the options.");
        return BadInput;
    }

    // ---------------------------------------------------------------- render

    private int Render(string[] args)
    {
        string? obj = null, svg = null, frame = null, title = null;
        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--out" or "--frame" or "--title")
            {
                if (i + 1 >= args.Length) return Usage("render", $"{a} needs a value");
                var v = args[++i];
                if (a == "--out") svg = v; else if (a == "--frame") frame = v; else title = v;
            }
            else if (obj is null && !a.StartsWith("--", StringComparison.Ordinal)) obj = a;
            else return Usage("render", $"unexpected argument '{a}'");
        }
        if (obj is null || svg is null) return Usage("render", "needs <model.obj> and --out <picture.svg>");

        Mesh mesh;
        Mesh? frameMesh = null;
        try
        {
            mesh = ObjReader.ReadFile(obj);
            if (frame is not null) frameMesh = ObjReader.ReadFile(frame);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or ObjFormatException)
        {
            _err.WriteLine($"m2w render: bad input: {ex.Message}");
            return BadInput;
        }

        var result = SvgRenderer.Render(mesh, new RenderOptions
        {
            Title = title ?? Path.GetFileName(obj),
            Frame = frameMesh,
        });
        var dir = Path.GetDirectoryName(Path.GetFullPath(svg));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var bytes = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(result.Svg);
        File.WriteAllBytes(svg, bytes);
        _out.WriteLine($"render: wrote {svg} ({bytes.Length} bytes; {result.Style.ToString().ToLowerInvariant()} style, " +
                       $"{result.Objects} object(s), {result.Polygons} polygon(s), {result.FreeEdges} free edge(s), {result.FlippedFaces} flipped face(s))");
        return Ok;
    }

    // ---------------------------------------------------------------- generate

    private int Generate(string casesDir, long seed)
    {
        if (_stages.Generator is null)
        {
            _err.WriteLine($"m2w generate: not implemented yet: no ICaseGenerator in {StageSet.GeneratorAssembly} (milestone M2).");
            return BadInput;
        }
        Directory.CreateDirectory(casesDir);
        var n = _stages.Generator.Generate(new GenerateRequest { CasesDir = Path.GetFullPath(casesDir), Seed = seed });
        _out.WriteLine($"generate: wrote {n} cases to {casesDir} (seed {seed})");
        return Ok;
    }

    // ---------------------------------------------------------------- repair

    private int Repair(CliArgs a)
    {
        if (!File.Exists(a.Path))
        {
            if (!Directory.Exists(a.Path))
            {
                _err.WriteLine($"m2w repair: bad input: no such file or folder: {a.Path}");
                return BadInput;
            }
            if (a.HasModelOptions)
                return Usage("repair", "--origin and tolerances are for an .obj file; for a case folder, set them in its case.json");
            return RepairCase(a.Path, a.Out, announce: true);
        }

        if (!Path.GetExtension(a.Path).Equals(".obj", StringComparison.OrdinalIgnoreCase))
        {
            _err.WriteLine($"m2w repair: bad input: not an .obj file or a case folder: {a.Path}");
            return BadInput;
        }

        // The same settings a case.json would hold (AGREEMENT.md), taken from the command line instead.
        var spec = new CaseSpec
        {
            SchemaVersion = SchemaVersions.Current,
            CaseId = Path.GetFileNameWithoutExtension(a.Path),
            Units = "m",
            Origin = a.Origin ?? Vec3.Zero,
            Tolerances = a.Tolerances,
            Files = new CaseFileNames { Input = Path.GetFileName(a.Path) },
        };
        var problems = spec.Validate();
        if (problems.Count > 0)
        {
            _err.WriteLine($"m2w repair {a.Path}: bad input: {string.Join("; ", problems)}");
            return BadInput;
        }

        Mesh input;
        try
        {
            input = ObjReader.ReadFile(a.Path);
        }
        catch (ObjFormatException ex)
        {
            _err.WriteLine($"m2w repair {a.Path}: bad input: {ex.Message}");
            return BadInput;
        }
        return RepairMesh(spec, input, a.Path, a.Out, announce: true);
    }

    private int RepairCase(string caseDir, string outDir, bool announce)
    {
        CaseSpec spec;
        Mesh input;
        string inputPath;
        try
        {
            spec = CaseSpec.Load(Path.Combine(caseDir, CaseFiles.Case));
            inputPath = Path.Combine(caseDir, spec.Files.Input);
            input = ObjReader.ReadFile(inputPath);
        }
        catch (Exception ex) when (ex is ModelValidationException or FileNotFoundException or DirectoryNotFoundException or ObjFormatException)
        {
            _err.WriteLine($"m2w repair {caseDir}: bad input: {ex.Message}");
            if (announce && !File.Exists(Path.Combine(caseDir, CaseFiles.Case)))
                _err.WriteLine("to repair a model without a case.json, give the .obj file instead of its folder");
            return BadInput;
        }
        return RepairMesh(spec, input, inputPath, outDir, announce);
    }

    private int RepairMesh(CaseSpec spec, Mesh input, string inputPath, string outDir, bool announce)
    {
        var caseOut = Path.Combine(outDir, spec.CaseId);
        var repairedPath = Path.Combine(caseOut, CaseFiles.Repaired);
        var reportPath = Path.Combine(caseOut, CaseFiles.Report);
        // The output folder is cleared below, so a model that is itself an earlier output must not be in the way.
        if (Path.GetFullPath(inputPath).Equals(Path.GetFullPath(repairedPath), StringComparison.OrdinalIgnoreCase))
        {
            _err.WriteLine($"m2w repair {inputPath}: bad input: the output would overwrite the input; choose another --out");
            return BadInput;
        }

        Directory.CreateDirectory(caseOut);
        // Remove the previous run's output so a stale repaired.obj or result.json can never be graded or counted.
        foreach (var stale in new[] { CaseFiles.Repaired, CaseFiles.Report, CaseFiles.Result })
            File.Delete(Path.Combine(caseOut, stale));

        var repairer = _stages.Repairer ?? new StubRepairer();
        var outcome = repairer.Repair(new RepairRequest { Case = spec, Input = input });

        var problems = CheckOutcome(spec, outcome);
        if (problems.Count > 0)
        {
            _err.WriteLine($"m2w repair {spec.CaseId}: the solver broke the output contract: {string.Join("; ", problems)}");
            return Failed;
        }

        if (outcome.Repaired is not null) ObjWriter.WriteFile(outcome.Repaired, repairedPath);
        M2WJson.WriteFile(outcome.Report, reportPath);

        foreach (var w in outcome.Report.Warnings) _err.WriteLine($"m2w repair {spec.CaseId}: warning: {w}");
        if (announce)
        {
            var before = $"{Count(input.Objects.Count, "object")}, {Count(Faces(input), "face")}";
            var after = outcome.Repaired is null ? "no output"
                : $"{Count(outcome.Report.ShellCount, "shell")}, {Count(Faces(outcome.Repaired), "triangle")}";
            _out.WriteLine($"{outcome.Report.Status.ToString().ToLowerInvariant()}: {before} -> {after} ({outcome.Report.Runtime_ms} ms)");
            if (outcome.Repaired is not null) _out.WriteLine($"  {repairedPath}");
            _out.WriteLine($"  {reportPath}");
        }
        return outcome.Report.Status == RepairStatus.Repaired ? Ok : Failed;
    }

    private static int Faces(Mesh m) => m.Objects.Sum(o => o.Faces.Count);

    private static string Count(int n, string what) => $"{n} {what}{(n == 1 ? "" : "s")}";

    private static List<string> CheckOutcome(CaseSpec spec, RepairOutcome? outcome)
    {
        var p = new List<string>();
        if (outcome?.Report is null) { p.Add("no report"); return p; }
        p.AddRange(outcome.Report.Validate());
        if (outcome.Report.CaseId != spec.CaseId) p.Add($"report caseId '{outcome.Report.CaseId}' is not '{spec.CaseId}'");
        if (outcome.Report.Status != RepairStatus.Failed && outcome.Repaired is null)
            p.Add($"status is {outcome.Report.Status} but no repaired mesh was returned");
        if (outcome.Repaired is not null)
        {
            try { outcome.Repaired.Validate(); }
            catch (InvalidOperationException ex) { p.Add($"repaired mesh is invalid: {ex.Message}"); }
        }
        return p;
    }

    // ---------------------------------------------------------------- check

    private int Check(string caseDir, string outDir) => CheckCase(caseDir, outDir, out _);

    private int CheckCase(string caseDir, string outDir, out CheckResult? result)
    {
        result = null;
        CaseSpec spec;
        ExpectedSpec expected;
        try
        {
            spec = CaseSpec.Load(Path.Combine(caseDir, CaseFiles.Case));
            expected = ExpectedSpec.Load(Path.Combine(caseDir, CaseFiles.Expected));
        }
        catch (Exception ex) when (ex is ModelValidationException or FileNotFoundException or DirectoryNotFoundException)
        {
            _err.WriteLine($"m2w check {caseDir}: {ex.Message}");
            return BadInput;
        }

        var caseOut = Path.Combine(outDir, spec.CaseId);
        var repaired = Path.Combine(caseOut, CaseFiles.Repaired);
        var resultPath = Path.Combine(caseOut, CaseFiles.Result);
        if (File.Exists(resultPath)) File.Delete(resultPath);

        if (!File.Exists(repaired))
        {
            _err.WriteLine($"m2w check {spec.CaseId}: missing file: {repaired}");
            return BadInput;
        }
        if (_stages.Checker is null)
        {
            _err.WriteLine($"m2w check {spec.CaseId}: not implemented yet: no ICaseChecker in {StageSet.CheckerAssembly} (milestone M2).");
            return BadInput;
        }

        var report = Path.Combine(caseOut, CaseFiles.Report);
        result = _stages.Checker.Check(new CheckRequest
        {
            CaseDir = Path.GetFullPath(caseDir),
            Case = spec,
            Expected = expected,
            RepairedObjPath = Path.GetFullPath(repaired),
            ReportPath = File.Exists(report) ? Path.GetFullPath(report) : null,
        });

        if (result.CaseId != spec.CaseId)
            throw new InvalidOperationException($"checker returned caseId '{result.CaseId}' for case '{spec.CaseId}'");
        M2WJson.WriteFile(result, resultPath); // validates, including pass == all checks pass
        return result.Pass ? Ok : Failed;
    }

    // ---------------------------------------------------------------- run-all

    private int RunAll(string casesDir, string outDir)
    {
        if (!Directory.Exists(casesDir))
        {
            _err.WriteLine($"m2w run-all: cases folder not found: {casesDir}");
            return BadInput;
        }
        var root = Path.GetFullPath(casesDir);
        var dirs = CaseDiscovery.FindCaseDirs(root);
        if (dirs.Count == 0)
        {
            _err.WriteLine($"m2w run-all: no folder under {casesDir} contains {CaseFiles.Case}");
            return BadInput;
        }

        // Two folders with the same caseId would write to the same out/<caseId>/ and grade each other's output.
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirs)
        {
            string id;
            try { id = CaseSpec.Load(Path.Combine(d, CaseFiles.Case)).CaseId; }
            catch (ModelValidationException) { continue; } // reported as bad input when the case runs
            if (ids.TryGetValue(id, out var other))
            {
                _err.WriteLine($"m2w run-all: caseId '{id}' is used by both {CaseDiscovery.RelativePath(root, other)} and {CaseDiscovery.RelativePath(root, d)}");
                return BadInput;
            }
            ids[id] = d;
        }

        Directory.CreateDirectory(outDir);
        _out.WriteLine($"run-all: {dirs.Count} case(s) under {casesDir}; repair: {Describe(_stages.Repairer, "stub")}; check: {Describe(_stages.Checker, "not implemented")}");

        var watch = Stopwatch.StartNew();
        var cases = new List<CaseSummary>();
        foreach (var d in dirs)
        {
            var rel = CaseDiscovery.RelativePath(root, d);
            var repairExit = RepairCase(d, outDir, announce: false);
            var checkExit = CheckCase(d, outDir, out var result);

            string caseId;
            IReadOnlyList<string> faults;
            try { caseId = CaseSpec.Load(Path.Combine(d, CaseFiles.Case)).CaseId; }
            catch (ModelValidationException) { caseId = Path.GetFileName(d); }
            try { faults = ExpectedSpec.Load(Path.Combine(d, CaseFiles.Expected)).FaultsInjected; }
            catch (Exception ex) when (ex is ModelValidationException or FileNotFoundException) { faults = new[] { RunSummary.UnknownKey }; }

            var pass = checkExit == Ok && result is { Pass: true };
            var failed = result?.Checks.Where(c => !c.Pass).Select(c => c.Name).ToList() ?? new List<string>();
            cases.Add(new CaseSummary
            {
                CaseId = caseId,
                Path = rel,
                Faults = faults,
                RepairExit = repairExit,
                CheckExit = checkExit,
                Pass = pass,
                FailedChecks = failed,
            });
            _out.WriteLine($"  {(pass ? "PASS" : "FAIL")}  {caseId,-12} repair={repairExit} check={checkExit}  {rel}");
        }

        var byFault = new SortedDictionary<string, FaultTally>(StringComparer.Ordinal);
        foreach (var c in cases)
            foreach (var f in c.Faults.Count == 0 ? new[] { RunSummary.CleanKey } : c.Faults)
            {
                byFault.TryGetValue(f, out var t);
                byFault[f] = new FaultTally { Total = (t?.Total ?? 0) + 1, Passed = (t?.Passed ?? 0) + (c.Pass ? 1 : 0) };
            }

        var summary = new RunSummary
        {
            SchemaVersion = SchemaVersions.Current,
            CasesTotal = cases.Count,
            CasesPassed = cases.Count(c => c.Pass),
            ByFault = byFault,
            Cases = cases,
        };
        var summaryPath = Path.Combine(outDir, CaseFiles.Summary);
        M2WJson.WriteFile(summary, summaryPath);
        _out.WriteLine($"run-all: {summary.CasesPassed}/{summary.CasesTotal} cases pass; wrote {summaryPath} ({watch.ElapsedMilliseconds} ms)");
        return Ok;
    }

    private static string Describe(object? stage, string fallback) => stage?.GetType().FullName ?? fallback;
}
