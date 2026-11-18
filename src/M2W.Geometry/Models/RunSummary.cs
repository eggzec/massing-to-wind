using System.Text.Json.Serialization;
using M2W.Geometry.Json;

namespace M2W.Geometry.Models;

/// <summary>
/// <c>out/summary.json</c>, written by <c>m2w run-all</c>. AGREEMENT.md asks for "pass counts per fault type" but does
/// not fix the layout; this is the v0.1 layout (see DECISIONS.md D-004). Cases with no injected faults are counted
/// under <see cref="CleanKey"/>; cases whose expected.json cannot be read are counted under <see cref="UnknownKey"/>.
/// </summary>
public sealed class RunSummary : IValidatable
{
    public const string CleanKey = "clean";
    public const string UnknownKey = "unknown";

    [JsonPropertyName("schemaVersion")] public required string SchemaVersion { get; init; }
    [JsonPropertyName("casesTotal")] public required int CasesTotal { get; init; }
    [JsonPropertyName("casesPassed")] public required int CasesPassed { get; init; }

    /// <summary>Per fault name, sorted by ordinal key. A case with several faults counts once under each.</summary>
    [JsonPropertyName("byFault")] public required SortedDictionary<string, FaultTally> ByFault { get; init; }

    /// <summary>One entry per case, in discovery order.</summary>
    [JsonPropertyName("cases")] public required IReadOnlyList<CaseSummary> Cases { get; init; }

    public static RunSummary Load(string path) => M2WJson.ReadFile<RunSummary>(path);

    public IReadOnlyList<string> Validate()
    {
        var e = new List<string>();
        Rules.SchemaVersion(e, SchemaVersion);
        if (Cases is null) { e.Add("cases is required"); return e; }
        if (ByFault is null) { e.Add("byFault is required"); return e; }
        if (CasesTotal != Cases.Count) e.Add($"casesTotal is {CasesTotal} but there are {Cases.Count} cases");
        var passed = Cases.Count(c => c.Pass);
        if (CasesPassed != passed) e.Add($"casesPassed is {CasesPassed} but {passed} cases pass");
        foreach (var (k, t) in ByFault)
            if (t is null || t.Passed < 0 || t.Passed > t.Total) e.Add($"byFault.{k} is inconsistent");
        return e;
    }
}

public sealed class FaultTally
{
    [JsonPropertyName("total")] public required int Total { get; init; }
    [JsonPropertyName("passed")] public required int Passed { get; init; }
}

public sealed class CaseSummary
{
    [JsonPropertyName("caseId")] public required string CaseId { get; init; }

    /// <summary>Case folder relative to the cases root, with '/' separators.</summary>
    [JsonPropertyName("path")] public required string Path { get; init; }

    [JsonPropertyName("faults")] public required IReadOnlyList<string> Faults { get; init; }

    /// <summary>Exit code of <c>m2w repair</c>: 0 repaired, 1 failed or partial, 2 bad input.</summary>
    [JsonPropertyName("repairExit")] public required int RepairExit { get; init; }

    /// <summary>Exit code of <c>m2w check</c>: 0 all pass, 1 a check failed, 2 missing file or no checker.</summary>
    [JsonPropertyName("checkExit")] public required int CheckExit { get; init; }

    [JsonPropertyName("pass")] public required bool Pass { get; init; }

    /// <summary>Names of the checks that failed (from result.json), in result order.</summary>
    [JsonPropertyName("failedChecks")] public required IReadOnlyList<string> FailedChecks { get; init; }
}
