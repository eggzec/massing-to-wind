namespace M2W.Generator;

/// <summary>Measures of one merged shell, computed from the height field.</summary>
public sealed record ShellStats(int Component, double Volume, double FootprintArea, double MaxHeight, int Genus, IReadOnlyList<string> Blocks);

/// <summary>
/// The merged (union) geometry of a set of blocks as a 2.5D height field on a rectilinear grid in the site's grid
/// frame. Every block stands on z = 0 and its tiers are nested, so the union over each grid cell is the single column
/// [0, max roof]. Volume, footprint, height, connectivity (shells) and genus (through-holes) follow exactly from the
/// cells. This is the ground truth for expected.json.
/// </summary>
public sealed class HeightField
{
    private HeightField(double[] xs, double[] ys, double[,] top, List<int>[,] owners, IReadOnlyList<Block> blocks)
    {
        Xs = xs;
        Ys = ys;
        Top = top;
        Owners = owners;
        Blocks = blocks;
        Comp = new int[Nx, Ny];
        LabelComponents();
    }

    public double[] Xs { get; }
    public double[] Ys { get; }
    public int Nx => Xs.Length - 1;
    public int Ny => Ys.Length - 1;

    /// <summary>Roof height of each cell; 0 = empty.</summary>
    public double[,] Top { get; }

    /// <summary>Indices of the blocks covering each cell.</summary>
    public List<int>[,] Owners { get; }

    public IReadOnlyList<Block> Blocks { get; }

    /// <summary>Component (shell) index of each cell, -1 if empty.</summary>
    public int[,] Comp { get; }

    public int ComponentCount { get; private set; }

