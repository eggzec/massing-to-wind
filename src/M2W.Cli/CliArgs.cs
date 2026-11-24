using System.Globalization;
using M2W.Geometry;
using M2W.Geometry.Models;

namespace M2W.Cli;

/// <summary>The arguments of generate, repair, check and run-all. Options may come before or after the path.</summary>
internal sealed class CliArgs
{
    /// <summary>The path argument: a case folder, a cases folder or (repair only) an OBJ file.</summary>
    public string Path { get; private set; } = "";
    public string Out { get; private set; } = CliApp.DefaultOut;
    public long Seed { get; private set; } = CliApp.DefaultSeed;
    public Vec3? Origin { get; private set; }

    /// <summary>Tolerance options as typed, e.g. <c>--min-gap</c> → 0.5. Options left out keep their default.</summary>
    private readonly Dictionary<string, double> _tolerances = new(StringComparer.Ordinal);

    private static readonly string[] ToleranceOptions = { "--min-gap", "--min-edge", "--ground-snap", "--weld", "--min-angle" };

    /// <summary>True if an option was given that a case folder already sets in its case.json.</summary>
    public bool HasModelOptions => Origin is not null || _tolerances.Count > 0;

    public Tolerances Tolerances => new()
    {
        MinGap = Get("--min-gap", Tolerances.DefaultMinGap),
        MinEdge = Get("--min-edge", Tolerances.DefaultMinEdge),
        GroundSnap = Get("--ground-snap", Tolerances.DefaultGroundSnap),
        Weld = Get("--weld", Tolerances.DefaultWeld),
        MinAngleDeg = Get("--min-angle", Tolerances.DefaultMinAngleDeg),
    };

    private double Get(string option, double fallback) => _tolerances.TryGetValue(option, out var v) ? v : fallback;

    /// <summary>Parses everything after the command name. Returns null and a message if something is wrong.</summary>
    public static CliArgs? Parse(string command, string[] args, out string problem)
    {
        problem = "";
        var r = new CliArgs();
        string? path = null;
        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                if (path is not null) { problem = $"unexpected argument '{a}'"; return null; }
                path = a;
                continue;
            }
            if (!Accepts(command, a)) { problem = $"unknown option '{a}'"; return null; }
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) { problem = $"{a} needs a value"; return null; }
            var value = args[++i];
            switch (a)
            {
                case "--out":
                    r.Out = value;
                    break;
                case "--seed":
                    if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seed))
                    {
                        problem = $"--seed needs an integer, got '{value}'";
                        return null;
                    }
                    r.Seed = seed;
                    break;
                case "--origin":
                    // A PowerShell function or alias passes 1,2,3 on as separate arguments: take the rest of the triple.
                    var text = value;
                    for (var n = 1; n < 3 && IsNumber(value) && i + 1 < args.Length && IsNumber(args[i + 1]); n++) text += "," + args[++i];
                    if (ParseOrigin(text) is not { } origin) { problem = $"--origin needs X,Y,Z in metres, got '{text}'"; return null; }
                    r.Origin = origin;
                    break;
                default:
                    if (!TryNumber(value, out var number)) { problem = $"{a} needs a number, got '{value}'"; return null; }
                    r._tolerances[a] = number;
                    break;
            }
        }
        if (path is null) { problem = $"missing {CliHelp.PathName(command)}"; return null; }
        r.Path = path;
        return r;
    }

    private static bool Accepts(string command, string option) => command switch
    {
        "generate" => option is "--seed",
        "repair" => option is "--out" or "--origin" || ToleranceOptions.Contains(option),
        _ => option is "--out",
    };

    private static Vec3? ParseOrigin(string text)
    {
        var parts = text.Split(',', ' ');
        if (parts.Length != 3) return null;
        var v = new double[3];
        for (var i = 0; i < 3; i++)
            if (!TryNumber(parts[i], out v[i])) return null;
        return new Vec3(v[0], v[1], v[2]);
    }

    private static bool IsNumber(string s) => TryNumber(s, out _);

    private static bool TryNumber(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
