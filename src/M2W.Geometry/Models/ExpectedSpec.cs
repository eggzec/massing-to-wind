using System.Text.Json.Serialization;
using M2W.Geometry.Json;

namespace M2W.Geometry.Models;

/// <summary><c>expected.json</c>: the answers. Only the checker (and the run-all summary) may read it; never the solver.</summary>
public sealed class ExpectedSpec : IValidatable
{
    /// <summary>The 13 fault names: the 12 of AGREEMENT.md v0.1, then <c>roofStep</c> (roof steps below and above
    /// <c>minEdge</c>, owner decision R1, D-037).</summary>
    public static readonly IReadOnlyList<string> KnownFaults = new[]
    {
        "overlap", "stackedFloors", "touchingWalls", "smallGap", "sliver", "shortEdge",
        "duplicateVertices", "openSurface", "flippedFaces", "floatingBlock", "sunkBlock", "farCoordinates",
        "roofStep",
    };

    [JsonPropertyName("schemaVersion")] public required string SchemaVersion { get; init; }
    [JsonPropertyName("caseId")] public required string CaseId { get; init; }
    [JsonPropertyName("seed")] public required long Seed { get; init; }

    /// <summary>Empty for clean control cases.</summary>
    [JsonPropertyName("faultsInjected")] public required IReadOnlyList<string> FaultsInjected { get; init; }

    [JsonPropertyName("expected")] public required ExpectedGeometry Expected { get; init; }
    [JsonPropertyName("checks")] public required ExpectedChecks Checks { get; init; }
    [JsonPropertyName("tolerances")] public required CheckTolerances Tolerances { get; init; }

    public static ExpectedSpec Load(string path) => M2WJson.ReadFile<ExpectedSpec>(path);

    public IReadOnlyList<string> Validate()
    {
        var e = new List<string>();
        Rules.SchemaVersion(e, SchemaVersion);
        Rules.NotEmpty(e, "caseId", CaseId);

        if (FaultsInjected is null) e.Add("faultsInjected is required");
        else
        {
            foreach (var f in FaultsInjected)
                if (f is null || !KnownFaults.Contains(f, StringComparer.Ordinal))
                    e.Add($"faultsInjected has unknown fault '{f}'");
            if (FaultsInjected.Distinct(StringComparer.Ordinal).Count() != FaultsInjected.Count)
                e.Add("faultsInjected has duplicates");
        }

        if (Expected is null) e.Add("expected is required");
        else Expected.Validate(e);

        if (Checks is null) e.Add("checks is required");
        else
        {
            Rules.NonNegative(e, "checks.minGap_m", Checks.MinGap_m);
            Rules.NonNegative(e, "checks.minEdge_m", Checks.MinEdge_m);
        }

        if (Tolerances is null) e.Add("tolerances is required");
        else
        {
            Rules.NonNegative(e, "tolerances.volumeRel", Tolerances.VolumeRel);
            Rules.NonNegative(e, "tolerances.areaRel", Tolerances.AreaRel);
            Rules.NonNegative(e, "tolerances.height_m", Tolerances.Height_m);
        }
        return e;
    }
}

public sealed class ExpectedGeometry
{
    [JsonPropertyName("shellCount")] public required int ShellCount { get; init; }
    [JsonPropertyName("shells")] public required IReadOnlyList<ExpectedShell> Shells { get; init; }

    /// <summary>Groups of block names (e.g. <c>["B01","B02"]</c>) that must end up in one shell.</summary>
    [JsonPropertyName("mustMerge")] public required IReadOnlyList<IReadOnlyList<string>> MustMerge { get; init; }

    /// <summary>Groups of block names that must end up in different shells.</summary>
    [JsonPropertyName("mustStaySeparate")] public required IReadOnlyList<IReadOnlyList<string>> MustStaySeparate { get; init; }

