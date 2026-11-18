using System.Text.Json;
using System.Text.Json.Serialization;
using M2W.Geometry.Json;

namespace M2W.Geometry.Models;

/// <summary><c>result.json</c>: the checker's grade for one case, written to <c>out/&lt;caseId&gt;/</c>.</summary>
public sealed class CheckResult : IValidatable
{
    [JsonPropertyName("schemaVersion")] public required string SchemaVersion { get; init; }
    [JsonPropertyName("caseId")] public required string CaseId { get; init; }

    /// <summary>True only if every check passes.</summary>
    [JsonPropertyName("pass")] public required bool Pass { get; init; }

    [JsonPropertyName("checks")] public required IReadOnlyList<CheckEntry> Checks { get; init; }

    public static CheckResult Load(string path) => M2WJson.ReadFile<CheckResult>(path);

    public IReadOnlyList<string> Validate()
    {
        var e = new List<string>();
        Rules.SchemaVersion(e, SchemaVersion);
        Rules.NotEmpty(e, "caseId", CaseId);
        if (Checks is null) { e.Add("checks is required"); return e; }
        for (var i = 0; i < Checks.Count; i++)
        {
            var c = Checks[i];
            if (c is null) { e.Add($"checks[{i}] is null"); continue; }
            Rules.NotEmpty(e, $"checks[{i}].name", c.Name);
            if (!double.IsFinite(c.Value)) e.Add($"checks[{i}].value must be finite");
            if (!double.IsFinite(c.Limit)) e.Add($"checks[{i}].limit must be finite");
        }
        // A case passes only if every check passes (AGREEMENT.md); zero checks cannot be a pass.
        var allPass = Checks.Count > 0 && Checks.All(c => c is not null && c.Pass);
        if (Pass != allPass) e.Add($"pass is {Pass.ToString().ToLowerInvariant()} but the checks say {allPass.ToString().ToLowerInvariant()}");
        return e;
    }
}

/// <summary>
/// One check. For <c>watertight</c>, <see cref="Value"/> is the number of free edges. Extra fields
/// (for example a <c>detail</c> string) are kept in <see cref="Extra"/>.
/// </summary>
public sealed class CheckEntry
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("pass")] public required bool Pass { get; init; }
    [JsonPropertyName("value")] public required double Value { get; init; }
    [JsonPropertyName("limit")] public required double Limit { get; init; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
