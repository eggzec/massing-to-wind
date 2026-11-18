namespace M2W.Generator;

/// <summary>How the blocks of a group relate. Decides the expected merge result.</summary>
public enum Relation
{
    Single,
    Overlap,
    Touching,

    /// <summary>Gap narrower than minGap: closed, the blocks merge.</summary>
    GapBelow,

    /// <summary>Gap wider than minGap: left open, the blocks stay separate.</summary>
    GapAbove,
}

/// <summary>
/// Blocks placed together. <see cref="Clean"/> is the designed model (what is emitted before faults);
/// <see cref="Expected"/> is the corrected model (differs only where a gap below minGap is closed).
/// </summary>
public sealed class Group
{
    public Group(Relation relation, IEnumerable<Block> clean, IEnumerable<Block>? expected = null)
    {
        Relation = relation;
        Clean = clean.ToList();
        Expected = (expected ?? Clean).ToList();
    }

    public Relation Relation { get; }
    public List<Block> Clean { get; private set; }
    public List<Block> Expected { get; private set; }
    public double Gap { get; init; }
    public string Variant { get; init; } = "";

    public Rect Bounds => Rect.Bounds(Expected.SelectMany(b => b.Rects));

    public void Transform(Func<Block, Block> f)
    {
        Clean = Clean.Select(f).ToList();
        Expected = Expected.Select(f).ToList();
    }

    /// <summary>Moves the group so its bounding box starts at (0, 0).</summary>
    public void Normalise()
    {
        var b = Bounds;
        Transform(x => x.Translate(-b.U0, -b.V0));
    }
}

/// <summary>Builds groups of blocks with a deliberate relation (overlap, touching walls, gaps either side of minGap).</summary>
public static class Layouts
{
    public static Group Single(Rng r, DesignRules d, string? kind = null)
    {
        var b = kind is null ? Shapes.Random(r, d) : Shapes.Make(kind, r, d);
        var g = new Group(Relation.Single, new[] { b }) { Variant = b.Kind };
        var quarter = r.Int(0, 4);
        g.Transform(x => x.Rotate90(quarter));
        g.Normalise();
        return g;
    }

    /// <summary>Snaps a coordinate to a reference if it is closer than the minimum feature size (flush instead of a
    /// tiny step), so overlapping blocks do not create edges shorter than a real design would have.</summary>
    private static double Snap(double x, double min, params double[] refs)
    {
        foreach (var c in refs)
            if (Math.Abs(x - c) < min) return c;
        return x;
    }

    /// <summary>Two blocks that interpenetrate: tower through a podium, crossing bars, or a corner overlap.</summary>
    public static Group Overlap(Rng r, DesignRules d)
    {
        var mf = d.MinFeature;
        var variant = r.Pick(new[] { "towerThroughPodium", "crossingBars", "cornerOverlap" });
        Block a, b;
        switch (variant)
        {
            case "towerThroughPodium":
            {
                var w = r.Step(30, 60, 0.1);
                var h = r.Step(22, 40, 0.1);
                a = new Block("bar", new[] { new Rect(0, 0, w, h) }, Shapes.Floors(r, r.IntIn(2, 5)));
                var tw = r.Step(15, Math.Min(26, w - mf), 0.1);
                var td = r.Step(15, Math.Min(24, h), 0.1);
                // Anywhere from fully inside to half outside, on a 0.1 m grid.
                var u0 = r.Step(-tw / 2, w - tw / 2, 0.1);
                var v0 = r.Step(-td / 3, h - td * 2 / 3, 0.1);
                u0 = Snap(u0, mf, 0, Math.Round(w - tw, 6));
                v0 = Snap(v0, mf, 0, Math.Round(h - td, 6));
                b = new Block("tower", new[] { new Rect(u0, v0, Math.Round(u0 + tw, 6), Math.Round(v0 + td, 6)) },
                    Shapes.Floors(r, r.IntIn(10, 28)));
                break;
            }
            case "crossingBars":
            {
                var len = r.Step(30, 70, 0.1);
                var dep = r.Step(10, 16, 0.1);
                a = new Block("bar", new[] { new Rect(0, 0, len, dep) }, Shapes.Floors(r, r.IntIn(3, 9)));
                var bw = r.Step(10, 16, 0.1);
                var u0 = r.Step(mf, len - bw - mf, 0.1);
                var below = r.Chance(0.6) ? r.Step(mf, 20, 0.1) : 0;
                var above = r.Step(mf, 24, 0.1);
                b = new Block("bar", new[] { new Rect(u0, Math.Round(-below, 6), Math.Round(u0 + bw, 6), Math.Round(dep + above, 6)) },
                    Shapes.Floors(r, r.IntIn(3, 12)));
                b = Shapes.MaybeSetbacks(b, r, d);
                break;
            }
            default:
            {
                a = r.Chance(0.5) ? Shapes.Bar(r, d) : Shapes.Tower(r, d);
                var ab = a.Bounds;
                var bw = r.Step(12, 30, 0.1);
                var bd = r.Step(10, 24, 0.1);
                var ou = r.Step(mf, Math.Min(bw, ab.Width) - mf, 0.1);
                var ov = r.Step(mf, Math.Min(bd, ab.Depth) - mf, 0.1);
                var u0 = Math.Round(ab.U1 - ou, 6);
                var v0 = Math.Round(ab.V1 - ov, 6);
                b = new Block("bar", new[] { new Rect(u0, v0, Math.Round(u0 + bw, 6), Math.Round(v0 + bd, 6)) },
                    Shapes.Floors(r, r.IntIn(3, 14)));
                break;
            }
        }
        var g = new Group(Relation.Overlap, r.Chance(0.5) ? new[] { a, b } : new[] { b, a }) { Variant = variant };
        var quarter = r.Int(0, 4);
        g.Transform(x => x.Rotate90(quarter));
        g.Normalise();
        return g;
    }

