using System.Text.Json.Serialization;
using M2W.Geometry.Json;

namespace M2W.Geometry.Models;

public static class SchemaVersions
{
    /// <summary>The only schema version this build reads and writes (AGREEMENT.md v0.1).</summary>
    public const string Current = "0.1";
}

/// <summary>Fixed file names inside a case folder and an output folder (AGREEMENT.md).</summary>
public static class CaseFiles
{
    public const string Case = "case.json";
    public const string Expected = "expected.json";
    public const string Input = "input.obj";
    public const string Repaired = "repaired.obj";
    public const string Report = "report.json";
    public const string Result = "result.json";
    public const string Summary = "summary.json";
}

/// <summary><c>case.json</c>: the settings the solver may read.</summary>
public sealed class CaseSpec : IValidatable
{
    [JsonPropertyName("schemaVersion")] public required string SchemaVersion { get; init; }
    [JsonPropertyName("caseId")] public required string CaseId { get; init; }
    [JsonPropertyName("units")] public required string Units { get; init; }

    /// <summary>Map origin. The solver writes output in local coordinates: input minus origin.</summary>
    [JsonPropertyName("origin")] public required Vec3 Origin { get; init; }

    /// <summary>Optional in the file; any tolerance left out takes its AGREEMENT.md default.</summary>
    [JsonPropertyName("tolerances")] public Tolerances Tolerances { get; init; } = new();

    [JsonPropertyName("files")] public required CaseFileNames Files { get; init; }

    public static CaseSpec Load(string path) => M2WJson.ReadFile<CaseSpec>(path);

    public IReadOnlyList<string> Validate()
    {
        var e = new List<string>();
        Rules.SchemaVersion(e, SchemaVersion);
        Rules.NotEmpty(e, "caseId", CaseId);
        if (CaseId is not null && CaseId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            e.Add($"caseId '{CaseId}' must be usable as a folder name");
        if (Units != "m") e.Add($"units must be \"m\", got {(Units is null ? "null" : $"\"{Units}\"")}");
        Rules.Finite(e, "origin", Origin);
        if (Tolerances is null) e.Add("tolerances must not be null");
        else Tolerances.Validate(e);
        if (Files is null) e.Add("files is required");
        else Rules.NotEmpty(e, "files.input", Files.Input);
        return e;
    }
}

/// <summary>Repair tolerances. Defaults are the AGREEMENT.md defaults; changing one needs owner approval.</summary>
public sealed class Tolerances
{
    public const double DefaultCellSize = 1.0;
    public const double DefaultMinGap = 1.0;
    public const double DefaultMinEdge = 0.25;
    public const double DefaultWeld = 0.001;
    public const double DefaultMinAngleDeg = 5.0;
    public const double DefaultGroundSnap = 0.5;

    [JsonPropertyName("cellSize")] public double CellSize { get; init; } = DefaultCellSize;
    [JsonPropertyName("minGap")] public double MinGap { get; init; } = DefaultMinGap;
    [JsonPropertyName("minEdge")] public double MinEdge { get; init; } = DefaultMinEdge;
    [JsonPropertyName("weld")] public double Weld { get; init; } = DefaultWeld;
    [JsonPropertyName("minAngleDeg")] public double MinAngleDeg { get; init; } = DefaultMinAngleDeg;
    [JsonPropertyName("groundSnap")] public double GroundSnap { get; init; } = DefaultGroundSnap;

    internal void Validate(List<string> e)
    {
        Rules.Positive(e, "tolerances.cellSize", CellSize);
        Rules.NonNegative(e, "tolerances.minGap", MinGap);
        Rules.NonNegative(e, "tolerances.minEdge", MinEdge);
        Rules.NonNegative(e, "tolerances.weld", Weld);
        if (!double.IsFinite(MinAngleDeg) || MinAngleDeg < 0 || MinAngleDeg >= 180)
            e.Add($"tolerances.minAngleDeg must be in [0, 180), got {MinAngleDeg}");
        Rules.NonNegative(e, "tolerances.groundSnap", GroundSnap);
    }
}

/// <summary>The <c>files</c> block of case.json. Paths are relative to the case folder.</summary>
public sealed class CaseFileNames
{
    [JsonPropertyName("input")] public required string Input { get; init; }
}
