namespace M2W.Generator;

/// <summary>
/// One tier of a block: the floors from the previous tier's top floor up to <see cref="TopFloor"/> (exclusive count),
/// with the footprint further cut by <see cref="Clip"/>. Clips are cumulative, so upper tiers always sit inside lower
/// ones (setbacks never overhang). The first tier normally has <see cref="Rect.Everything"/>.
/// </summary>
public sealed record Tier(int TopFloor, Rect Clip);

/// <summary>
/// A building block in the grid frame of its site: a rectilinear footprint (union of rectangles) extruded floor by
/// floor, with optional setbacks (tiers). Ground at z = 0. Immutable-by-convention: the editing helpers return copies.
/// </summary>
public sealed class Block
{
    public Block(string kind, IEnumerable<Rect> rects, IEnumerable<double> floorHeights, IEnumerable<Tier>? tiers = null)
    {
        Kind = kind;
        Rects = rects.ToList();
        FloorHeights = floorHeights.ToList();
        Tiers = tiers?.ToList() ?? new List<Tier> { new(FloorHeights.Count, Rect.Everything) };
        if (Rects.Count == 0) throw new ArgumentException("A block needs a footprint.");
        if (FloorHeights.Count == 0) throw new ArgumentException("A block needs floors.");
        if (Tiers[^1].TopFloor != FloorHeights.Count) throw new ArgumentException("The last tier must reach the top floor.");
        for (var k = 1; k < Tiers.Count; k++)
            if (Tiers[k].TopFloor <= Tiers[k - 1].TopFloor) throw new ArgumentException("Tiers must go up.");
    }

    public string Name { get; set; } = "";
    public string Kind { get; }
    public List<Rect> Rects { get; }
    public List<double> FloorHeights { get; }
    public List<Tier> Tiers { get; }

    public int FloorCount => FloorHeights.Count;

    /// <summary>Height of the bottom of floor <paramref name="f"/> (f = FloorCount gives the roof).</summary>
    public double FloorZ(int f)
    {
        double z = 0;
        for (var i = 0; i < f; i++) z += FloorHeights[i];
        return Math.Round(z, 6);
    }

    public double Height => FloorZ(FloorCount);

    public Rect Bounds => Rect.Bounds(Rects);

    public int TierOfFloor(int floor)
    {
        for (var k = 0; k < Tiers.Count; k++)
            if (floor < Tiers[k].TopFloor) return k;
        throw new ArgumentOutOfRangeException(nameof(floor));
    }

    /// <summary>True if the point is strictly inside the footprint of tier <paramref name="k"/>.</summary>
    public bool InTier(int k, double u, double v)
    {
        var inRects = false;
        foreach (var r in Rects)
            if (r.Contains(u, v)) { inRects = true; break; }
        if (!inRects) return false;
        for (var i = 0; i <= k; i++)
            if (!Tiers[i].Clip.Contains(u, v)) return false;
        return true;
    }

    /// <summary>Roof height above the point, or 0 if the point is outside the footprint.</summary>
    public double TopAt(double u, double v)
    {
        for (var k = Tiers.Count - 1; k >= 0; k--)
            if (InTier(k, u, v)) return FloorZ(Tiers[k].TopFloor);
        return 0;
    }

    /// <summary>Every coordinate where the block's outline can change, along u (axis 0) or v (axis 1).</summary>
    public IEnumerable<double> Lines(int axis)
    {
        var b = Bounds;
        double lo = axis == 0 ? b.U0 : b.V0, hi = axis == 0 ? b.U1 : b.V1;
        foreach (var r in Rects)
        {
            yield return axis == 0 ? r.U0 : r.V0;
            yield return axis == 0 ? r.U1 : r.V1;
        }
        foreach (var t in Tiers)
        {
            foreach (var c in axis == 0 ? new[] { t.Clip.U0, t.Clip.U1 } : new[] { t.Clip.V0, t.Clip.V1 })
                if (c > lo && c < hi) yield return c;
        }
    }

    public Block Clone() => new(Kind, Rects, FloorHeights, Tiers) { Name = Name };

    public Block Translate(double du, double dv) =>
        new(Kind, Rects.Select(r => r.Translate(du, dv)), FloorHeights,
            Tiers.Select(t => t with { Clip = t.Clip.Translate(du, dv) })) { Name = Name };

    public Block Rotate90(int quarterTurns) =>
        new(Kind, Rects.Select(r => r.Rotate90(quarterTurns)), FloorHeights,
            Tiers.Select(t => t with { Clip = t.Clip.Rotate90(quarterTurns) })) { Name = Name };

    /// <summary>Copy with every rectangle and clip coordinate equal to <paramref name="from"/> on the given side
    /// replaced by <paramref name="to"/>. Side: 0 = U0, 1 = V0, 2 = U1, 3 = V1. Used to extend a block across a
    /// closed gap and to misalign one edge.</summary>
    public Block MoveEdge(int side, double from, double to, bool clipsToo = true)
    {
        Rect Move(Rect r) => side switch
        {
            0 when r.U0 == from => r with { U0 = to },
            1 when r.V0 == from => r with { V0 = to },
            2 when r.U1 == from => r with { U1 = to },
            3 when r.V1 == from => r with { V1 = to },
            _ => r,
        };
        return new Block(Kind, Rects.Select(Move), FloorHeights,
            Tiers.Select(t => clipsToo ? t with { Clip = Move(t.Clip) } : t)) { Name = Name };
    }

    public Block WithTiers(IEnumerable<Tier> tiers) => new(Kind, Rects, FloorHeights, tiers) { Name = Name };

    public Block WithRects(IEnumerable<Rect> rects) => new(Kind, rects, FloorHeights, Tiers) { Name = Name };

    /// <summary>Volume by direct integration over the block's own grid (exact for this rectilinear model).</summary>
    public double Volume()
    {
        var hf = HeightField.Build(new[] { this });
        return hf.TotalVolume();
    }

    public override string ToString() => $"{Name} {Kind} {FloorCount} floors {Height} m";
}
