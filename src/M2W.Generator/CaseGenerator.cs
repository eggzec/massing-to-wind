using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using M2W.Geometry;
using M2W.Geometry.Json;
using M2W.Geometry.Models;
using M2W.Geometry.Stages;

namespace M2W.Generator;

/// <summary>One planned case before it is built.</summary>
public sealed record PlannedCase(string CaseId, long Seed, Recipe Recipe);

/// <summary>
/// The exam: 10 clean controls, 20 single-fault cases for each of the 12 v0.1 faults, and 40 combined cases.
/// The order is shuffled with the master seed and case IDs are given in that order, so a case ID says nothing
/// about its faults and a different seed gives a different mapping.
/// </summary>
public static class CasePlan
{
    public const int CleanCount = 10;
    public const int PerFault = 20;
    public const int CombinedCount = 40;

    /// <summary>Generated IDs start here; case_0001–case_0100 are kept for hand-made references such as case_0007.</summary>
    public const int FirstId = 101;

    public static List<PlannedCase> Make(long masterSeed)
    {
        var master = new Rng((ulong)masterSeed);
        var recipes = new List<Recipe>();
        for (var i = 0; i < CleanCount; i++) recipes.Add(new Recipe(Recipe.Clean, Array.Empty<string>(), i));
        foreach (var fault in ExpectedSpec.KnownFaults)
            for (var i = 0; i < PerFault; i++) recipes.Add(new Recipe(Recipe.SingleFault, new[] { fault }, i));
        recipes.AddRange(Combinations(master.Fork("combinations")));

        // Seeds come from the recipe's position in the unshuffled plan, so they do not depend on the shuffle.
        var seeds = new HashSet<long>();
        var planned = new List<(Recipe R, long Seed)>();
        for (var i = 0; i < recipes.Count; i++)
        {
            var s = (long)(Rng.Hash((ulong)masterSeed, $"case{i}") % 2_000_000_000UL);
            while (!seeds.Add(s)) s++;
            planned.Add((recipes[i], s));
        }
        master.Fork("order").Shuffle(planned);
        return planned.Select((p, k) => new PlannedCase($"case_{FirstId + k:0000}", p.Seed, p.R)).ToList();
    }

    /// <summary>
    /// 40 combinations of 2–4 faults: consecutive windows over shuffled permutations of the 12 faults, so every
    /// fault appears about ten times and no window repeats a fault.
    /// </summary>
    private static List<Recipe> Combinations(Rng r)
    {
        var stream = new List<string>();
        var result = new List<Recipe>();
        var pos = 0;
        for (var c = 0; c < CombinedCount; c++)
        {
            var size = 2 + c % 3;
            var picked = new List<string>();
            while (picked.Count < size)
            {
                if (pos >= stream.Count)
                {
                    var perm = ExpectedSpec.KnownFaults.ToList();
                    r.Shuffle(perm);
                    stream.AddRange(perm);
                }
                var f = stream[pos++];
                if (!picked.Contains(f)) picked.Add(f);
            }
            var ordered = ExpectedSpec.KnownFaults.Where(picked.Contains).ToList();
            result.Add(new Recipe(Recipe.Combined, ordered, c));
        }
        return result;
    }
}

/// <summary>m2w's entry point for the generator role: <c>m2w generate &lt;casesDir&gt; [--seed N]</c>.</summary>
public sealed class CaseGenerator : ICaseGenerator
{
    private static readonly Regex CaseDir = new(@"^case_\d{4}$", RegexOptions.CultureInvariant);

    public int Generate(GenerateRequest request)
    {
        var dir = request.CasesDir;
        Directory.CreateDirectory(dir);

        // Remove earlier generated cases (and only those) so a smaller plan never leaves stale folders behind.
        foreach (var sub in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.Ordinal))
            if (CaseDir.IsMatch(Path.GetFileName(sub)) && File.Exists(Path.Combine(sub, CaseFiles.Case)))
                Directory.Delete(sub, recursive: true);

        var plan = CasePlan.Make(request.Seed);
        var entries = new List<IndexEntry>();
        foreach (var p in plan)
        {
            var built = CaseBuilder.Build(p.CaseId, p.Seed, p.Recipe);
            Write(dir, built);
            entries.Add(IndexEntry.From(built));
        }
        var index = new CaseIndex
        {
            SchemaVersion = SchemaVersions.Current,
            GeneratorVersion = CaseBuilder.GeneratorVersion,
            MasterSeed = request.Seed,
            CaseCount = entries.Count,
            Cases = entries,
        };
        M2WJson.WriteFile(index, Path.Combine(dir, CaseIndex.FileName));
        return entries.Count;
    }

    public static void Write(string casesDir, BuiltCase c)
    {
        var caseDir = Path.Combine(casesDir, c.CaseId);
        Directory.CreateDirectory(caseDir);
        // No fault information in the OBJ: the solver reads it.
        ObjWriter.WriteFile(c.Input, Path.Combine(caseDir, CaseFiles.Input), new[] { $"{c.CaseId} - M2W.Generator {CaseBuilder.GeneratorVersion}" });
        M2WJson.WriteFile(c.Case, Path.Combine(caseDir, CaseFiles.Case));
        M2WJson.WriteFile(c.Expected, Path.Combine(caseDir, CaseFiles.Expected));
    }
}

