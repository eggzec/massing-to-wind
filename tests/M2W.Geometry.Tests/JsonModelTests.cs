using System.Text.Json.Nodes;
using M2W.Geometry.Json;
using M2W.Geometry.Models;
using Xunit;

namespace M2W.Geometry.Tests;

public class ExampleCaseTests
{
    [Fact]
    public void Example_case_json_reads_and_validates()
    {
        var c = CaseSpec.Load(Path.Combine(RepoPaths.Example0007, CaseFiles.Case));
        Assert.Equal("0.1", c.SchemaVersion);
        Assert.Equal("case_0007", c.CaseId);
        Assert.Equal("m", c.Units);
        Assert.Equal(new Vec3(530000, 180000, 0), c.Origin);
        Assert.Equal(1.0, c.Tolerances.CellSize);
        Assert.Equal(1.0, c.Tolerances.MinGap);
        Assert.Equal(0.25, c.Tolerances.MinEdge);
        Assert.Equal(0.001, c.Tolerances.Weld);
        Assert.Equal(5.0, c.Tolerances.MinAngleDeg);
        Assert.Equal(0.5, c.Tolerances.GroundSnap);
        Assert.Equal("input.obj", c.Files.Input);
        Assert.Empty(c.Validate());
    }

    [Fact]
    public void Example_expected_json_reads_and_validates()
    {
        var x = ExpectedSpec.Load(Path.Combine(RepoPaths.Example0007, CaseFiles.Expected));
        Assert.Equal("case_0007", x.CaseId);
        Assert.Equal(4127, x.Seed);
        Assert.Equal(new[] { "stackedFloors", "smallGap", "duplicateVertices", "farCoordinates" }, x.FaultsInjected);
        Assert.Equal(1, x.Expected.ShellCount);
        var s = Assert.Single(x.Expected.Shells);
        Assert.Equal(5454.0, s.Volume_m3);
        Assert.Equal(303.0, s.FootprintArea_m2);
        Assert.Equal(18.0, s.MaxHeight_m);
        Assert.Equal(0, s.Genus);
        Assert.Equal(new[] { "B01", "B02" }, Assert.Single(x.Expected.MustMerge));
        Assert.Empty(x.Expected.MustStaySeparate);
        Assert.True(x.Checks.Watertight && x.Checks.OutwardNormals && x.Checks.NoSelfIntersections);
        Assert.Equal(1.0, x.Checks.MinGap_m);
        Assert.Equal(0.25, x.Checks.MinEdge_m);
        Assert.Equal(0.01, x.Tolerances.VolumeRel);
        Assert.Equal(0.01, x.Tolerances.AreaRel);
        Assert.Equal(0.01, x.Tolerances.Height_m);
    }

    [Fact]
    public void Example_input_file_named_in_case_json_exists_and_reads()
    {
        var c = CaseSpec.Load(Path.Combine(RepoPaths.Example0007, CaseFiles.Case));
        var m = ObjReader.ReadFile(Path.Combine(RepoPaths.Example0007, c.Files.Input));
        Assert.Equal(42, m.FaceCount);
    }

    [Fact]
    public void Example_case_files_round_trip_through_the_models()
    {
        foreach (var (file, parse) in new (string, Func<string, string>)[]
                 {
                     (CaseFiles.Case, j => M2WJson.Serialize(M2WJson.Parse<CaseSpec>(j))),
                     (CaseFiles.Expected, j => M2WJson.Serialize(M2WJson.Parse<ExpectedSpec>(j))),
                 })
        {
            var once = parse(File.ReadAllText(Path.Combine(RepoPaths.Example0007, file)));
            Assert.Equal(once, parse(once));
            Assert.True(JsonSame.Equal(
                JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Example0007, file))), JsonNode.Parse(once)), file);
        }
    }
}

public class AgreementExampleTests
{
    // Copied verbatim from AGREEMENT.md.
    private const string ReportExample = """
        {
          "schemaVersion": "0.1",
          "caseId": "case_0007",
          "solverVersion": "0.1.0",
          "status": "repaired",
          "shiftApplied": [-530000.0, -180000.0, 0.0],
          "shellCount": 1,
          "operations": [
            { "op": "weld", "count": 48 },
            { "op": "mergeStackedFloors", "block": "B01", "count": 6 },
            { "op": "closeGap", "pair": ["B01", "B02"], "width_m": 0.3 }
          ],
          "warnings": [],
          "runtime_ms": 42
        }
        """;

