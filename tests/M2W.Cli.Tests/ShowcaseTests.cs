using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using M2W.Geometry.Models;
using Xunit;

namespace M2W.Cli.Tests;

/// <summary>
/// The README's before/after images in <c>docs/images/</c> (generator, M5; D-044).
/// <para>Regenerate after <c>scripts/validate</c> (which writes <c>out/</c>):
/// <c>M2W_WRITE_SHOWCASE=1 dotnet test tests/M2W.Cli.Tests --filter Showcase</c> (PowerShell:
/// <c>$env:M2W_WRITE_SHOWCASE=1; dotnet test ...</c>). That writes the 8 SVGs with <c>m2w render</c> (each pair framed
/// by the other) and <c>showcase.json</c> from <c>cases/&lt;id&gt;/expected.json</c> and <c>out/&lt;id&gt;/result.json</c>.</para>
/// <para>Without the variable the test only checks what is committed: names, sizes, shared frames, faults. It does
/// not compare with <c>out/</c>, so a later solver change cannot turn validate red; rerun the writer instead.</para>
/// </summary>
public class ShowcaseTests
{
    public sealed record Pick(int N, string CaseId, string Caption);

    /// <summary>Visible cases only. Four clearly different repairs (D-044).</summary>
    public static readonly Pick[] Picks =
    {
        new(1, "case_0301", "Stacked floors and overlapping blocks (29 input objects, 2 missing faces) become 4 closed shells; a 0.375 m roof step (above minEdge) is kept."),
        new(2, "case_0177", "A 0.2 m gap (below minGap 1 m) is closed, so two blocks become one shell; the block farther away stays separate."),
        new(3, "case_0183", "An inside-out block with 3 missing faces, floating 0.71 m above the ground: faces turned outward, holes closed, set on the ground."),
        new(4, "case_0235", "Two roofs 0.24 m apart (below minEdge 0.25 m) are flushed to 10.84 m and joined; the taller block stays a shell of its own."),
    };

    public const long MaxSvgBytes = 300 * 1024;
    private const string WriteVariable = "M2W_WRITE_SHOWCASE";

    private static string Images => Path.Combine(Workspace.RepoRoot, "docs", "images");
    private static string Svg(int n, string side) => Path.Combine(Images, $"showcase-{n}-{side}.svg");

