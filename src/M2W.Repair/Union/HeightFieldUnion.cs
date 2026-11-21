using M2W.Geometry;
using M2W.Repair.Model;
using M2W.Repair.Planar;
using M2W.Repair.Steps;

namespace M2W.Repair.Union;

/// <summary>
/// Rebuilds the massing as a 2.5D height field: the union of all closed solids, filled down to the ground, as one
/// closed shell per connected footprint. This merges stacked floors, overlapping blocks and blocks touching wall to
/// wall in one pass.
///
/// How: the horizontal faces of each solid at each height level are reduced to that level's boundary edges (XOR of
/// triangle edges). All boundary edges of all solids are planarised together; each edge keeps tags (solid, level).
/// Crossing an edge toggles "inside level L of solid s" for each of its tags, so a breadth-first walk from the outside
/// labels every face of the plan with, per solid, which levels lie below it. A solid is solid between alternate
/// toggles; the face height is the highest roof of any solid over it. Edges with equal height on both sides are
/// dropped, collinear chains merged, and shells built from the remaining plan: a ground face, a roof per face and
/// walls on every edge where the height changes. Wall columns are split at every height used around their vertex,
/// so all triangles meet edge to edge.
/// </summary>
public sealed class HeightFieldUnion
{
    public sealed class Result
    {
        public List<OutputShell> Shells { get; } = new();
        public List<string> Problems { get; } = new();

        /// <summary>Roof steps lower than minEdge that were made flush: (from height, to height, plan faces raised).</summary>
        public List<(double from, double to, int faces)> FlushedSteps { get; } = new();

        /// <summary>For each solid (by input order), the shell it ended up in (-1 if none).</summary>
        public int[] SolidShell { get; init; } = Array.Empty<int>();

        /// <summary>Gaps below minGap that were bridged: (blocks on one side, blocks on the other, width, fill height).</summary>
        public List<(string a, string b, double width, double z, PlanBox box)> ClosedGaps { get; } = new();

        /// <summary>Blocks with parts that stand on nothing (beyond groundSnap) and were filled down to the ground:
        /// per block, each connected region of such plan faces with its box and area.</summary>
        public List<(string block, List<(PlanBox box, double area)> regions)> Unsupported { get; } = new();

        /// <summary>Plan points merged or snapped onto an edge because they were closer than minEdge (beyond weld).</summary>
        public int ShortEdgeSnaps { get; set; }

        /// <summary>Regions thinner than minEdge raised to the higher roof beside them: (old height, new height).</summary>
        public List<(double from, double to)> ThinRaised { get; } = new();

        /// <summary>Short outline edges removed, by kind (<c>cutBump</c>, <c>fillNotch</c>, <c>alignJog</c>,
        /// <c>alignJogCut</c>, <c>squareCorner</c>).</summary>
        public SortedDictionary<string, int> OutlineFills { get; } = new(StringComparer.Ordinal);
    }

    private readonly IReadOnlyList<Solid> _solids;
    private readonly double _tol;
    private readonly double _minEdge;
    private readonly double _minGap;
    private readonly double _planTol;

    /// <param name="tolerance">Weld tolerance: points and heights closer than this are the same.</param>
    /// <param name="minEdge">Neighbouring roofs whose heights differ by less than this are made flush (raised to the
    /// higher one), because the step between them would be an edge shorter than minEdge.</param>
    /// <param name="minGap">Gaps between separate parts narrower than this are closed (see <see cref="GapCloser"/>);
    /// 0 turns gap closing off.</param>
    public HeightFieldUnion(IReadOnlyList<Solid> solids, double tolerance, double minEdge = 0, double minGap = 0)
    {
        _minGap = Math.Max(minGap, 0);
        // Points closer than minEdge are merged in the final plan, so no plan edge is shorter than minEdge. A distance
        // within 1e-6 m of minEdge (the precision of the output file) counts as minEdge and is kept.
        _planTol = Math.Max(Math.Max(tolerance, 1e-6), minEdge - GapCloser.Slack);
        _solids = solids;
        _tol = Math.Max(tolerance, 1e-6);
        _minEdge = Math.Max(minEdge, 0);
    }

    private sealed record Tag(int Solid, int Level, double Z);