    public static HeightField Build(IReadOnlyList<Block> blocks)
    {
        var xs = SortedUnique(blocks.SelectMany(b => b.Lines(0)));
        var ys = SortedUnique(blocks.SelectMany(b => b.Lines(1)));
        int nx = xs.Length - 1, ny = ys.Length - 1;
        var top = new double[nx, ny];
        var owners = new List<int>[nx, ny];
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < ny; j++)
            {
                double u = (xs[i] + xs[i + 1]) / 2, v = (ys[j] + ys[j + 1]) / 2;
                var list = new List<int>();
                double h = 0;
                for (var b = 0; b < blocks.Count; b++)
                {
                    var t = blocks[b].TopAt(u, v);
                    if (t > 0) { list.Add(b); h = Math.Max(h, t); }
                }
                top[i, j] = h;
                owners[i, j] = list;
            }
        return new HeightField(xs, ys, top, owners, blocks);
    }

    public static double[] SortedUnique(IEnumerable<double> values)
    {
        var sorted = values.Select(v => Math.Round(v, 6)).Distinct().OrderBy(v => v).ToList();
        return sorted.ToArray();
    }

    public double CellArea(int i, int j) => (Xs[i + 1] - Xs[i]) * (Ys[j + 1] - Ys[j]);

    public Rect CellRect(int i, int j) => new(Xs[i], Ys[j], Xs[i + 1], Ys[j + 1]);

    private double T(int i, int j) => i < 0 || j < 0 || i >= Nx || j >= Ny ? 0 : Top[i, j];

    public double TotalVolume()
    {
        double v = 0;
        for (var i = 0; i < Nx; i++)
            for (var j = 0; j < Ny; j++)
                v += CellArea(i, j) * Top[i, j];
        return v;
    }

    private void LabelComponents()
    {
        for (var i = 0; i < Nx; i++)
            for (var j = 0; j < Ny; j++)
                Comp[i, j] = -1;
        var n = 0;
        var stack = new Stack<(int, int)>();
        for (var i = 0; i < Nx; i++)
            for (var j = 0; j < Ny; j++)
            {
                if (Top[i, j] <= 0 || Comp[i, j] >= 0) continue;
                Comp[i, j] = n;
                stack.Push((i, j));
                while (stack.Count > 0)
                {
                    var (a, b) = stack.Pop();
                    foreach (var (c, d) in Neighbours(a, b))
                        if (Top[c, d] > 0 && Comp[c, d] < 0)
                        {
                            Comp[c, d] = n;
                            stack.Push((c, d));
                        }
                }
                n++;
            }
        ComponentCount = n;
    }

    private IEnumerable<(int, int)> Neighbours(int i, int j)
    {
        if (i > 0) yield return (i - 1, j);
        if (i + 1 < Nx) yield return (i + 1, j);
        if (j > 0) yield return (i, j - 1);
        if (j + 1 < Ny) yield return (i, j + 1);
    }

    /// <summary>Stats for every component, in component order.</summary>
    public List<ShellStats> Shells()
    {
        var result = new List<ShellStats>();
        for (var c = 0; c < ComponentCount; c++)
        {
            double vol = 0, area = 0, maxH = 0;
            var names = new SortedSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < Nx; i++)
                for (var j = 0; j < Ny; j++)
                {
                    if (Comp[i, j] != c) continue;
                    var a = CellArea(i, j);
                    vol += a * Top[i, j];
                    area += a;
                    maxH = Math.Max(maxH, Top[i, j]);
                    foreach (var b in Owners[i, j]) names.Add(Blocks[b].Name);
                }
            result.Add(new ShellStats(c, vol, area, maxH, Genus(c), names.ToList()));
        }
        return result;
    }

    /// <summary>Number of holes in the footprint of component <paramref name="c"/>: connected empty regions (other
    /// shells count as empty) that do not reach the outside. The solid deformation-retracts onto its footprint, so this
    /// is the genus of its surface.</summary>
    public int Genus(int c)
    {
        // Padded grid: index 0 and Nx+1 are outside.
        int w = Nx + 2, h = Ny + 2;
        var seen = new bool[w, h];
        bool Solid(int i, int j) => i >= 1 && j >= 1 && i <= Nx && j <= Ny && Comp[i - 1, j - 1] == c;
        var holes = 0;
        var stack = new Stack<(int, int)>();
        for (var i = 0; i < w; i++)
            for (var j = 0; j < h; j++)
            {
                if (seen[i, j] || Solid(i, j)) continue;
                var touchesBorder = false;
                seen[i, j] = true;
                stack.Push((i, j));
                while (stack.Count > 0)
                {
                    var (a, b) = stack.Pop();
                    if (a == 0 || b == 0 || a == w - 1 || b == h - 1) touchesBorder = true;
                    foreach (var (da, db) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int x = a + da, y = b + db;
                        if (x < 0 || y < 0 || x >= w || y >= h || seen[x, y] || Solid(x, y)) continue;
                        seen[x, y] = true;
                        stack.Push((x, y));
                    }
                }
                if (!touchesBorder) holes++;
            }
        return holes;
    }

    /// <summary>
    /// Finds grid vertices where the union would be non-manifold: two diagonal cells both above some level while the
    /// other two are below it (columns touching along a single vertical edge). Returns a description or null.
    /// </summary>
    public string? FindNonManifoldVertex()
    {
        for (var i = 0; i <= Nx; i++)
            for (var j = 0; j <= Ny; j++)
            {
                double sw = T(i - 1, j - 1), se = T(i, j - 1), nw = T(i - 1, j), ne = T(i, j);
                if (Math.Min(sw, ne) > Math.Max(se, nw) || Math.Min(se, nw) > Math.Max(sw, ne))
                    return $"diagonal contact at ({Xs[i]}, {Ys[j]})";
            }
        return null;
    }

    /// <summary>
    /// For each component, the grid lines where its outline or roof steps ("used" lines) must be at least
    /// <paramref name="minFeature"/> apart. That bounds every edge length, wall width, notch and step from below.
    /// Returns a description of the first violation or null.
    /// </summary>
    public string? FindNarrowFeature(double minFeature)
    {
        for (var c = 0; c < ComponentCount; c++)
        {
            var usedX = new SortedSet<double>();
            var usedY = new SortedSet<double>();
            for (var i = 0; i <= Nx; i++)
                for (var j = 0; j < Ny; j++)
                {
                    double l = T(i - 1, j), r = T(i, j);
                    if (l == r) continue;
                    if ((i > 0 && Comp[i - 1, j] == c) || (i < Nx && Comp[i, j] == c)) usedX.Add(Xs[i]);
                }
            for (var j = 0; j <= Ny; j++)
                for (var i = 0; i < Nx; i++)
                {
                    double b = T(i, j - 1), a = T(i, j);
                    if (a == b) continue;
                    if ((j > 0 && Comp[i, j - 1] == c) || (j < Ny && Comp[i, j] == c)) usedY.Add(Ys[j]);
                }
            var p = Spacing(usedX, minFeature, "u") ?? Spacing(usedY, minFeature, "v");
            if (p is not null) return $"shell {c}: {p}";
        }
        return null;
    }

    private static string? Spacing(SortedSet<double> lines, double min, string axis)
    {
        double? prev = null;
        foreach (var x in lines)
        {
            if (prev is double p && x - p < min - 1e-6) return $"{axis} lines {p} and {x} are {x - p:0.###} m apart (< {min})";
            prev = x;
        }
        return null;
    }

    /// <summary>Cells of a component merged into row runs (fewer rectangles for distance queries).</summary>
    public List<Rect> ComponentRects(int c)
    {
        var rects = new List<Rect>();
        for (var j = 0; j < Ny; j++)
        {
            var i = 0;
            while (i < Nx)
            {
                if (Comp[i, j] != c) { i++; continue; }
                var i0 = i;
                while (i < Nx && Comp[i, j] == c) i++;
                rects.Add(new Rect(Xs[i0], Ys[j], Xs[i], Ys[j + 1]));
            }
        }
        return rects;
    }

    /// <summary>
    /// A point strictly inside component <paramref name="c"/>'s footprint, as far from its outline as the grid allows:
    /// the cell centre with the largest clearance to every cell outside the component (and to the grid's edge).
    /// Ties go to the first cell in (i, j) order, so the result is deterministic.
    /// </summary>
    public (double U, double V, double Clearance) InteriorPoint(int c)
    {
        var outside = new List<Rect>();
        for (var j = 0; j < Ny; j++)
        {
            var i = 0;
            while (i < Nx)
            {
                if (Comp[i, j] == c) { i++; continue; }
                var i0 = i;
                while (i < Nx && Comp[i, j] != c) i++;
                outside.Add(new Rect(Xs[i0], Ys[j], Xs[i], Ys[j + 1]));
            }
        }
        (double U, double V, double Clearance) best = (double.NaN, double.NaN, -1);
        for (var i = 0; i < Nx; i++)
            for (var j = 0; j < Ny; j++)
            {
                if (Comp[i, j] != c) continue;
                double u = (Xs[i] + Xs[i + 1]) / 2, v = (Ys[j] + Ys[j + 1]) / 2;
                var d = Math.Min(Math.Min(u - Xs[0], Xs[^1] - u), Math.Min(v - Ys[0], Ys[^1] - v));
                var p = new Rect(u, v, u, v);
                foreach (var o in outside) d = Math.Min(d, p.DistanceTo(o));
                if (d > best.Clearance) best = (u, v, d);
            }
        return best;
    }

    /// <summary>Plan distance between two components. Every shell stands on the ground, so it is also the 3D distance.</summary>
    public double Distance(int a, int b)
    {
        var ra = ComponentRects(a);
        var rb = ComponentRects(b);
        var d = double.PositiveInfinity;
        foreach (var x in ra)
            foreach (var y in rb)
                d = Math.Min(d, x.DistanceTo(y));
        return d;
    }

    /// <summary>The component each named block belongs to. A block split over two components is reported as -2.</summary>
    public Dictionary<string, int> BlockComponents()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < Nx; i++)
            for (var j = 0; j < Ny; j++)
                foreach (var b in Owners[i, j])
                {
                    var name = Blocks[b].Name;
                    var c = Comp[i, j];
                    if (!map.TryGetValue(name, out var prev)) map[name] = c;
                    else if (prev != c) map[name] = -2;
                }
        return map;
    }
}