    [Fact]
    public void Showcase_images_and_json_are_complete()
    {
        if (Environment.GetEnvironmentVariable(WriteVariable) == "1") Write();

        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Images, "showcase.json")))!.AsArray();
        Assert.Equal(Picks.Length, json.Count);
        var allFaults = new HashSet<string>();
        for (var k = 0; k < Picks.Length; k++)
        {
            var p = Picks[k];
            var e = json[k]!.AsObject();
            Assert.Equal(p.N, (int)e["n"]!);
            Assert.Equal(p.CaseId, (string)e["caseId"]!);
            Assert.Equal(p.Caption, (string)e["caption"]!);
            Assert.True((double)e["volume_m3"]! > 0);
            Assert.True((double)e["footprintArea_m2"]! > 0);

            var caseDir = Path.Combine(Workspace.RepoRoot, "cases", p.CaseId);
            Assert.True(File.Exists(Path.Combine(caseDir, "input.obj")), $"{p.CaseId} is not a visible case");
            var faults = ExpectedSpec.Load(Path.Combine(caseDir, CaseFiles.Expected)).FaultsInjected;
            Assert.Equal(faults, e["faults"]!.AsArray().Select(x => (string)x!).ToArray());
            allFaults.UnionWith(faults);

            string before = File.ReadAllText(Svg(p.N, "before")), after = File.ReadAllText(Svg(p.N, "after"));
            foreach (var side in new[] { "before", "after" })
            {
                var bytes = File.ReadAllBytes(Svg(p.N, side));
                Assert.True(bytes.Length < MaxSvgBytes, $"{Svg(p.N, side)} is {bytes.Length} bytes");
                Assert.DoesNotContain((byte)'\r', bytes);
            }
            Assert.StartsWith("<svg ", before);
            Assert.Equal(ViewBox(before), ViewBox(after)); // rendered with each other as --frame
            Assert.Contains("colour = input object", before);
            Assert.Contains("colour = shell", after);
        }

        // The four kinds of repair the brief asks for.
        Assert.Contains("stackedFloors", allFaults);
        Assert.True(allFaults.Contains("overlap") || allFaults.Contains("touchingWalls"));
        Assert.Contains("smallGap", allFaults);
        Assert.True(allFaults.Contains("openSurface") || allFaults.Contains("flippedFaces"));
        Assert.True(allFaults.Contains("shortEdge") || allFaults.Contains("roofStep"));
    }

    private static string ViewBox(string svg) => Regex.Match(svg, "viewBox=\"([^\"]*)\"").Groups[1].Value;

    // ------------------------------------------------------------ writer

    private static void Write()
    {
        Directory.CreateDirectory(Images);
        var root = Workspace.RepoRoot;
        var entries = new JsonArray();
        foreach (var p in Picks)
        {
            var input = Path.Combine(root, "cases", p.CaseId, "input.obj");
            var repaired = Path.Combine(root, "out", p.CaseId, CaseFiles.Repaired);
            var resultPath = Path.Combine(root, "out", p.CaseId, CaseFiles.Result);
            if (!File.Exists(repaired) || !File.Exists(resultPath))
                throw new InvalidOperationException($"run scripts/validate first: {repaired} or {resultPath} is missing");

            var faults = ExpectedSpec.Load(Path.Combine(root, "cases", p.CaseId, CaseFiles.Expected)).FaultsInjected;
            var result = CheckResult.Load(resultPath);
            if (!result.Pass) throw new InvalidOperationException($"{p.CaseId} does not pass; pick a passing case");
            var shells = ShellValues(result, "volume_m3");
            var foot = ShellValues(result, "footprintArea_m2");
            var shellCount = (int)result.Checks.Single(c => c.Name == "shellCount").Value;
            if (shells.Count != shellCount || foot.Count != shellCount)
                throw new InvalidOperationException($"{p.CaseId}: result.json lists {shells.Count} volumes for {shellCount} shells");

            Render(input, Svg(p.N, "before"), repaired, $"{p.N} · {p.CaseId} · input: {string.Join(", ", faults)}");
            Render(repaired, Svg(p.N, "after"), input, $"{p.N} · {p.CaseId} · repaired: {shellCount} closed shell{(shellCount == 1 ? "" : "s")}");

            entries.Add(new JsonObject
            {
                ["n"] = p.N,
                ["caseId"] = p.CaseId,
                ["faults"] = new JsonArray(faults.Select(f => (JsonNode)f!).ToArray()),
                ["caption"] = p.Caption,
                ["before"] = $"docs/images/showcase-{p.N}-before.svg",
                ["after"] = $"docs/images/showcase-{p.N}-after.svg",
                ["shells"] = shellCount,
                ["volume_m3"] = Round(shells.Sum(s => s.Repaired)),
                ["footprintArea_m2"] = Round(foot.Sum(s => s.Repaired)),
                ["expectedVolume_m3"] = Round(shells.Sum(s => s.Expected)),
                ["expectedFootprintArea_m2"] = Round(foot.Sum(s => s.Expected)),
                ["pass"] = result.Pass,
            });
        }
        var text = entries.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
        File.WriteAllText(Path.Combine(Images, "showcase.json"), text, new UTF8Encoding(false));
    }

    private static void Render(string obj, string svg, string frame, string title)
    {
        var err = new StringWriter();
        var code = new CliApp(new StageSet(), TextWriter.Null, err).Run(new[] { "render", obj, "--out", svg, "--frame", frame, "--title", title });
        if (code != CliApp.Ok) throw new InvalidOperationException($"m2w render {obj} failed ({code}): {err}");
    }

    /// <summary>Per-shell (repaired, expected) values from a check's detail, "shell_000: 123.4 vs 123.0 (tol …); …".
    /// The check's own value and limit hold only the worst shell.</summary>
    private static List<(double Repaired, double Expected)> ShellValues(CheckResult r, string check)
    {
        var entry = r.Checks.Single(c => c.Name == check);
        var detail = entry.Extra is not null && entry.Extra.TryGetValue("detail", out var d) ? d.GetString() ?? "" : "";
        return Regex.Matches(detail, @"shell_\d+: (-?[0-9.eE+-]+) vs (-?[0-9.eE+-]+)")
            .Select(m => (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static double Round(double v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);
}