    public Result Run()
    {
        var problems = new List<string>();

        // ---- 1. height levels from horizontal triangles
        var zs = new List<double>();
        var sloped = 0;
        var slopedBox = PlanBox.Empty;
        foreach (var s in _solids)
            foreach (var t in s.Triangles)
            {
                var (a, b, c) = (s.Vertices[t.A], s.Vertices[t.B], s.Vertices[t.C]);
                var dz = Math.Max(a.Z, Math.Max(b.Z, c.Z)) - Math.Min(a.Z, Math.Min(b.Z, c.Z));
                if (dz <= _tol) { zs.Add(a.Z); zs.Add(b.Z); zs.Add(c.Z); }
                else if (PlanWidth(a, b, c) > 2 * _tol) // vertical walls project to a sliver thinner than the tolerance
                {
                    sloped++;
                    slopedBox = slopedBox.With(new P2(a.X, a.Y)).With(new P2(b.X, b.Y)).With(new P2(c.X, c.Y));
                }
            }
        if (sloped > 0) problems.Add($"{sloped} sloped triangles (neither horizontal nor vertical) are not supported; ignored (within {slopedBox})");
        var levels = ClusterLevels(zs);
        int LevelOf(double z)
        {
            int lo = 0, hi = levels.Count - 1;
            while (lo < hi) { var m = (lo + hi) >> 1; if (levels[m].max + _tol < z) lo = m + 1; else hi = m; }
            return lo;
        }

        // ---- 2. boundary edges per (solid, level), tagged
        var tags = new List<Tag>();
        var segs = new List<(P2 a, P2 b, int tag)>();
        for (var si = 0; si < _solids.Count; si++)
        {
            var s = _solids[si];
            var perLevel = new SortedDictionary<int, Dictionary<(int, int), int>>();
            foreach (var t in s.Triangles)
            {
                var (a, b, c) = (s.Vertices[t.A], s.Vertices[t.B], s.Vertices[t.C]);
                var dz = Math.Max(a.Z, Math.Max(b.Z, c.Z)) - Math.Min(a.Z, Math.Min(b.Z, c.Z));
                if (dz > _tol) continue;
                var L = LevelOf(a.Z);
                if (!perLevel.TryGetValue(L, out var edges)) perLevel[L] = edges = new Dictionary<(int, int), int>();
                for (var k = 0; k < 3; k++)
                {
                    var key = Orienter.Key(t[k], t[(k + 1) % 3]);
                    edges[key] = edges.TryGetValue(key, out var n) ? n + 1 : 1;
                }
            }
            foreach (var (L, edges) in perLevel)
            {
                var tagId = tags.Count;
                var any = false;
                foreach (var ((u, v), n) in edges.OrderBy(kv => kv.Key))
                {
                    if (n % 2 == 0) continue;
                    var pu = s.Vertices[u]; var pv = s.Vertices[v];
                    segs.Add((new P2(pu.X, pu.Y), new P2(pv.X, pv.Y), tagId));
                    any = true;
                }
                if (any) tags.Add(new Tag(si, L, levels[L].z));
            }
        }
        var res = new Result { SolidShell = Enumerable.Repeat(-1, _solids.Count).ToArray() };
        res.Problems.AddRange(problems);
        if (tags.Count == 0) { res.Problems.Add("no horizontal faces: nothing to build"); return res; }

        // ---- 3-4. plan, labels and heights. The plan is built again after each repair of the plan:
        //   a. at weld precision: gaps below minGap are bridged (filled to the lower block), until none is left;
        //   b. still at weld precision: regions thinner than minEdge next to a higher roof are raised to it;
        //   c. finally with points closer than minEdge merged, so no plan edge is shorter than minEdge.
        // Gaps are bridged before snapping, so a gap narrower than minEdge is filled to the lower block, not shut by
        // moving the taller block's wall.
        var fills = new List<Fill>();
        // Copies of one fill (a gap bridge is found again on every level below the lower roof, with the same ring):
        // the face heights they give are those of one fill at the highest of their heights, so it is held once.
        var fillAt = new Dictionary<IReadOnlyList<(P2 a, P2 b)>, int>(SegsComparer.Instance);
        void AddFill(Fill f)
        {
            if (!f.Cut && fillAt.TryGetValue(f.Segs, out var i))
                fills[i] = fills[i] with { Z = Math.Max(fills[i].Z, f.Z), Copies = fills[i].Copies + 1 };
            else
            {
                if (!f.Cut) fillAt[f.Segs] = fills.Count;
                fills.Add(f);
            }
        }
        Bake? bake = null;
        var baseSegs = segs;
        var baseTags = tags;
        Plan plan;
        Plan solidPlan = null!; // the last plan built from the solids themselves: knows which solid covers each face
        var snapped = false;
        Func<P2, double>? cornerOf = null;
        var unbridged = new SortedSet<string>(StringComparer.Ordinal);
        for (var round = 0; ; round++)
        {
            plan = BuildPlan(baseSegs, baseTags, bake, fills, snapped ? _planTol : _tol, snapped ? cornerOf : null);
            if (baseTags.Count > 0) solidPlan = plan;
            if (round == MaxRounds)
            {
                res.Problems.Add($"plan repairs (gap closing, thin regions, short edges) did not settle after {MaxRounds} rounds");
                break;
            }
            var added = new List<Fill>();
            var found = _minGap > 0 ? GapCloser.Find(plan.Dcel, plan.Height, _minGap, _tol, _planTol) : new GapCloser.Found();
            foreach (var gap in found.Gaps)
            {
                var pair = new[] { BlocksOf(plan, solidPlan, gap.FaceA), BlocksOf(plan, solidPlan, gap.FaceB) };
                if (!gap.Bridged)
                    unbridged.Add($"gap of {gap.Width:0.###} m between {pair[0]} and {pair[1]} at {gap.Z:0.###} m could not be bridged (at {gap.Box})");
                else
                    res.ClosedGaps.Add((pair[0], pair[1], gap.Width, gap.Z, gap.Box));
            }
            added.AddRange(found.Bridges.Select(b => Fill.FromRings(new[] { b.Ring }, b.Z)));
            if (added.Count == 0 && _planTol > _tol)
            {
                var thin = ThinRegions(plan);
                res.ThinRaised.AddRange(thin.Select(t => (t.from, t.fill.Z)));
                added.AddRange(thin.Select(t => t.fill));
            }
            if (added.Count == 0 && _planTol > _tol)
            {
                var jogs = ShortOutlineEdges(plan);
                foreach (var (kind, _) in jogs) res.OutlineFills[kind] = res.OutlineFills.TryGetValue(kind, out var n) ? n + 1 : 1;
                added.AddRange(jogs.Select(j => j.fill));
            }
            if (added.Count > 0)
            {
                // Snapping left a gap, a thin region or a jog: the snapped plan becomes the base, repaired at weld
                // precision, then snapped again.
                if (snapped) Rebase(plan);
                foreach (var f in added) AddFill(f);
                continue;
            }
            if (!snapped && _planTol > _tol)
            {
                // Bake everything into one outline per roof level and snap those together, so no bridge or other
                // fill is ever snapped on its own (a narrow one could fold or detach).
                cornerOf = Corners(plan);
                Rebase(plan);
                snapped = true;
                continue;
            }
            break;
        }
        void Rebase(Plan p)
        {
            bake = Bake.Of(p);
            fills = new List<Fill>();
            fillAt.Clear();
            baseSegs = new List<(P2 a, P2 b, int tag)>();
            baseTags = new List<Tag>();
            snapped = false;
        }
        res.Problems.AddRange(unbridged);
        res.ShortEdgeSnaps = plan.Snapped;
        if (!ReferenceEquals(plan, solidPlan) && plan.Inconsistent > 0)
            res.Problems.Add($"snapped plan labelling inconsistent on {plan.Inconsistent} edges");
        var (pedges, pts, dcel, height) = (plan.Edges, plan.Points, plan.Dcel, plan.Height);
        if (solidPlan.Inconsistent > 0)
            res.Problems.Add($"plan labelling inconsistent on {solidPlan.Inconsistent} edges (a level boundary is not closed)");
        foreach (var (b, regions) in UnsupportedRegions(solidPlan))
        {
            res.Unsupported.Add((b, regions));
            res.Problems.Add($"block {b} has no support below part of it (it floats beyond groundSnap); filled down to the ground: " +
                             RegionText(regions));
        }
        foreach (var s in solidPlan.OpenSolids)
            res.Problems.Add($"object {_solids[s].Name}: horizontal faces do not close (open above or below), at " +
                             PlanBox.Of(_solids[s].Vertices.Select(v => new P2(v.X, v.Y))));

        // ---- 4b. steps lower than minEdge between neighbouring roofs: raise the lower roof to the higher one
        // Repeated until stable: raising a roof can bring it within minEdge of another one.
        if (_minEdge > 0)
            for (var i = 0; i < dcel.FaceCount && FlushSmallSteps(dcel, height, res); i++) { }

        // ---- 5. keep edges where the height changes; merge collinear chains
        var kept = new List<KeptEdge>();
        for (var e = 0; e < pedges.Count; e++)
        {
            var h = 2 * e;
            var hl = height[dcel.FaceOf(h)];
            var hr = height[dcel.FaceOf(h ^ 1)];
            if (hl != hr) kept.Add(new KeptEdge(pedges[e].u, pedges[e].v, h));
        }
        MergeCollinear(kept, pts);

        // ---- 6. shells: faces of the first plan connected through positive-height neighbours
        var shellOfFace = ShellsOfFaces(dcel, height);
        var final = Dcel.Build(pts, kept.Select(k => (k.U, k.V)).ToList());
        // Every final half-edge maps to a first-plan half-edge with the same left face.
        var refOf = new int[kept.Count * 2];
        for (var i = 0; i < kept.Count; i++) { refOf[2 * i] = kept[i].Ref; refOf[2 * i + 1] = kept[i].Ref ^ 1; }
        int FirstFace(int finalHalf) => dcel.FaceOf(refOf[finalHalf]);

        var shellIds = new SortedDictionary<int, int>(); // first-plan shell root -> output shell number
        var builders = new List<ShellBuilder>();
        var heightsAt = new Dictionary<int, SortedSet<double>>();
        void AddHeight(int v, double z)
        {
            if (!heightsAt.TryGetValue(v, out var set)) heightsAt[v] = set = new SortedSet<double> { 0 };
            set.Add(z);
        }
        for (var h = 0; h < final.From.Length; h++)
        {
            AddHeight(final.From[h], height[FirstFace(h)]);
        }
        ShellBuilder Builder(int firstFace)
        {
            var root = shellOfFace[firstFace];
            if (!shellIds.TryGetValue(root, out var id))
            {
                id = builders.Count;
                shellIds[root] = id;
                builders.Add(new ShellBuilder(pts));
            }
            return builders[id];
        }

        // Roofs and ground faces.
        for (var f = 1; f < final.FaceCount; f++)
        {
            var outer = final.FaceOuter[f];
            var ff = FirstFace(final.Cycles[outer][0]);
            var h = height[ff];
            foreach (var hc in final.FaceHoles[f])
                if (height[FirstFace(final.Cycles[hc][0])] != h)
                    res.Problems.Add("a hole cycle has a different height from its face");
            if (h <= 0) continue;
            var b = Builder(ff);
            if (!b.AddCap(final.RingIds(outer), final.FaceHoles[f].Select(final.RingIds).ToList(), h))
                res.Problems.Add($"could not triangulate a roof at {h:0.###} m, at {PlanBox.Of(final.Ring(outer))}");
        }
        foreach (var hc in final.FaceHoles[0])
            if (height[FirstFace(final.Cycles[hc][0])] != 0)
                res.Problems.Add("an outer boundary has positive height outside");

        // Walls.
        for (var i = 0; i < kept.Count; i++)
        {
            var hl = height[dcel.FaceOf(kept[i].Ref)];
            var hr = height[dcel.FaceOf(kept[i].Ref ^ 1)];
            var (u, v) = (kept[i].U, kept[i].V);
            var higherFace = hl > hr ? dcel.FaceOf(kept[i].Ref) : dcel.FaceOf(kept[i].Ref ^ 1);
            if (hr > hl) (u, v) = (v, u); // solid on the left of u→v
            var lo = Math.Min(hl, hr); var hi = Math.Max(hl, hr);
            Builder(higherFace).AddWall(u, v, Column(heightsAt[u], lo, hi), Column(heightsAt[v], lo, hi));
        }

        // Which shell each solid is in: the shell of the first face it covers in the last plan built from the solids
        // (in face order), found in the final plan by an interior point of that face. A solid whose faces all
        // vanished in snapping (thinner than minEdge) is in no shell.
        for (var g = 1; g < solidPlan.Dcel.FaceCount; g++)
        {
            if (solidPlan.Height[g] <= 0 || solidPlan.Covers[g].All(s => res.SolidShell[s] >= 0)) continue;
            var f = ReferenceEquals(plan, solidPlan) ? g : InteriorPoint(solidPlan.Dcel, g) is { } q ? Locate(dcel, q) : 0;
            if (f <= 0 || height[f] <= 0 || !shellIds.TryGetValue(shellOfFace[f], out var id)) continue;
            foreach (var s in solidPlan.Covers[g])
                if (res.SolidShell[s] < 0) res.SolidShell[s] = id;
        }

        for (var i = 0; i < builders.Count; i++)
        {
            var sh = builders[i].ToShell(_solids.Select((s, k) => (s, k)).Where(p => res.SolidShell[p.k] == i)
                .Select(p => p.s.Block).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList());
            res.Shells.Add(sh);
        }
        return res;
    }

