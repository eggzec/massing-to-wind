using M2W.Geometry.Json;
using M2W.Geometry.Models;
using Xunit;

namespace M2W.Geometry.Tests;

/// <summary>Optional expected.shells[] fields <c>blocks</c> and <c>anchor_m</c> (owner-approved 2026-11-18, DECISIONS D-017).</summary>
public class ExpectedShellFieldsTests
{
    private static string Doc(string shellExtra) => $$"""
        {
          "schemaVersion": "0.1", "caseId": "case_x", "seed": 1, "faultsInjected": [],
          "expected": {
            "shellCount": 1,
            "shells": [ { "volume_m3": 10, "footprintArea_m2": 5, "maxHeight_m": 2, "genus": 0{{shellExtra}} } ],
            "mustMerge": [], "mustStaySeparate": []
          },
          "checks": { "watertight": true, "outwardNormals": true, "noSelfIntersections": true, "minGap_m": 1, "minEdge_m": 0.25 },
          "tolerances": { "volumeRel": 0.01, "areaRel": 0.01, "height_m": 0.01 }
        }
        """;

    [Fact]
    public void Fields_are_optional()
    {
        var s = Assert.Single(M2WJson.Parse<ExpectedSpec>(Doc("")).Expected.Shells);
        Assert.Null(s.Blocks);
        Assert.Null(s.Anchor_m);
        Assert.Null(Assert.Single(ExpectedSpec.Load(Path.Combine(RepoPaths.Example0007, CaseFiles.Expected)).Expected.Shells).Blocks);
    }

    [Fact]
    public void Fields_read_and_round_trip()
    {
        var x = M2WJson.Parse<ExpectedSpec>(Doc(""", "blocks": ["B01", "B02"], "anchor_m": [12.5, -3.25]"""));
        var s = Assert.Single(x.Expected.Shells);
        Assert.Equal(new[] { "B01", "B02" }, s.Blocks);
        Assert.Equal(new[] { 12.5, -3.25 }, s.Anchor_m);
        var again = Assert.Single(M2WJson.Parse<ExpectedSpec>(M2WJson.Serialize(x)).Expected.Shells);
        Assert.Equal(s.Blocks, again.Blocks);
        Assert.Equal(s.Anchor_m, again.Anchor_m);
        Assert.DoesNotContain("anchor_m", M2WJson.Serialize(M2WJson.Parse<ExpectedSpec>(Doc("")))); // null is not written
    }

    [Theory]
    [InlineData(""", "blocks": []""")]
    [InlineData(""", "blocks": [""]""")]
    [InlineData(""", "anchor_m": [1]""")]
    [InlineData(""", "anchor_m": [1, 2, 3]""")]
    public void Bad_values_are_rejected(string extra)
    {
        Assert.Throws<ModelValidationException>(() => M2WJson.Parse<ExpectedSpec>(Doc(extra)));
    }
}
