using System.Text.Json;
using System.Text.Json.Serialization;
using M2W.Geometry.Json;

namespace M2W.Geometry.Models;

/// <summary>Written as lower camel case: <c>repaired</c>, <c>partial</c>, <c>failed</c>.</summary>
public enum RepairStatus
{
    Repaired,
    Partial,
    Failed,
}

/// <summary><c>report.json</c>: what the solver did, written to <c>out/&lt;caseId&gt;/</c>.</summary>
public sealed class RepairReport : IValidatable
{
    [JsonPropertyName("schemaVersion")] public required string SchemaVersion { get; init; }
    [JsonPropertyName("caseId")] public required string CaseId { get; init; }
    [JsonPropertyName("solverVersion")] public required string SolverVersion { get; init; }
    [JsonPropertyName("status")] public required RepairStatus Status { get; init; }

    /// <summary>Translation added to input coordinates (normally minus the origin).</summary>
    [JsonPropertyName("shiftApplied")] public required Vec3 ShiftApplied { get; init; }

    [JsonPropertyName("shellCount")] public required int ShellCount { get; init; }
    [JsonPropertyName("operations")] public required IReadOnlyList<RepairOperation> Operations { get; init; }
    [JsonPropertyName("warnings")] public required IReadOnlyList<string> Warnings { get; init; }
    [JsonPropertyName("runtime_ms")] public required long Runtime_ms { get; init; }

    public static RepairReport Load(string path) => M2WJson.ReadFile<RepairReport>(path);

    public IReadOnlyList<string> Validate()
    {
        var e = new List<string>();
        Rules.SchemaVersion(e, SchemaVersion);
        Rules.NotEmpty(e, "caseId", CaseId);
        Rules.NotEmpty(e, "solverVersion", SolverVersion);
        if (!Enum.IsDefined(Status)) e.Add($"status {(int)Status} is not defined");
        Rules.Finite(e, "shiftApplied", ShiftApplied);
        if (ShellCount < 0) e.Add($"shellCount must be >= 0, got {ShellCount}");
        if (Operations is null) e.Add("operations is required");
        else
            for (var i = 0; i < Operations.Count; i++)
                if (Operations[i] is null || string.IsNullOrWhiteSpace(Operations[i].Op))
                    e.Add($"operations[{i}].op is required");
        if (Warnings is null) e.Add("warnings is required");
        else if (Warnings.Any(w => w is null)) e.Add("warnings must not contain null");
        if (Runtime_ms < 0) e.Add($"runtime_ms must be >= 0, got {Runtime_ms}");
        return e;
    }
}

/// <summary>
/// One entry of <c>operations</c>. <see cref="Op"/> is required; the other fields are written only when set.
/// Fields not modelled here are kept in <see cref="Extra"/>, so the solver can add detail without a schema change.
/// </summary>
public sealed class RepairOperation
{
    [JsonPropertyName("op")] public required string Op { get; init; }
    [JsonPropertyName("block")] public string? Block { get; init; }
    [JsonPropertyName("pair")] public IReadOnlyList<string>? Pair { get; init; }
    [JsonPropertyName("count")] public int? Count { get; init; }
    [JsonPropertyName("width_m")] public double? Width_m { get; init; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
