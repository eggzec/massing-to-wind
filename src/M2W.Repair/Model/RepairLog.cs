using System.Text.Json;
using M2W.Geometry.Models;

namespace M2W.Repair.Model;

/// <summary>Collects the operations and warnings for <c>report.json</c>, in the order they happen.</summary>
public sealed class RepairLog
{
    public List<RepairOperation> Operations { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>Set when a step could not fully do its job; the report status becomes <c>partial</c>.</summary>
    public bool Incomplete { get; private set; }

    public void Op(string op, int? count = null, string? block = null, IReadOnlyList<string>? pair = null,
        double? width = null, IReadOnlyDictionary<string, object>? extra = null)
    {
        Dictionary<string, JsonElement>? x = null;
        if (extra is not null)
        {
            x = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var (k, v) in extra) x[k] = JsonSerializer.SerializeToElement(v);
        }
        Operations.Add(new RepairOperation { Op = op, Count = count, Block = block, Pair = pair, Width_m = width, Extra = x });
    }

    /// <summary>A warning that does not by itself make the repair incomplete.</summary>
    public void Note(string warning) => Warnings.Add(warning);

    /// <summary>A warning that means the output is not fully repaired.</summary>
    public void Fail(string warning)
    {
        Warnings.Add(warning);
        Incomplete = true;
    }
}
