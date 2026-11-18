using System.Text.RegularExpressions;
using M2W.Geometry;

namespace M2W.Checker;

/// <summary>
/// Finds which repaired shell each input block ended up in, by position. The input is moved to local coordinates
/// (input minus <c>origin</c>); each block's vertices and face centres then vote for the shell whose solid is
/// nearest (0 inside it). A point votes only if it is within <see cref="MaxDistance"/> of a shell and its nearest
/// shell is unambiguous, so a point on a wall two shells share cannot decide anything. The shell with the most
/// votes wins (ties: lowest shell index). A block with no votes is not found.
/// </summary>
public sealed class BlockLocator
{
    private static readonly Regex BlockName = new(@"^(B[^_]+)(?:_F\d+)?$", RegexOptions.CultureInvariant);

    /// <summary>Two distances closer than this are a tie.</summary>
    public const double AmbiguityTol = 1e-6;

    private readonly Dictionary<string, List<Vec3>> _points;
    private readonly IReadOnlyList<Shell> _shells;
    private readonly Dictionary<string, Location> _cache = new(StringComparer.Ordinal);

    public BlockLocator(Mesh input, Vec3 origin, IReadOnlyList<Shell> shells, double maxDistance)
    {
        _shells = shells;
        MaxDistance = maxDistance;
        _points = new Dictionary<string, List<Vec3>>(StringComparer.Ordinal);
        foreach (var obj in input.Objects)
        {
            var block = BlockOf(obj.Name);
            if (block is null) continue;
            if (!_points.TryGetValue(block, out var pts)) _points[block] = pts = new List<Vec3>();
            foreach (var f in obj.Faces)
            {
                var sum = Vec3.Zero;
                foreach (var i in f)
                {
                    var p = input.Vertices[i] - origin;
                    pts.Add(p);
                    sum += p;
                }
                pts.Add(sum / f.Count);
            }
        }
        foreach (var key in _points.Keys.ToList())
            _points[key] = _points[key].Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToList();
    }

    public double MaxDistance { get; }

    public IReadOnlyCollection<string> Blocks => _points.Keys;

    /// <summary><c>B01_F03</c> → <c>B01</c>; <c>B02</c> → <c>B02</c>; anything else → null.</summary>
    public static string? BlockOf(string objectName)
    {
        var m = BlockName.Match(objectName);
        return m.Success ? m.Groups[1].Value : null;
    }

    public readonly record struct Location(int Shell, int Votes, int Points)
    {
        public bool Found => Shell >= 0;
    }

    public Location Locate(string block)
    {
        if (_cache.TryGetValue(block, out var cached)) return cached;
        var loc = new Location(-1, 0, 0);
        if (_points.TryGetValue(block, out var pts) && _shells.Count > 0)
        {
            var votes = new int[_shells.Count];
            var dist = new double[_shells.Count];
            foreach (var p in pts)
            {
                for (var s = 0; s < _shells.Count; s++)
                    dist[s] = Box.Distance(_shells[s].Bounds, p) > MaxDistance ? double.PositiveInfinity : _shells[s].SolidDistance(p);
                var best = -1;
                for (var s = 0; s < _shells.Count; s++)
                    if (dist[s] <= MaxDistance && (best < 0 || dist[s] < dist[best])) best = s;
                if (best < 0) continue;
                var ambiguous = false;
                for (var s = 0; s < _shells.Count; s++)
                    if (s != best && dist[s] - dist[best] <= AmbiguityTol) ambiguous = true;
                if (!ambiguous) votes[best]++;
            }
            var winner = -1;
            for (var s = 0; s < votes.Length; s++)
                if (votes[s] > 0 && (winner < 0 || votes[s] > votes[winner])) winner = s;
            loc = new Location(winner, winner >= 0 ? votes[winner] : 0, pts.Count);
        }
        _cache[block] = loc;
        return loc;
    }
}