    /// <summary>
    /// The east-facing straight wall segments of a block at its bounding box edge: the v-ranges of rectangles whose
    /// east edge is the block's east edge and that no tier cuts back (so the wall is full height there).
    /// </summary>
    private static List<(double V0, double V1)> EastFaces(Block a)
    {
        var u1 = a.Bounds.U1;
        var list = new List<(double, double)>();
        foreach (var rect in a.Rects)
            if (rect.U1 == u1) list.Add((rect.V0, rect.V1));
        return list;
    }

    /// <summary>A block whose east side is one straight wall: bar, tower or L (east wall of the bar).</summary>
    private static Block FacingBlock(Rng r, DesignRules d, bool allowSetbacks)
    {
        var kind = r.Pick(new[] { "bar", "bar", "tower", "L" });
        var a = Shapes.Make(kind, r, d);
        if (!allowSetbacks) a = a.WithTiers(new[] { new Tier(a.FloorCount, Rect.Everything) });
        return a;
    }

    /// <summary>Blocks built wall to wall (shared, coincident wall planes): a pair, or a terrace of three.</summary>
    public static Group Touching(Rng r, DesignRules d)
    {
        var mf = d.MinFeature;
        var terrace = r.Chance(0.3);
        var a = FacingBlock(r, d, allowSetbacks: true);
        var blocks = new List<Block> { a };
        var count = terrace ? 3 : 2;
        var sameHeight = r.Chance(0.35);
        for (var k = 1; k < count; k++)
        {
            var prev = blocks[^1];
            var faces = EastFaces(prev);
            var (f0, f1) = r.Pick(faces);
            var depth = r.Step(10, 24, 0.1);
            var width = r.Step(12, 36, 0.1);
            var v0 = r.Step(f0 - depth + mf, f1 - mf, 0.1);
            v0 = Snap(v0, mf, f0, Math.Round(f1 - depth, 6));
            if (Math.Abs(v0 + depth - f0) < mf || Math.Abs(v0 + depth - f1) < mf) v0 = f0;
            var u0 = prev.Bounds.U1;
            var floors = sameHeight ? prev.FloorHeights : Shapes.Floors(r, r.IntIn(3, 12));
            var b = new Block("bar", new[] { new Rect(u0, v0, Math.Round(u0 + width, 6), Math.Round(v0 + depth, 6)) }, floors);
            if (!sameHeight) b = Shapes.MaybeSetbacks(b, r, d);
            blocks.Add(b);
        }
        var g = new Group(Relation.Touching, blocks) { Variant = terrace ? "terrace" : "pair" };
        var quarter = r.Int(0, 4);
        g.Transform(x => x.Rotate90(quarter));
        g.Normalise();
        return g;
    }

    /// <summary>
    /// Two blocks with parallel facing walls a gap apart. Below minGap the gap is closed in the expected model by
    /// extending the smaller block (B) up to the larger one (A); to keep that answer unambiguous B's facing wall lies
    /// within A's and is no taller. Above minGap the blocks stay separate.
    /// </summary>
    public static Group Gap(Rng r, DesignRules d, double gap, bool below)
    {
        var mf = d.MinFeature;
        for (var attempt = 0; ; attempt++)
        {
            var a = FacingBlock(r, d, allowSetbacks: !below);
            var faces = EastFaces(a);
            var (f0, f1) = r.Pick(faces);
            var span = f1 - f0;
            var u0 = Math.Round(a.Bounds.U1 + gap, 6);
            Block b;
            if (below)
            {
                var depth = r.Chance(0.4) ? span : r.Step(Math.Min(10, span), span, 0.1);
                var v0 = Math.Round(f0 + Shapes.Offset(r, Math.Round(span - depth, 6), mf), 6);
                var width = r.Step(10, 30, 0.1);
                // A's wall height along the face (A has no setbacks here, so it is A's height).
                var maxFloors = 0;
                var floors = Shapes.Floors(r, r.IntIn(2, 12));
                double z = 0;
                foreach (var h in floors)
                {
                    if (Math.Round(z + h, 6) > a.Height + 1e-9) break;
                    z = Math.Round(z + h, 6);
                    maxFloors++;
                }
                if (maxFloors < 2)
                {
                    if (attempt > 50) throw new InvalidOperationException("Could not build a gap pair.");
                    continue;
                }
                b = new Block("bar", new[] { new Rect(u0, v0, Math.Round(u0 + width, 6), Math.Round(v0 + depth, 6)) },
                    floors.Take(maxFloors));
                b = Shapes.MaybeSetbacks(b, r, d);
                var expectedB = b.MoveEdge(0, u0, a.Bounds.U1);
                var g = new Group(Relation.GapBelow, new[] { a, b }, new[] { a, expectedB }) { Gap = gap, Variant = "below" };
                var quarter = r.Int(0, 4);
                g.Transform(x => x.Rotate90(quarter));
                g.Normalise();
                return g;
            }
            else
            {
                var depth = r.Step(10, 24, 0.1);
                var v0 = r.Step(f0 - depth + mf, f1 - mf, 0.1);
                var width = r.Step(10, 30, 0.1);
                b = new Block("bar", new[] { new Rect(u0, v0, Math.Round(u0 + width, 6), Math.Round(v0 + depth, 6)) },
                    Shapes.Floors(r, r.IntIn(2, 12)));
                b = Shapes.MaybeSetbacks(b, r, d);
                var g = new Group(Relation.GapAbove, new[] { a, b }) { Gap = gap, Variant = "above" };
                var quarter = r.Int(0, 4);
                g.Transform(x => x.Rotate90(quarter));
                g.Normalise();
                return g;
            }
        }
    }
}
