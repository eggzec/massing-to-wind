using M2W.Geometry.Models;
using M2W.Geometry.Stages;

namespace M2W.Cli;

/// <summary>
/// Used until M2W.Repair provides an <see cref="ICaseRepairer"/> (milestone M3). It repairs nothing and says so:
/// status <c>failed</c>, no repaired.obj, one warning. It never pretends to succeed.
/// </summary>
public sealed class StubRepairer : ICaseRepairer
{
    public const string Version = "0.0.0-stub";
    public const string Warning = "solver not implemented: no ICaseRepairer in M2W.Repair yet (milestone M3); nothing was repaired";

    public RepairOutcome Repair(RepairRequest request) => new()
    {
        Report = new RepairReport
        {
            SchemaVersion = SchemaVersions.Current,
            CaseId = request.Case.CaseId,
            SolverVersion = Version,
            Status = RepairStatus.Failed,
            ShiftApplied = -request.Case.Origin,
            ShellCount = 0,
            Operations = Array.Empty<RepairOperation>(),
            Warnings = new[] { Warning },
            Runtime_ms = 0,
        },
        Repaired = null,
    };
}