    private const int MaxRounds = 12;

    /// <summary>Two walls within this angle count as parallel (a jog) when a short edge joins them.</summary>
    private const double JogParallelDeg = 10.0;

    private sealed class OutlineWall
    {
        public int From, To;
        public double Hi, Lo;
        public OutlineWall? Prev, Next;
    }

    /// <summary>
    /// Short edges of the outline (where the height changes) between two long walls:
    /// - a wall section with such a jog at each end, both stepping the same way (a shallow bump or notch): if the
    ///   section is no longer than its two neighbours together, it is cut back or filled out onto their line;
    /// - the two walls are parallel (a jog): the shorter wall moves onto the line of the other (the smaller change
    ///   of area), by filling the strip along it if that lies outside the solid or cutting it if inside; so a
    ///   shallow bump is cut back and a shallow notch filled;
    /// - the walls meet at a convex corner cut off by the short edge: the corner triangle out to where the walls'
    ///   lines meet is filled.
    /// Walls are chains of collinear plan edges with the higher side on the left; the short edge and both walls must
    /// have the same heights on each side. Anything else (a reflex chamfer, several short edges in a row) is left to
    /// the snapping of the final plan.
    /// </summary>
    private List<(string kind, Fill fill)> ShortOutlineEdges(Plan p)
    {
        var d = p.Dcel; var pts = d.Points;
        var halves = new List<(int h, double hi, double lo)>();
        for (var e = 0; 2 * e < d.From.Length; e++)
        {
            double hl = p.Height[d.FaceOf(2 * e)], hr = p.Height[d.FaceOf(2 * e + 1)];
            if (hl == hr) continue;
            halves.Add((hl > hr ? 2 * e : 2 * e + 1, Math.Max(hl, hr), Math.Min(hl, hr)));
        }
        var degree = new Dictionary<int, int>();
        foreach (var (h, _, _) in halves)
        {
            degree[d.From[h]] = degree.GetValueOrDefault(d.From[h]) + 1;
            degree[d.To[h]] = degree.GetValueOrDefault(d.To[h]) + 1;
        }
        var outOf = new Dictionary<int, (int h, double hi, double lo)>();
        foreach (var x in halves) if (degree[d.From[x.h]] == 2) outOf[d.From[x.h]] = x;
        bool Collinear(int a, int b, int c)
        {
            var ab = pts[b] - pts[a]; var ac = pts[c] - pts[a];
            return P2.Dot(ab, pts[c] - pts[b]) > 0 && Math.Abs(P2.Cross(ab, ac)) / ac.Length <= _tol;
        }
        // Chain collinear half-edges with the same heights into walls; link walls through degree-2 vertices.
        var wallOf = new Dictionary<int, OutlineWall>();
        var walls = new List<OutlineWall>();
        var hasCollinearPred = new HashSet<int>();
        foreach (var x in halves)
            if (outOf.TryGetValue(d.To[x.h], out var y) && y.hi == x.hi && y.lo == x.lo && Collinear(d.From[x.h], d.To[x.h], d.To[y.h]))
                hasCollinearPred.Add(y.h);
        foreach (var x in halves)
        {
            if (hasCollinearPred.Contains(x.h)) continue;
            var w = new OutlineWall { From = d.From[x.h], Hi = x.hi, Lo = x.lo };
            var cur = x;
            wallOf[cur.h] = w;
            while (outOf.TryGetValue(d.To[cur.h], out var nx) && hasCollinearPred.Contains(nx.h) && !wallOf.ContainsKey(nx.h))
            {
                cur = nx;
                wallOf[cur.h] = w;
            }
            w.To = d.To[cur.h];
            walls.Add(w);
        }
        foreach (var w in walls)
            if (outOf.TryGetValue(w.To, out var nx) && wallOf.TryGetValue(nx.h, out var nw) && nw != w && nw.Hi == w.Hi && nw.Lo == w.Lo)
            {
                w.Next = nw;
                nw.Prev = w;
            }

        double Len(OutlineWall w) => (pts[w.To] - pts[w.From]).Length;
        var cosPar = Math.Cos(JogParallelDeg * Math.PI / 180);
        var result = new List<(string, Fill)>();
        var used = new HashSet<OutlineWall>();
        bool Parallel(OutlineWall x, OutlineWall y)
        {
            var dx = pts[x.To] - pts[x.From]; var dy = pts[y.To] - pts[y.From];
            return P2.Dot(dx, dy) >= cosPar * dx.Length * dy.Length;
        }
        // A section with a jog at each end, both stepping the same way (a shallow bump or notch), is decided as one:
        // moving the section onto its neighbours' line changes δ × section, moving both neighbours δ × their sum.
        foreach (var w in walls)
        {
            if (Len(w) < _planTol || w.Prev is not { } s1 || w.Next is not { } s2 || s1 == s2) continue;
            if (Len(s1) >= _planTol || Len(s2) >= _planTol || s1.Prev is not { } pw || s2.Next is not { } nw) continue;
            if (pw == w || nw == w || Len(pw) < _planTol || Len(nw) < _planTol) continue;
            if (new[] { w, s1, s2, pw, nw }.Any(used.Contains) || !Parallel(pw, w) || !Parallel(w, nw)) continue;
            var dw = pts[w.To] - pts[w.From];
            var sideP = P2.Cross(dw, pts[s1.From] - pts[w.From]) / dw.Length;
            var sideN = P2.Cross(dw, pts[s2.To] - pts[w.From]) / dw.Length;
            if (Math.Abs(sideP) <= _tol || Math.Abs(sideN) <= _tol || Math.Sign(sideP) != Math.Sign(sideN)) continue;
            if (Len(w) > Len(pw) + Len(nw)) continue; // cheaper to move the neighbours: left to the single-jog rule
            var bump = sideP > 0; // neighbours' line on the solid side of the section: the section sticks out
            var ring = new[] { pts[s1.From], pts[w.From], pts[w.To], pts[s2.To] };
            if (Math.Abs(Triangulator.SignedArea(ring)) <= _tol * _tol) continue;
            foreach (var x in new[] { w, s1, s2, pw, nw }) used.Add(x);
            result.Add((bump ? "cutBump" : "fillNotch", Fill.FromRings(new[] { (IReadOnlyList<P2>)ring }, bump ? w.Lo : w.Hi, bump)));
        }
        foreach (var s in walls)
        {
            if (Len(s) >= _planTol || s.Prev is not { } pw || s.Next is not { } nw) continue;
            if (Len(pw) < _planTol || Len(nw) < _planTol || used.Contains(pw) || used.Contains(nw) || used.Contains(s)) continue;
            P2 a = pts[pw.From], u = pts[s.From], v = pts[s.To], b = pts[nw.To];
            var dp = (u - a) * (1 / (u - a).Length);
            var dn = (b - v) * (1 / (b - v).Length);
            P2 Project(P2 q, P2 o, P2 dir) => o + dir * P2.Dot(q - o, dir);
            P2[]? ring = null;
            string kind;
            var cut = false;
            if (P2.Dot(dp, dn) >= cosPar)
            {
                kind = "alignJog";
                // The shorter wall moves onto the other wall's line (the smaller change of area): the strip along it
                // is filled if it lies outside the solid, cut if inside. Solid on the left: the step goes outward
                // when the next wall lies on the right of the previous wall's line.
                var outward = P2.Cross(dp, v - u) < 0;
                var alongPrev = Math.Abs(Len(pw) - Len(nw)) <= _tol ? outward : Len(pw) < Len(nw);
                ring = alongPrev ? new[] { a, u, v, Project(a, v, dn) } : new[] { v, b, Project(b, u, dp), u };
                cut = alongPrev != outward;
            }
            else
            {
                kind = "squareCorner";
                // A short edge nearly in line with one of its walls is a kink (a split vertex), not a cut corner:
                // left to snapping, which keeps the sharper corner.
                var ds = (v - u) * (1 / (v - u).Length);
                if (Math.Abs(P2.Dot(ds, dp)) >= cosPar || Math.Abs(P2.Dot(ds, dn)) >= cosPar) continue;
                var den = P2.Cross(dp, dn);
                if (Math.Abs(den) < 1e-12) continue;
                var t = P2.Cross(v - u, dn) / den;
                var x = u + dp * t;
                // Convex corner only (the corner point outside, on the right of the short edge), and not far out.
                if (P2.Orient(u, v, x) >= 0 || t <= 0 || (x - u).Length > 2 * _planTol || (x - v).Length > 2 * _planTol) continue;
                ring = new[] { u, x, v };
            }
            if (Math.Abs(Triangulator.SignedArea(ring)) <= _tol * _tol) continue;
            used.Add(s); used.Add(pw); used.Add(nw);
            result.Add((cut ? kind + "Cut" : kind, Fill.FromRings(new[] { (IReadOnlyList<P2>)ring }, cut ? s.Lo : s.Hi, cut)));
        }
        return result;
    }

