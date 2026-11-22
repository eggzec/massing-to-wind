using System.Diagnostics;
using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Geometry.Stages;
using M2W.Repair.Model;
using M2W.Repair.Steps;

namespace M2W.Repair;

/// <summary>
/// The solver entry point that <c>m2w repair</c> finds (the only public <see cref="ICaseRepairer"/> in this
/// assembly). It reads only the request (case.json settings and input.obj) and runs the steps in
/// <see cref="RepairPipeline"/>.
/// </summary>
public sealed class MassingRepairer : ICaseRepairer
{
    public const string Version = "0.7.1";

    private readonly RepairOptions _options;

    public MassingRepairer() : this(RepairOptions.Default) { }

    /// <summary>A repairer with internal switches (tests, D-048); each instance has its own.</summary>
    internal MassingRepairer(RepairOptions options) { _options = options; }

    public RepairOutcome Repair(RepairRequest request)
    {
        var sw = Stopwatch.StartNew();
        var log = new RepairLog();
        var shift = -request.Case.Origin;
        List<OutputShell> shells;
        try
        {
            shells = RepairPipeline.Run(request.Input, shift, request.Case.Tolerances, log, _options);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Not hidden: the report says the repair failed and why.
            log.Fail($"repair failed: {ex.GetType().Name}: {ex.Message}");
            shells = new List<OutputShell>();
        }

        var status = shells.Count == 0 ? RepairStatus.Failed
            : log.Incomplete ? RepairStatus.Partial
            : RepairStatus.Repaired;
        if (shells.Count == 0 && !log.Incomplete) log.Fail("no closed shell could be built from the input");

        return new RepairOutcome
        {
            Report = new RepairReport
            {
                SchemaVersion = SchemaVersions.Current,
                CaseId = request.Case.CaseId,
                SolverVersion = Version,
                Status = status,
                ShiftApplied = shift,
                ShellCount = shells.Count,
                Operations = log.Operations,
                Warnings = log.Warnings,
                Runtime_ms = sw.ElapsedMilliseconds,
            },
            Repaired = shells.Count == 0 ? null : OutputShell.ToMesh(shells),
        };
    }
}

/// <summary>One closed shell ready to be written: its own vertices (local coordinates) and triangles.</summary>
public sealed class OutputShell
{
    public required List<Vec3> Vertices { get; init; }
    public required List<Tri> Triangles { get; init; }

    /// <summary>Input blocks that ended up in this shell (for the report).</summary>
    public IReadOnlyList<string> Blocks { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Builds the output mesh: one <c>o shell_NNN</c> per shell. Coordinates are rounded to the 6 decimals the OBJ
    /// writer uses, so the file holds exactly the geometry the solver checked.
    /// </summary>
    public static Mesh ToMesh(IReadOnlyList<OutputShell> shells)
    {
        var mesh = new Mesh();
        for (var s = 0; s < shells.Count; s++)
        {
            var sh = shells[s];
            var baseIndex = mesh.Vertices.Count;
            foreach (var v in sh.Vertices) mesh.Vertices.Add(Round6(v));
            var o = mesh.AddObject($"shell_{s:000}");
            foreach (var t in sh.Triangles)
                o.Faces.Add(new Face(baseIndex + t.A, baseIndex + t.B, baseIndex + t.C));
        }
        return mesh;
    }

    public static Vec3 Round6(Vec3 v) => new(R(v.X), R(v.Y), R(v.Z));

    public static double R(double x)
    {
        var r = Math.Round(x, 6, MidpointRounding.ToEven);
        return r == 0 ? 0 : r; // no negative zero
    }
}