    internal void Validate(List<string> e)
    {
        if (ShellCount < 0) e.Add($"expected.shellCount must be >= 0, got {ShellCount}");
        if (Shells is null) e.Add("expected.shells is required");
        else
        {
            if (Shells.Count != ShellCount)
                e.Add($"expected.shells has {Shells.Count} entries but shellCount is {ShellCount}");
            for (var i = 0; i < Shells.Count; i++)
            {
                var s = Shells[i];
                if (s is null) { e.Add($"expected.shells[{i}] is null"); continue; }
                Rules.Positive(e, $"expected.shells[{i}].volume_m3", s.Volume_m3);
                Rules.Positive(e, $"expected.shells[{i}].footprintArea_m2", s.FootprintArea_m2);
                Rules.Positive(e, $"expected.shells[{i}].maxHeight_m", s.MaxHeight_m);
                if (s.Genus < 0) e.Add($"expected.shells[{i}].genus must be >= 0, got {s.Genus}");
                if (s.Blocks is not null && (s.Blocks.Count == 0 || s.Blocks.Any(string.IsNullOrWhiteSpace)))
                    e.Add($"expected.shells[{i}].blocks must list at least one block name");
                if (s.Anchor_m is not null && (s.Anchor_m.Count != 2 || s.Anchor_m.Any(x => !double.IsFinite(x))))
                    e.Add($"expected.shells[{i}].anchor_m must be [x, y] with two finite numbers");
            }
        }
        ValidateGroups(e, "expected.mustMerge", MustMerge);
        ValidateGroups(e, "expected.mustStaySeparate", MustStaySeparate);
    }

    private static void ValidateGroups(List<string> e, string field, IReadOnlyList<IReadOnlyList<string>>? groups)
    {
        if (groups is null) { e.Add($"{field} is required"); return; }
        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            if (g is null || g.Count < 2) e.Add($"{field}[{i}] must list at least 2 block names");
            else if (g.Any(string.IsNullOrWhiteSpace)) e.Add($"{field}[{i}] has an empty block name");
        }
    }
}

public sealed class ExpectedShell
{
    [JsonPropertyName("volume_m3")] public required double Volume_m3 { get; init; }
    [JsonPropertyName("footprintArea_m2")] public required double FootprintArea_m2 { get; init; }
    [JsonPropertyName("maxHeight_m")] public required double MaxHeight_m { get; init; }

    /// <summary>Number of through-holes (courtyards). V − E + F = 2 − 2 × genus.</summary>
    [JsonPropertyName("genus")] public required int Genus { get; init; }

    /// <summary>Optional (owner-approved 2026-11-18, D-017): the input blocks that end up in this shell,
    /// e.g. <c>["B03","B04"]</c>. Block names are the prefix of the input object names.</summary>
    [JsonPropertyName("blocks")] public IReadOnlyList<string>? Blocks { get; init; }

    /// <summary>Optional (owner-approved 2026-11-18, D-017): a plan point [x, y] in local coordinates (input minus
    /// origin) strictly inside this shell's footprint, well away from its walls. Used to match repaired shells to
    /// expected ones by position.</summary>
    [JsonPropertyName("anchor_m")] public IReadOnlyList<double>? Anchor_m { get; init; }
}

public sealed class ExpectedChecks
{
    [JsonPropertyName("watertight")] public required bool Watertight { get; init; }
    [JsonPropertyName("outwardNormals")] public required bool OutwardNormals { get; init; }
    [JsonPropertyName("noSelfIntersections")] public required bool NoSelfIntersections { get; init; }
    [JsonPropertyName("minGap_m")] public required double MinGap_m { get; init; }
    [JsonPropertyName("minEdge_m")] public required double MinEdge_m { get; init; }
}

public sealed class CheckTolerances
{
    [JsonPropertyName("volumeRel")] public required double VolumeRel { get; init; }
    [JsonPropertyName("areaRel")] public required double AreaRel { get; init; }
    [JsonPropertyName("height_m")] public required double Height_m { get; init; }
}