    private const string ResultExample = """
        {
          "schemaVersion": "0.1",
          "caseId": "case_0007",
          "pass": false,
          "checks": [
            { "name": "watertight", "pass": true, "value": 0, "limit": 0 },
            { "name": "shellCount", "pass": true, "value": 1, "limit": 1 },
            { "name": "volume_m3", "pass": false, "value": 5400.0, "limit": 5454.0 }
          ]
        }
        """;

    [Fact]
    public void Report_example_reads_and_round_trips()
    {
        var r = M2WJson.Parse<RepairReport>(ReportExample);
        Assert.Equal(RepairStatus.Repaired, r.Status);
        Assert.Equal(new Vec3(-530000, -180000, 0), r.ShiftApplied);
        Assert.Equal(3, r.Operations.Count);
        Assert.Equal(48, r.Operations[0].Count);
        Assert.Equal("B01", r.Operations[1].Block);
        Assert.Equal(new[] { "B01", "B02" }, r.Operations[2].Pair);
        Assert.Equal(0.3, r.Operations[2].Width_m);
        Assert.Equal(42, r.Runtime_ms);

        var text = M2WJson.Serialize(r);
        Assert.True(JsonSame.Equal(JsonNode.Parse(ReportExample), JsonNode.Parse(text)));
        Assert.Contains("\"status\": \"repaired\"", text);
        Assert.DoesNotContain("\"block\": null", text); // unset optional fields are left out
    }

    [Fact]
    public void Result_example_reads_and_round_trips()
    {
        var r = M2WJson.Parse<CheckResult>(ResultExample);
        Assert.False(r.Pass);
        Assert.Equal(3, r.Checks.Count);
        Assert.Equal(5454.0, r.Checks[2].Limit);
        Assert.True(JsonSame.Equal(JsonNode.Parse(ResultExample), JsonNode.Parse(M2WJson.Serialize(r))));
    }

    [Fact]
    public void Unmodelled_operation_and_check_fields_are_kept()
    {
        var report = ReportExample.Replace("{ \"op\": \"weld\", \"count\": 48 }",
            "{ \"op\": \"weld\", \"count\": 48, \"maxMove_m\": 0.0004 }");
        var text = M2WJson.Serialize(M2WJson.Parse<RepairReport>(report));
        Assert.Contains("\"maxMove_m\": 0.0004", text);

        var result = ResultExample.Replace("\"limit\": 0 }", "\"limit\": 0, \"detail\": \"none\" }");
        Assert.Contains("\"detail\": \"none\"", M2WJson.Serialize(M2WJson.Parse<CheckResult>(result)));
    }

    [Fact]
    public void Serialized_json_is_lf_only_and_ends_with_a_newline()
    {
        var text = M2WJson.Serialize(M2WJson.Parse<RepairReport>(ReportExample));
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("}\n", text);
    }

    [Fact]
    public void Negative_zero_is_written_as_zero()
    {
        var r = M2WJson.Parse<RepairReport>(ReportExample.Replace("[-530000.0, -180000.0, 0.0]", "[-530000.0, -180000.0, -0.0]"));
        Assert.Contains("-180000,\n    0\n", M2WJson.Serialize(r));
    }
}

public class JsonValidationTests
{
    private static string ExampleCaseJson => File.ReadAllText(Path.Combine(RepoPaths.Example0007, CaseFiles.Case));
    private static string ExampleExpectedJson => File.ReadAllText(Path.Combine(RepoPaths.Example0007, CaseFiles.Expected));

    private static string Without(string json, string path)
    {
        var root = JsonNode.Parse(json)!;
        var parts = path.Split('.');
        var node = root;
        foreach (var p in parts[..^1]) node = node[p]!;
        node.AsObject().Remove(parts[^1]);
        return root.ToJsonString();
    }

