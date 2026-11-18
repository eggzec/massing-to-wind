using M2W.Geometry;
using M2W.Geometry.Stages;

namespace M2W.Checker;

/// <summary>
/// The checker stage that <c>m2w check</c> runs (found by reflection, see DECISIONS.md D-003). Reads
/// <c>repaired.obj</c> itself so an unreadable file is graded (as a failed <c>parse</c>) rather than thrown,
/// and reads <c>input.obj</c> only to locate blocks for <c>mustMerge</c> / <c>mustStaySeparate</c>.
/// <c>report.json</c> is never read.
/// </summary>
public sealed class CaseChecker : ICaseChecker
{
    public M2W.Geometry.Models.CheckResult Check(CheckRequest request)
    {
        Mesh? repaired = null;
        string? error = null;
        try
        {
            repaired = ObjReader.ReadFile(request.RepairedObjPath);
        }
        catch (Exception ex) when (ex is ObjFormatException or IOException or UnauthorizedAccessException)
        {
            error = $"repaired.obj: {ex.Message}";
        }

        (Mesh?, string?) LoadInput()
        {
            var path = Path.Combine(request.CaseDir, request.Case.Files.Input);
            try { return (ObjReader.ReadFile(path), null); }
            catch (Exception ex) when (ex is ObjFormatException or IOException or UnauthorizedAccessException)
            {
                return (null, $"{request.Case.Files.Input}: {ex.Message}");
            }
        }

        return Grader.Grade(request.Case.CaseId, repaired, error, LoadInput, request.Case, request.Expected);
    }
}