    /// <summary>
    /// How much of a corner each point of the plan is, on the outline where the height changes: a point where three
    /// or more such edges meet, or an end, counts as infinitely sharp; a point with two counts as its distance from
    /// the line through its two neighbours; a point on no such edge counts -1. Points are looked up within weld.
    /// </summary>
    private Func<P2, double> Corners(Plan p)
    {
        var d = p.Dcel;
        var nb = new Dictionary<int, List<int>>();
        for (var h = 0; h < d.From.Length; h += 2)
        {
            if (p.Height[d.FaceOf(h)] == p.Height[d.FaceOf(h ^ 1)]) continue;
            int u = d.From[h], v = d.To[h];
            if (!nb.TryGetValue(u, out var lu)) nb[u] = lu = new List<int>();
            if (!nb.TryGetValue(v, out var lv)) nb[v] = lv = new List<int>();
            lu.Add(v); lv.Add(u);
        }
        var index = new Planarizer(_tol);
        var value = new List<double>();
        for (var i = 0; i < d.Points.Count; i++)
        {
            var id = index.AddPoint(d.Points[i]);
            double c = -1;
            if (nb.TryGetValue(i, out var l))
            {
                if (l.Count != 2) c = double.PositiveInfinity;
                else
                {
                    var (a, b, x) = (d.Points[l[0]], d.Points[l[1]], d.Points[i]);
                    var ab = (b - a).Length;
                    c = ab > 0 ? Math.Abs(P2.Cross(b - a, x - a)) / ab : double.PositiveInfinity;
                }
            }
            while (value.Count <= id) value.Add(-1);
            value[id] = Math.Max(value[id], c);
        }
        return q =>
        {
            var id = index.AddPoint(q);
            return id < value.Count ? value[id] : -1;
        };
    }