    private static string With(string json, string field, JsonNode? value)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root[field] = value;
        return root.ToJsonString();
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("caseId")]
    [InlineData("units")]
    [InlineData("origin")]
    [InlineData("files")]
    [InlineData("files.input")]
    public void Case_json_missing_a_required_field_is_an_error(string field)
    {
        var ex = Assert.Throws<ModelValidationException>(() => M2WJson.Parse<CaseSpec>(Without(ExampleCaseJson, field)));
        Assert.Contains(field.Split('.')[^1], ex.Message);
    }

    [Fact]
    public void Missing_tolerances_take_the_agreement_defaults()
    {
        var none = M2WJson.Parse<CaseSpec>(Without(ExampleCaseJson, "tolerances"));
        Assert.Equal(Tolerances.DefaultMinGap, none.Tolerances.MinGap);

        var some = M2WJson.Parse<CaseSpec>(Without(ExampleCaseJson.Replace("\"weld\": 0.001", "\"weld\": 0.002"), "tolerances.minEdge"));
        Assert.Equal(0.25, some.Tolerances.MinEdge);
        Assert.Equal(0.002, some.Tolerances.Weld);
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        var c = M2WJson.Parse<CaseSpec>(With(ExampleCaseJson, "futureField", new JsonObject { ["x"] = 1 }));
        Assert.Equal("case_0007", c.CaseId);
    }

    [Theory]
    [InlineData("schemaVersion", "\"0.2\"")]
    [InlineData("caseId", "\"\"")]
    [InlineData("caseId", "null")]
    [InlineData("caseId", "\"a/b\"")]
    [InlineData("units", "\"ft\"")]
    [InlineData("origin", "[1, 2]")]
    [InlineData("origin", "[1, 2, 3, 4]")]
    [InlineData("origin", "\"0,0,0\"")]
    [InlineData("files", "null")]
    public void Case_json_with_a_bad_value_is_an_error(string field, string valueJson)
    {
        Assert.Throws<ModelValidationException>(() =>
            M2WJson.Parse<CaseSpec>(With(ExampleCaseJson, field, JsonNode.Parse(valueJson))));
    }

    [Fact]
    public void Negative_tolerance_is_an_error()
    {
        Assert.Throws<ModelValidationException>(() =>
            M2WJson.Parse<CaseSpec>(ExampleCaseJson.Replace("\"minGap\": 1.0", "\"minGap\": -1.0")));
    }

    [Fact]
    public void Malformed_json_is_an_error()
    {
        Assert.Throws<ModelValidationException>(() => M2WJson.Parse<CaseSpec>("{ \"caseId\": "));
        Assert.Throws<ModelValidationException>(() => M2WJson.Parse<CaseSpec>("null"));
    }

    [Fact]
    public void Missing_file_is_reported_as_file_not_found()
    {
        Assert.Throws<FileNotFoundException>(() => CaseSpec.Load(Path.Combine(RepoPaths.Root, "no-such-case", "case.json")));
    }

    [Theory]
    [InlineData("seed")]
    [InlineData("faultsInjected")]
    [InlineData("expected")]
    [InlineData("expected.shells")]
    [InlineData("expected.mustMerge")]
    [InlineData("expected.mustStaySeparate")]
    [InlineData("checks")]
    [InlineData("checks.minEdge_m")]
    [InlineData("tolerances")]
    [InlineData("tolerances.height_m")]
    public void Expected_json_missing_a_required_field_is_an_error(string field)
    {
        Assert.Throws<ModelValidationException>(() => M2WJson.Parse<ExpectedSpec>(Without(ExampleExpectedJson, field)));
    }

    [Theory]
    [InlineData("\"stackedFloors\"", "\"stackedFloor\"")]                     // unknown fault name
    [InlineData("\"smallGap\", \"duplicateVertices\"", "\"smallGap\", \"smallGap\"")] // duplicate fault
    [InlineData("\"shellCount\": 1", "\"shellCount\": 2")]                    // shells and shellCount disagree
    [InlineData("\"genus\": 0", "\"genus\": -1")]                             // negative genus
    [InlineData("[[\"B01\", \"B02\"]]", "[[\"B01\"]]")]                       // a merge group of one
    public void Expected_json_with_a_bad_value_is_an_error(string from, string to)
    {
        Assert.Contains(from, ExampleExpectedJson);
        Assert.Throws<ModelValidationException>(() => M2WJson.Parse<ExpectedSpec>(ExampleExpectedJson.Replace(from, to)));
    }

    [Fact]
    public void Clean_control_with_no_faults_is_valid()
    {
        var x = M2WJson.Parse<ExpectedSpec>(With(ExampleExpectedJson, "faultsInjected", new JsonArray()));
        Assert.Empty(x.FaultsInjected);
    }

    [Theory]
    [InlineData("\"repaired\"", "\"done\"")]
    [InlineData("\"repaired\"", "0")]
    [InlineData("\"runtime_ms\": 42", "\"runtime_ms\": -1")]
    [InlineData("\"op\": \"weld\", ", "")]
    public void Report_with_a_bad_value_is_an_error(string from, string to)
    {
        var report = """
            {"schemaVersion":"0.1","caseId":"c","solverVersion":"1","status":"repaired","shiftApplied":[0,0,0],
             "shellCount":1,"operations":[{"op": "weld", "count":1}],"warnings":[],"runtime_ms": 42}
            """;
        Assert.NotNull(M2WJson.Parse<RepairReport>(report));
        Assert.Contains(from, report);
        Assert.Throws<ModelValidationException>(() => M2WJson.Parse<RepairReport>(report.Replace(from, to)));
    }

    [Theory]
    [InlineData("\"pass\": false, \"checks\"", "\"pass\": true, \"checks\"")]   // pass disagrees with checks
    [InlineData("{\"name\":\"a\",\"pass\":false,\"value\":1,\"limit\":0}", "")]  // pass with zero checks
    [InlineData("\"limit\":0", "\"limit\":\"x\"")]
    public void Result_with_a_bad_value_is_an_error(string from, string to)
    {
        var result = """{"schemaVersion":"0.1","caseId":"c","pass": false, "checks":[{"name":"a","pass":false,"value":1,"limit":0}]}""";
        Assert.NotNull(M2WJson.Parse<CheckResult>(result));
        Assert.Contains(from, result);
        var bad = result.Replace(from, to);
        if (to.Length == 0) bad = bad.Replace("\"pass\": false", "\"pass\": true");
        Assert.Throws<ModelValidationException>(() => M2WJson.Parse<CheckResult>(bad));
    }

    [Fact]
    public void Serialize_refuses_an_invalid_model()
    {
        var r = new CheckResult
        {
            SchemaVersion = SchemaVersions.Current,
            CaseId = "c",
            Pass = true,
            Checks = new[] { new CheckEntry { Name = "watertight", Pass = false, Value = 3, Limit = 0 } },
        };
        Assert.Throws<ModelValidationException>(() => M2WJson.Serialize(r));
    }
}