/// <summary><c>cases/index.json</c>: every case with its faults and a human-readable account of what was done.</summary>
public sealed class CaseIndex : IValidatable
{
    public const string FileName = "index.json";

    [JsonPropertyName("schemaVersion")] public required string SchemaVersion { get; init; }
    [JsonPropertyName("generatorVersion")] public required string GeneratorVersion { get; init; }
    [JsonPropertyName("masterSeed")] public required long MasterSeed { get; init; }
    [JsonPropertyName("caseCount")] public required int CaseCount { get; init; }
    [JsonPropertyName("cases")] public required IReadOnlyList<IndexEntry> Cases { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var e = new List<string>();
        if (Cases is null) e.Add("cases is required");
        else if (Cases.Count != CaseCount) e.Add("caseCount does not match cases");
        else if (Cases.Select(c => c.CaseId).Distinct().Count() != Cases.Count) e.Add("duplicate caseId");
        return e;
    }

    public static CaseIndex Load(string path) => M2WJson.ReadFile<CaseIndex>(path);
}

public sealed class IndexEntry
{
    [JsonPropertyName("caseId")] public required string CaseId { get; init; }
    [JsonPropertyName("seed")] public required long Seed { get; init; }

    /// <summary><c>clean</c>, <c>single</c> or <c>combined</c>.</summary>
    [JsonPropertyName("kind")] public required string Kind { get; init; }

    [JsonPropertyName("faults")] public required IReadOnlyList<string> Faults { get; init; }
    [JsonPropertyName("variant")] public required int Variant { get; init; }
    [JsonPropertyName("faceStyle")] public required string FaceStyle { get; init; }
    [JsonPropertyName("site")] public required IndexSite Site { get; init; }
    [JsonPropertyName("blocks")] public required IReadOnlyList<IndexBlock> Blocks { get; init; }
    [JsonPropertyName("shellCount")] public required int ShellCount { get; init; }
    [JsonPropertyName("notes")] public required IReadOnlyList<string> Notes { get; init; }

    public static IndexEntry From(BuiltCase c) => new()
    {
        CaseId = c.CaseId,
        Seed = c.Seed,
        Kind = c.Recipe.Kind,
        Faults = c.Expected.FaultsInjected,
        Variant = c.Recipe.Variant,
        FaceStyle = c.Style == M2W.Generator.FaceStyle.Polygons ? "polygons" : "triangles",
        Site = new IndexSite
        {
            Shape = c.Site.Shape,
            Aligned = c.Site.Aligned,
            Setback_m = c.Site.SetbackDistance,
            Polygon = c.Site.Polygon.Select(p => (IReadOnlyList<double>)new[] { Math.Round(p.X, 3), Math.Round(p.Y, 3) }).ToList(),
            FrameU = new[] { Math.Round(c.Site.Eu.X, 6), Math.Round(c.Site.Eu.Y, 6) },
        },
        Blocks = c.CleanBlocks.Select(b => new IndexBlock
        {
            Name = b.Name,
            Kind = b.Kind,
            Floors = b.FloorCount,
            Height_m = b.Height,
            Tiers = b.Tiers.Count,
            Emitted = c.FloorBlocks.Contains(b.Name) ? "floors" : "whole",
        }).ToList(),
        ShellCount = c.Expected.Expected.ShellCount,
        Notes = c.Notes,
    };
}

public sealed class IndexSite
{
    [JsonPropertyName("shape")] public required string Shape { get; init; }
    [JsonPropertyName("aligned")] public required bool Aligned { get; init; }
    [JsonPropertyName("setback_m")] public required double Setback_m { get; init; }

    /// <summary>Site polygon in local plan coordinates (before any map origin).</summary>
    [JsonPropertyName("polygon")] public required IReadOnlyList<IReadOnlyList<double>> Polygon { get; init; }

    /// <summary>Direction of the massing grid (unit vector of its u axis).</summary>
    [JsonPropertyName("frameU")] public required IReadOnlyList<double> FrameU { get; init; }
}

public sealed class IndexBlock
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("floors")] public required int Floors { get; init; }
    [JsonPropertyName("height_m")] public required double Height_m { get; init; }
    [JsonPropertyName("tiers")] public required int Tiers { get; init; }
    [JsonPropertyName("emitted")] public required string Emitted { get; init; }
}