    /// <summary>
    /// A region (inside an odd number of its segments, which form closed rings) filled from the ground up to
    /// <see cref="Z"/>, or, when <see cref="Cut"/>, cut down to <see cref="Z"/>. Cuts apply after all fills.
    /// <see cref="Lo"/> is the lowest level its outline stands for (merge priority in the snapped plan).
    /// </summary>
    private sealed record Fill(IReadOnlyList<(P2 a, P2 b)> Segs, double Z, bool Cut = false, double Lo = 0)
    {
        /// <summary>How many identical fills this one stands for (Z is the highest of theirs).</summary>
        public int Copies { get; init; } = 1;

        /// <summary>A fill from closed rings (from the ground up, unless a cut).</summary>
        public static Fill FromRings(IEnumerable<IReadOnlyList<P2>> rings, double z, bool cut = false) =>
            new(rings.SelectMany(r => r.Select((p, i) => (p, r[(i + 1) % r.Count]))).ToList(), z, cut, cut ? z : 0);
    }

    /// <summary>
    /// Regions of one height (plan faces joined across edges with the same height on both sides) that are thinner
    /// than minEdge, measured as 2 × area / perimeter (the width of a strip), and that touch a higher roof: each is
    /// raised to the higher roof it shares the longest boundary with, as a roof step lower than minEdge is (D-030),
    /// so a ledge thinner than minEdge becomes part of the block above it and the footprint is kept. A thin region with nothing higher
    /// beside it (a free-standing thin wall) is left alone.
    /// </summary>
    private List<(double from, Fill fill)> ThinRegions(Plan p)
    {
        var d = p.Dcel; var height = p.Height;
        var parent = Enumerable.Range(0, d.FaceCount).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (var h = 0; h < d.From.Length; h += 2)
        {
            int f = d.FaceOf(h), g = d.FaceOf(h ^ 1);
            if (f == 0 || g == 0 || height[f] <= 0 || height[f] != height[g]) continue;
            int a = Find(f), b = Find(g);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }
        var area = new SortedDictionary<int, double>();
        var perim = new Dictionary<int, double>();
        var higher = new Dictionary<int, SortedDictionary<double, double>>(); // region -> higher height -> shared length
        for (var f = 1; f < d.FaceCount; f++)
        {
            if (height[f] <= 0) continue;
            var r = Find(f);
            var a = d.CycleArea[d.FaceOuter[f]] + d.FaceHoles[f].Sum(c => d.CycleArea[c]);
            area[r] = (area.TryGetValue(r, out var a0) ? a0 : 0) + a;
        }
        for (var h = 0; h < d.From.Length; h++)
        {
            int f = d.FaceOf(h), g = d.FaceOf(h ^ 1);
            if (f == 0 || height[f] <= 0) continue;
            var r = Find(f);
            if (g != 0 && Find(g) == r) continue;
            var len = (d.Points[d.To[h]] - d.Points[d.From[h]]).Length;
            perim[r] = (perim.TryGetValue(r, out var p0) ? p0 : 0) + len;
            if (height[g] > height[f])
            {
                if (!higher.TryGetValue(r, out var byH)) higher[r] = byH = new SortedDictionary<double, double>();
                byH[height[g]] = byH.GetValueOrDefault(height[g]) + len;
            }
        }
        var result = new List<(double, Fill)>();
        foreach (var (r, a) in area)
        {
            if (!higher.TryGetValue(r, out var byHeight) || !perim.TryGetValue(r, out var per) || per <= 0) continue;
            // The higher roof it shares the longest boundary with (a ledge belongs to the block it runs along, not to
            // a taller block touching its end); ties go to the higher roof.
            var top = byHeight.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key).First().Key;
            if (2 * a / per >= _planTol) continue;
            var rings = new List<IReadOnlyList<P2>>();
            for (var f = 1; f < d.FaceCount; f++)
            {
                if (height[f] <= 0 || Find(f) != r) continue;
                rings.Add(d.Ring(d.FaceOuter[f]));
                foreach (var c in d.FaceHoles[f]) rings.Add(d.Ring(c));
            }
            result.Add((height[r], Fill.FromRings(rings, top)));
        }
        return result;
    }

    /// <summary>Compares fill outlines point by point (exact coordinates).</summary>
    private sealed class SegsComparer : IEqualityComparer<IReadOnlyList<(P2 a, P2 b)>>
    {
        public static readonly SegsComparer Instance = new();

        public bool Equals(IReadOnlyList<(P2 a, P2 b)>? x, IReadOnlyList<(P2 a, P2 b)>? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null || x.Count != y.Count) return false;
            for (var i = 0; i < x.Count; i++)
                if (x[i].a.X != y[i].a.X || x[i].a.Y != y[i].a.Y || x[i].b.X != y[i].b.X || x[i].b.Y != y[i].b.Y) return false;
            return true;
        }

        public int GetHashCode(IReadOnlyList<(P2 a, P2 b)> segs)
        {
            var h = new HashCode();
            h.Add(segs.Count);
            foreach (var (a, b) in segs) { h.Add(a.X); h.Add(a.Y); h.Add(b.X); h.Add(b.Y); }
            return h.ToHashCode();
        }
    }

    private sealed class Plan
    {
        public required List<(int u, int v, int[] tags)> Edges;
        public required List<P2> Points;
        public required Dcel Dcel;
        public required double[] Height;
        public required List<int>[] Covers;
        public int Inconsistent;
        public int Snapped;
        public SortedSet<int> OpenSolids = new();
        public SortedSet<int> FilledUnder = new();

        /// <summary>Faces with a roof but no solid reaching the ground below them (filled down).</summary>
        public List<int> UnsupportedFaces = new();
    }

    /// <summary>
    /// Per block (in name order), the connected regions (faces joined across shared edges) of the plan faces that it
    /// covers and that nothing below reaches the ground under: box and area of each, largest first.
    /// </summary>
    private List<(string block, List<(PlanBox box, double area)> regions)> UnsupportedRegions(Plan p)
    {
        var d = p.Dcel;
        var byBlock = new SortedDictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        foreach (var f in p.UnsupportedFaces)
            foreach (var s in p.Covers[f])
            {
                var b = _solids[s].Block;
                if (!byBlock.TryGetValue(b, out var set)) byBlock[b] = set = new SortedSet<int>();
                set.Add(f);
            }
        var result = new List<(string, List<(PlanBox, double)>)>();
        foreach (var (block, faces) in byBlock)
        {
            var seen = new HashSet<int>();
            var regions = new List<(PlanBox box, double area)>();
            foreach (var start in faces)
            {
                if (!seen.Add(start)) continue;
                var box = PlanBox.Empty;
                var area = 0.0;
                var queue = new Queue<int>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var f = queue.Dequeue();
                    box = box.Union(PlanBox.Of(d.Ring(d.FaceOuter[f])));
                    area += d.CycleArea[d.FaceOuter[f]] + d.FaceHoles[f].Sum(c => d.CycleArea[c]);
                    foreach (var c in FaceCycles(d, f))
                        foreach (var he in d.Cycles[c])
                        {
                            var g = d.FaceOf(Dcel.Twin(he));
                            if (faces.Contains(g) && seen.Add(g)) queue.Enqueue(g);
                        }
                }
                regions.Add((box, area));
            }
            result.Add((block, regions.OrderByDescending(r => r.area).ThenBy(r => r.box.X0).ThenBy(r => r.box.Y0).ToList()));
        }
        return result;
    }

    /// <summary>"1 region of 12.5 m² at [..]" or "3 regions, 40 m² in all, within [..]".</summary>
    internal static string RegionText(IReadOnlyList<(PlanBox box, double area)> regions)
    {
        var total = regions.Sum(r => r.area);
        var all = regions.Aggregate(PlanBox.Empty, (b, r) => b.Union(r.box));
        return regions.Count == 1
            ? $"1 region of {total.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} m² at {all}"
            : $"{regions.Count} regions, {total.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} m² in all, within {all}";
    }

    /// <summary>The solids covering face f of plan p: its own record, or for a baked plan (which has none) the
    /// covers of the face of the solid plan that holds an interior point of f.</summary>
    private static IReadOnlyList<int> CoversAt(Plan p, Plan solidPlan, int f)
    {
        if (ReferenceEquals(p, solidPlan)) return p.Covers[f];
        if (InteriorPoint(p.Dcel, f) is not { } q) return Array.Empty<int>();
        var g = Locate(solidPlan.Dcel, q);
        return g > 0 ? solidPlan.Covers[g] : Array.Empty<int>();
    }

    /// <summary>A point strictly inside face f: the centroid of the largest triangle of its triangulation.</summary>
    private static P2? InteriorPoint(Dcel d, int f)
    {
        if (f <= 0) return null;
        var outer = d.Ring(d.FaceOuter[f]);
        var holes = d.FaceHoles[f].Select(c => (IReadOnlyList<P2>)d.Ring(c)).ToList();
        var tris = new List<Model.Tri>();
        if (!Triangulator.TryTriangulate(outer, holes, tris) || tris.Count == 0) return null;
        var all = new List<P2>(outer);
        foreach (var h in holes) all.AddRange(h);
        var best = tris.OrderByDescending(t => Math.Abs(P2.Orient(all[t.A], all[t.B], all[t.C]))).First();
        return (all[best.A] + all[best.B] + all[best.C]) * (1.0 / 3);
    }

    /// <summary>The bounded face containing q (0 if none).</summary>
    private static int Locate(Dcel d, P2 q) => d.Locate(q);

    /// <summary>
    /// A plan baked into the outline of every roof level (region "height ≥ z"), so the height field can be built
    /// again from them alone. Each plan edge where the height changes lies on the outlines of a run of levels:
    /// those above its lower side up to its higher side, levels [From, To). It is held once with that run instead of
    /// once per level. Its tags are the two ends of the run (T + From and T + To, T the first bake tag): XOR is
    /// linear, so a face's label then holds the ends of the runs of levels it is inside of, and the levels themselves
    /// follow by a running XOR (see <see cref="BakedHeight"/>).
    /// </summary>
    private sealed class Bake
    {
        /// <summary>The roof levels, ascending.</summary>
        public required double[] Z { get; init; }

        /// <summary>Edges in the order their first level lists them (by From, then plan edge order).</summary>
        public required List<(P2 a, P2 b, int from, int to)> Edges { get; init; }

        public static Bake Of(Plan p)
        {
            var d = p.Dcel;
            var z = p.Height.Where(h => h > 0).Distinct().OrderBy(h => h).ToArray();
            int Above(double h) // index of the first level higher than h
            {
                int lo = 0, hi = z.Length;
                while (lo < hi) { var m = (lo + hi) >> 1; if (z[m] <= h) lo = m + 1; else hi = m; }
                return lo;
            }
            var edges = new List<(P2 a, P2 b, int from, int to, int e)>();
            for (var e = 0; e < p.Edges.Count; e++)
            {
                double hl = p.Height[d.FaceOf(2 * e)], hr = p.Height[d.FaceOf(2 * e + 1)];
                if (hl == hr) continue;
                int from = Above(Math.Min(hl, hr)), to = Above(Math.Max(hl, hr));
                if (from < to) edges.Add((p.Points[p.Edges[e].u], p.Points[p.Edges[e].v], from, to, e));
            }
            return new Bake
            {
                Z = z,
                Edges = edges.OrderBy(x => x.from).ThenBy(x => x.e).Select(x => (x.a, x.b, x.from, x.to)).ToList(),
            };
        }
    }

    /// <summary>
    /// The highest baked level a face is inside of, from the bake tags of its label (bits first..first+Z.Length),
    /// or 0 if none.
    /// </summary>
    private static double BakedHeight(ulong[] label, int first, double[] z)
    {
        var inside = false;
        var top = -1;
        for (var k = 0; k <= z.Length; k++)
        {
            var t = first + k;
            if ((label[t >> 6] & (1UL << (t & 63))) == 0) continue;
            inside = !inside;
            if (!inside) top = k - 1; // the run of levels ending just below k
        }
        if (inside) top = z.Length - 1;
        return top >= 0 ? z[top] : 0;
    }

    private string BlocksOf(Plan p, Plan solidPlan, int face)
    {
        // The blocks of the slice component containing this face: blocks covering any face joined to it at its height.
        var h = p.Height[face];
        var seen = new HashSet<int> { face };
        var queue = new Queue<int>();
        queue.Enqueue(face);
        var blocks = new SortedSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var f = queue.Dequeue();
            foreach (var s in CoversAt(p, solidPlan, f)) blocks.Add(_solids[s].Block);
            foreach (var c in FaceCycles(p.Dcel, f))
                foreach (var he in p.Dcel.Cycles[c])
                {
                    var g = p.Dcel.FaceOf(Dcel.Twin(he));
                    if (p.Height[g] >= h && seen.Add(g)) queue.Enqueue(g);
                }
        }
        return blocks.Count == 0 ? "?" : string.Join("+", blocks);
    }

    /// <summary>The first set bit at or after <paramref name="from"/> and before <paramref name="end"/> (or end).</summary>
    private static int NextBit(ulong[] bits, int from, int end)
    {
        for (var w = from >> 6; w < bits.Length && (w << 6) < end; w++)
        {
            var x = bits[w];
            if (w == from >> 6) x &= ~0UL << (from & 63);
            if (x != 0)
            {
                var t = (w << 6) + System.Numerics.BitOperations.TrailingZeroCount(x);
                return t < end ? t : end;
            }
        }
        return end;
    }

    /// <summary>Planarises the level boundaries (and bridge fills), labels the plan faces and gives each its height.</summary>
    private Plan BuildPlan(List<(P2 a, P2 b, int tag)> segs, List<Tag> tags, Bake? bake, List<Fill> fills, double planTol,
        Func<P2, double>? corner = null)
    {
        // Which point survives when points closer than the tolerance merge: the first one added. Points are added
        // in priority order: first those on the lowest outline (the footprint at the ground beats a roof edge above
        // it, so a ledge thinner than minEdge is absorbed by extending the block above it over the ledge, and the
        // footprint is kept); then, among those, the one on the highest roof (a tall block's wall stays where it is,
        // and a lower block or a gap bridge next to it takes its place, so the gap is still filled to the lower
        // block). Fills reach from the ground up to their height.
        // Tags: the solids' level boundaries [0, T), then the bake's run ends [T, T + levels + 1), then the fills.
        var bakeLevels = bake?.Z.Length ?? 0;
        var fill0 = tags.Count + (bake is null ? 0 : bakeLevels + 1);
        var all = new List<(P2 a, P2 b, int[] tags, int weight, double lo, double hi)>(segs.Count + fills.Count * 4);
        foreach (var (a, b, t) in segs) all.Add((a, b, new[] { t }, 1, tags[t].Z, tags[t].Z));
        // A baked edge stands for one copy on each level of its run (lowest level first, as one fill per level was).
        if (bake is not null)
            foreach (var (a, b, from, to) in bake.Edges)
                all.Add((a, b, new[] { tags.Count + from, tags.Count + to }, to - from, bake.Z[from], bake.Z[to - 1]));
        for (var k = 0; k < fills.Count; k++)
            foreach (var (a, b) in fills[k].Segs) all.Add((a, b, new[] { fill0 + k }, fills[k].Copies, fills[k].Lo, fills[k].Z));
        var clusters = new Planarizer(_tol);
        var key = new Dictionary<int, (double lo, double hi)>();
        void Key(P2 p, double lo, double hi)
        {
            var id = clusters.AddPoint(p);
            key[id] = key.TryGetValue(id, out var k0) ? (Math.Min(k0.lo, lo), Math.Max(k0.hi, hi)) : (lo, hi);
        }
        foreach (var (a, b, _, _, lo, hi) in all) { Key(a, lo, hi); Key(b, lo, hi); }
        // Points merge at planTol, and segments are split at points within planTol: in the snapped plan all points
        // end up at least minEdge apart and from every segment, so no edge can be shorter and snapping again changes
        // nothing.
        var planarizer = new Planarizer(planTol, reportAbove: _tol);
        // Ties on the lowest outline go to the sharper corner of the weld-precision plan, so collapsing a short jog
        // moves the nearly straight point, not a corner of a long wall.
        var sharp = key.Keys.ToDictionary(i => i, i => corner?.Invoke(clusters.Points[i]) ?? 0);
        foreach (var id in key.Keys.OrderBy(i => key[i].lo).ThenByDescending(i => sharp[i]).ThenByDescending(i => key[i].hi).ThenBy(i => i))
            planarizer.AddPoint(clusters.Points[id]);
        foreach (var (a, b, t, w, _, _) in all) planarizer.AddSegment(a, b, t, w);
        var pedges = planarizer.Build();
        var pts = planarizer.Points;
        var dcel = Dcel.Build(pts, pedges.Select(e => (e.u, e.v)).ToList());
        var words = (fill0 + fills.Count + 63) / 64;

        // ---- 3. label faces by toggling tags across edges, starting from the outside
        var edgeBits = pedges.Select(e =>
        {
            var b = new ulong[words];
            foreach (var t in e.tags) b[t >> 6] ^= 1UL << (t & 63);
            return b;
        }).ToArray();
        var label = new ulong[dcel.FaceCount][];
        label[0] = new ulong[words];
        var queue = new Queue<int>();
        queue.Enqueue(0);
        var inconsistent = 0;
        while (queue.Count > 0)
        {
            var f = queue.Dequeue();
            foreach (var c in FaceCycles(dcel, f))
                foreach (var h in dcel.Cycles[c])
                {
                    var g = dcel.FaceOf(Dcel.Twin(h));
                    var eb = edgeBits[h >> 1];
                    var lf = label[f];
                    if (label[g] is null)
                    {
                        var lg = new ulong[words];
                        for (var w = 0; w < words; w++) lg[w] = lf[w] ^ eb[w];
                        label[g] = lg;
                        queue.Enqueue(g);
                    }
                    else
                    {
                        var lg = label[g];
                        for (var w = 0; w < words; w++)
                            if (lg[w] != (lf[w] ^ eb[w])) { inconsistent++; break; }
                    }
                }
        }

        // ---- 4. height of each face; which solids cover it; bridges raise faces to their height
        var height = new double[dcel.FaceCount];
        var covers = new List<int>[dcel.FaceCount];
        var plan = new Plan { Edges = pedges, Points = pts, Dcel = dcel, Height = height, Covers = covers };
        var lowest = new double[dcel.FaceCount];
        for (var f = 0; f < dcel.FaceCount; f++)
        {
            covers[f] = new List<int>();
            if (label[f] is null) { inconsistent++; continue; }
            var curSolid = -1; var inside = false; var top = 0.0; var hasTop = false; var bottom = 0.0;
            lowest[f] = double.PositiveInfinity;
            void Close()
            {
                if (curSolid < 0) return;
                if (inside) plan.OpenSolids.Add(curSolid);
                if (hasTop) { covers[f].Add(curSolid); height[f] = Math.Max(height[f], top); lowest[f] = Math.Min(lowest[f], bottom); }
            }
            for (var t = NextBit(label[f], 0, tags.Count); t < tags.Count; t = NextBit(label[f], t + 1, tags.Count))
            {
                var tg = tags[t];
                if (tg.Solid != curSolid) { Close(); curSolid = tg.Solid; inside = false; hasTop = false; bottom = tg.Z; }
                inside = !inside;
                if (!inside) { top = tg.Z; hasTop = true; }
            }
            Close();
            if (height[f] > 0 && lowest[f] > _tol)
            {
                foreach (var s in covers[f]) plan.FilledUnder.Add(s);
                plan.UnsupportedFaces.Add(f);
            }
            if (bake is not null) height[f] = Math.Max(height[f], BakedHeight(label[f], tags.Count, bake.Z));
            var fillEnd = fill0 + fills.Count;
            for (var t = NextBit(label[f], fill0, fillEnd); t < fillEnd; t = NextBit(label[f], t + 1, fillEnd))
                if (!fills[t - fill0].Cut) height[f] = Math.Max(height[f], fills[t - fill0].Z);
            for (var t = NextBit(label[f], fill0, fillEnd); t < fillEnd; t = NextBit(label[f], t + 1, fillEnd))
                if (fills[t - fill0].Cut) height[f] = Math.Min(height[f], fills[t - fill0].Z);
            if (height[f] < 0) height[f] = 0;
        }
        plan.Inconsistent = inconsistent;
        plan.Snapped = planarizer.Snapped;
        return plan;
    }

    /// <returns>Whether any height changed.</returns>
    private bool FlushSmallSteps(Dcel d, double[] height, Result res)
    {
        var parent = Enumerable.Range(0, d.FaceCount).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        var any = false;
        // Faces meeting at a plan vertex (sharing an edge, or only touching corner to corner) whose roofs differ by
        // less than minEdge would give that vertex's wall column an edge shorter than minEdge. Equal heights join
        // too: a roof made of several plan faces is raised as a whole, not only the face that touches the higher
        // roof (that would leave a new step lower than minEdge next to it).
        var around = new SortedDictionary<int, SortedSet<int>>();
        for (var h = 0; h < d.From.Length; h++)
        {
            if (!around.TryGetValue(d.From[h], out var set)) around[d.From[h]] = set = new SortedSet<int>();
            set.Add(d.FaceOf(h));
        }
        foreach (var faces in around.Values)
        {
            var list = faces.Where(f => height[f] > 0).ToList();
            for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
            {
                int f = list[i], g = list[j];
                if (Math.Abs(height[f] - height[g]) >= _minEdge - 1e-9) continue;
                int a = Find(f), b = Find(g);
                if (a != b) { parent[Math.Max(a, b)] = Math.Min(a, b); any = true; }
            }
        }
        if (!any) return false;
        var groups = Enumerable.Range(0, d.FaceCount).GroupBy(Find).Where(g => g.Count() > 1);
        var steps = new SortedDictionary<(double, double), int>();
        foreach (var g in groups)
        {
            var top = g.Max(f => height[f]);
            foreach (var f in g)
                if (height[f] != top)
                {
                    var key = (height[f], top);
                    steps[key] = steps.TryGetValue(key, out var n) ? n + 1 : 1;
                    height[f] = top;
                }
        }
        foreach (var ((from, to), n) in steps) res.FlushedSteps.Add((from, to, n));
        return steps.Count > 0;
    }

    /// <summary>Width of a triangle's plan projection: twice its plan area over its longest plan edge.</summary>
    private static double PlanWidth(Vec3 a, Vec3 b, Vec3 c)
    {
        var area2 = Math.Abs(Vec3.Cross(b - a, c - a).Z);
        double L(Vec3 p, Vec3 q) => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
        var longest = Math.Max(L(a, b), Math.Max(L(b, c), L(c, a)));
        return longest > 0 ? area2 / longest : 0;
    }

    private static IEnumerable<int> FaceCycles(Dcel d, int f)
    {
        if (d.FaceOuter[f] >= 0) yield return d.FaceOuter[f];
        foreach (var h in d.FaceHoles[f]) yield return h;
    }

    private static List<double> Column(SortedSet<double> heights, double lo, double hi) =>
        heights.Where(z => z >= lo && z <= hi).ToList();

    private List<(double z, double max)> ClusterLevels(List<double> zs)
    {
        zs.Sort();
        var result = new List<(double z, double max)>();
        var start = 0;
        for (var i = 1; i <= zs.Count; i++)
        {
            if (i < zs.Count && zs[i] - zs[i - 1] <= _tol) continue;
            var median = zs[(start + i - 1) / 2];
            var z = Math.Round(median, 6);
            if (Math.Abs(z) < 1e-9) z = 0;
            result.Add((z, zs[i - 1]));
            start = i;
        }
        return result;
    }

    private sealed class KeptEdge(int u, int v, int @ref)
    {
        public int U = u, V = v;

        /// <summary>A first-plan half-edge running in the U→V direction along this edge (same left and right faces).</summary>
        public int Ref = @ref;
    }

    /// <summary>Removes degree-2 vertices whose two edges are collinear within tolerance and bound the same faces.</summary>
    private void MergeCollinear(List<KeptEdge> kept, IReadOnlyList<P2> pts)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            var at = new Dictionary<int, List<KeptEdge>>();
            foreach (var k in kept)
            {
                if (!at.TryGetValue(k.U, out var a)) at[k.U] = a = new List<KeptEdge>();
                a.Add(k);
                if (!at.TryGetValue(k.V, out var b)) at[k.V] = b = new List<KeptEdge>();
                b.Add(k);
            }
            var existing = new HashSet<(int, int)>(kept.Select(k => Orienter.Key(k.U, k.V)));
            var removed = new HashSet<KeptEdge>();
            foreach (var (v, list) in at.OrderBy(kv => kv.Key))
            {
                if (list.Count != 2 || removed.Contains(list[0]) || removed.Contains(list[1])) continue;
                // Orient as a→v (e1) and v→b (e2).
                var e1 = list[0]; var e2 = list[1];
                int a, b, ref1, ref2;
                if (e1.V == v) { a = e1.U; ref1 = e1.Ref; } else { a = e1.V; ref1 = e1.Ref ^ 1; }
                if (e2.U == v) { b = e2.V; ref2 = e2.Ref; } else { b = e2.U; ref2 = e2.Ref ^ 1; }
                if (a == b || existing.Contains(Orienter.Key(a, b))) continue;
                var pa = pts[a]; var pb = pts[b]; var pv = pts[v];
                var d = pb - pa;
                var len = d.Length;
                if (len == 0) continue;
                if (Math.Abs(P2.Cross(d, pv - pa)) / len > _tol) continue;
                if (P2.Dot(pv - pa, d) <= 0 || P2.Dot(pv - pb, d) >= 0) continue; // v must lie between a and b
                _ = ref2;
                removed.Add(e1); removed.Add(e2);
                kept.Add(new KeptEdge(a, b, ref1));
                existing.Add(Orienter.Key(a, b));
                changed = true;
            }
            if (removed.Count > 0) kept.RemoveAll(removed.Contains);
        }
        kept.Sort((x, y) => x.U != y.U ? x.U.CompareTo(y.U) : x.V.CompareTo(y.V));
    }

    /// <summary>Union-find of first-plan faces with positive height that share an edge. Returns each face's root.</summary>
    private static int[] ShellsOfFaces(Dcel d, double[] height)
    {
        var parent = Enumerable.Range(0, d.FaceCount).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (var h = 0; h < d.From.Length; h += 2)
        {
            int f = d.FaceOf(h), g = d.FaceOf(h ^ 1);
            if (height[f] <= 0 || height[g] <= 0) continue;
            int a = Find(f), b = Find(g);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }
        return Enumerable.Range(0, d.FaceCount).Select(Find).ToArray();
    }
}