public class CaseDiscoveryTests
{
    [Fact]
    public void Finds_every_folder_with_case_json_at_any_depth_in_ordinal_order()
    {
        var root = Directory.CreateTempSubdirectory("m2w-disc-");
        try
        {
            foreach (var rel in new[] { "b/case_2", "a/deep/er/case_3", "a/case_1", "_example/case_0007", ".hidden/case_9" })
            {
                var d = Directory.CreateDirectory(Path.Combine(root.FullName, rel));
                File.WriteAllText(Path.Combine(d.FullName, "case.json"), "{}");
            }
            Directory.CreateDirectory(Path.Combine(root.FullName, "empty/folder"));
            File.WriteAllText(Path.Combine(root.FullName, "b", "Case.json.bak"), "{}");

            var found = CaseDiscovery.FindCaseDirs(root.FullName).Select(d => CaseDiscovery.RelativePath(root.FullName, d));
            Assert.Equal(new[] { "_example/case_0007", "a/case_1", "a/deep/er/case_3", "b/case_2" }, found);
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void Finds_the_example_under_the_repository_cases_folder()
    {
        var found = CaseDiscovery.FindCaseDirs(Path.Combine(RepoPaths.Root, "cases"));
        Assert.Contains(found, d => CaseDiscovery.RelativePath(Path.Combine(RepoPaths.Root, "cases"), d) == "_example/case_0007");
    }

    [Fact]
    public void Missing_root_throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => CaseDiscovery.FindCaseDirs(Path.Combine(RepoPaths.Root, "nope")));
    }
}

/// <summary>Structural JSON equality where numbers compare by value (530000.0 equals 530000).</summary>
internal static class JsonSame
{
    public static bool Equal(JsonNode? a, JsonNode? b) => (a, b) switch
    {
        (null, null) => true,
        (JsonObject x, JsonObject y) => x.Count == y.Count && x.All(kv => y.ContainsKey(kv.Key) && Equal(kv.Value, y[kv.Key])),
        (JsonArray x, JsonArray y) => x.Count == y.Count && x.Zip(y).All(t => Equal(t.First, t.Second)),
        (JsonValue x, JsonValue y) when x.GetValueKind() == System.Text.Json.JsonValueKind.Number
                                      && y.GetValueKind() == System.Text.Json.JsonValueKind.Number
            => x.GetValue<double>() == y.GetValue<double>(),
        (JsonValue x, JsonValue y) => x.ToJsonString() == y.ToJsonString(),
        _ => false,
    };
}
