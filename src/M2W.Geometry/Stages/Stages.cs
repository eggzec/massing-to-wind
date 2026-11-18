using M2W.Geometry.Models;

namespace M2W.Geometry.Stages;

// The three role entry points. The m2w command line finds the one public, non-abstract class with a public
// parameterless constructor that implements each interface in its role's assembly (ICaseRepairer in M2W.Repair,
// ICaseChecker in M2W.Checker, ICaseGenerator in M2W.Generator). Until a role adds one, m2w uses a stub.
// This lets each role plug in from its own folder without editing M2W.Cli. See DECISIONS.md D-003.

/// <summary>What the solver is given: case.json and input.obj, nothing else (AGREEMENT.md).</summary>
public sealed class RepairRequest
{
    public required CaseSpec Case { get; init; }

    /// <summary>The input as read from <c>input.obj</c>, in the file's (possibly map) coordinates.</summary>
    public required Mesh Input { get; init; }
}

/// <summary>What the solver returns. m2w writes <c>repaired.obj</c> (if any) and <c>report.json</c>.</summary>
public sealed class RepairOutcome
{
    public required RepairReport Report { get; init; }

    /// <summary>Repaired mesh in local coordinates. Required unless <see cref="RepairReport.Status"/> is failed.</summary>
    public Mesh? Repaired { get; init; }
}

public interface ICaseRepairer
{
    RepairOutcome Repair(RepairRequest request);
}

/// <summary>What the checker is given. It reads <see cref="RepairedObjPath"/> itself so it can grade parse errors.</summary>
public sealed class CheckRequest
{
    /// <summary>The case folder (case.json, expected.json, input.obj).</summary>
    public required string CaseDir { get; init; }

    public required CaseSpec Case { get; init; }
    public required ExpectedSpec Expected { get; init; }

    /// <summary><c>out/&lt;caseId&gt;/repaired.obj</c>. m2w has checked that it exists.</summary>
    public required string RepairedObjPath { get; init; }

    /// <summary><c>out/&lt;caseId&gt;/report.json</c> if present. Information only; never used to decide a check.</summary>
    public string? ReportPath { get; init; }
}

public interface ICaseChecker
{
    CheckResult Check(CheckRequest request);
}

public sealed class GenerateRequest
{
    /// <summary>Folder to write cases into (created if missing).</summary>
    public required string CasesDir { get; init; }

    public required long Seed { get; init; }
}

public interface ICaseGenerator
{
    /// <summary>Writes the cases and returns how many were written.</summary>
    int Generate(GenerateRequest request);
}
