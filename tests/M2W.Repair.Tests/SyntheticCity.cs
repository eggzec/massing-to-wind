using M2W.Geometry;

namespace M2W.Repair.Tests;

/// <summary>
/// A seeded synthetic district for the scale tests: a grid of city blocks, each two terraced rows of houses back to
/// back. Houses in a row share walls; now and then a house stands 0.1–0.9 m off its neighbour, or an alley more than
/// 1 m wide opens. The two rows of a city block are either 0.1–0.9 m apart or a garden of 2–8 m. Streets are 12–18 m.
/// Heights vary; some houses carry a set-back upper tier (its own part, standing on the house roof), and a few carry a
/// raised slab (a canopy at 4.0–4.4 m that overhangs the street by 3 m).
///
/// Every part is a box (a rectangle in plan, extruded) with its own 8 vertices, so the same vertex list can be given
/// as one object per part (<see cref="AsObjects"/>, objects <c>B0001</c>…) or as one object
/// (<see cref="AsOneObject"/>). Plan coordinates are on a 1 cm grid and distinct features are at least 0.1 m apart, so
/// nothing lands within weld of something else by accident. With <c>rotated</c>, each city block is turned by up to
/// 8° about its centre (shared corners stay shared: they are the same point turned the same way) and heights are to
/// 1 cm instead of 0.1 m, so there are many more distinct roof levels.
/// </summary>
internal sealed class SyntheticCity
{
    /// <summary>A part: rectangle [X0, X1] × [Y0, Y1] in its city block's frame, turned by Angle (radians) about
    /// (Cx, Cy), from Z0 to Z1.</summary>
    public sealed record Box(double X0, double Y0, double Z0, double X1, double Y1, double Z1)
    {
        public double Angle { get; init; }
        public double Cx { get; init; }
        public double Cy { get; init; }

        public Vec3 Corner(double x, double y, double z)
        {
            if (Angle == 0) return new Vec3(x, y, z);
            double c = Math.Cos(Angle), s = Math.Sin(Angle), dx = x - Cx, dy = y - Cy;
            return new Vec3(Cx + c * dx - s * dy, Cy + s * dx + c * dy, z);
        }
    }

    private double _heightStep = 0.1;

    public List<Box> Parts { get; } = new();

    public int Houses { get; private set; }
    public int Tiers { get; private set; }
    public int Slabs { get; private set; }
    public int SmallGaps { get; private set; }

    /// <param name="seed">Fixed seed: the same seed gives the same city.</param>
    /// <param name="targetParts">The city grows block by block until it has at least this many parts.</param>
    public static SyntheticCity Build(int seed, int targetParts = 500, bool rotated = false)
    {
        var city = new SyntheticCity { _heightStep = rotated ? 0.01 : 0.1 };
        var rng = new Random(seed);
        double R(double lo, double hi) => Math.Round(lo + (hi - lo) * rng.NextDouble(), 2);
        const int blocksPerRow = 6;
        double streetY = 0;
        double rowDepthMax = 0;
        var cityBlock = 0;
        double x = 0;
        while (city.Parts.Count < targetParts)
        {
            if (cityBlock % blocksPerRow == 0 && cityBlock > 0)
            {
                streetY += rowDepthMax + R(12, 18);
                x = 0;
                rowDepthMax = 0;
            }
            var y0 = streetY;
            var depthS = R(8, 12);
            var depthN = R(8, 12);
            var back = rng.NextDouble() < 0.5 ? R(0.1, 0.9) : R(2, 8);
            if (back < 1) city.SmallGaps++;
            var x0 = x;
            var first0 = city.Parts.Count;
            var lengthTarget = R(45, 70);
            // South row faces the street to the south (front at y0); north row faces north.
            var xEndS = city.Row(rng, R, x0, lengthTarget, y0, y0 + depthS, frontLow: true);
            var yN0 = Math.Round(y0 + depthS + back, 2);
            var xEndN = city.Row(rng, R, x0, lengthTarget, yN0, Math.Round(yN0 + depthN, 2), frontLow: false);
            rowDepthMax = Math.Max(rowDepthMax, depthS + back + depthN);
            if (rotated)
            {
                // Turn the whole city block (both rows, from the first south house) about its centre.
                var angle = (rng.NextDouble() * 16 - 8) * Math.PI / 180;
                double cx = (x0 + Math.Max(xEndS, xEndN)) / 2, cy = y0 + (depthS + back + depthN) / 2;
                for (var i = first0; i < city.Parts.Count; i++)
                    city.Parts[i] = city.Parts[i] with { Angle = angle, Cx = cx, Cy = cy };
            }
            x = Math.Round(Math.Max(xEndS, xEndN) + R(12, 18), 2);
            cityBlock++;
        }
        return city;
    }

    /// <summary>A terraced row from x0 along +x; returns where it ends.</summary>
    private double Row(Random rng, Func<double, double, double> R, double x0, double length, double y0, double y1,
        bool frontLow)
    {
        var x = x0;
        while (x - x0 < length)
        {
            var w = R(5, 9);
            var h = Math.Round(Math.Round(R(6, 21) / _heightStep) * _heightStep, 2); // neighbours sometimes differ by less than minEdge
            var x1 = Math.Round(x + w, 2);
            Parts.Add(new Box(x, y0, 0, x1, y1, h));
            Houses++;
            if (rng.NextDouble() < 0.3)
            {
                // Set-back tier: back from the front wall, sometimes narrower than the house.
                var setBack = R(1, 3);
                var side = rng.NextDouble() < 0.5 ? 0 : R(0.5, 1.5);
                var (ty0, ty1) = frontLow ? (Math.Round(y0 + setBack, 2), y1) : (y0, Math.Round(y1 - setBack, 2));
                Parts.Add(new Box(Math.Round(x + side, 2), ty0, h, Math.Round(x1 - side, 2), ty1, Math.Round(h + R(3, 6), 2)));
                Tiers++;
            }
            if (rng.NextDouble() < 0.03 && h > 5)
            {
                // Raised slab: 2 m over the house front, 3 m out over the street.
                var (sy0, sy1) = frontLow ? (Math.Round(y0 - 3, 2), Math.Round(y0 + 2, 2)) : (Math.Round(y1 - 2, 2), Math.Round(y1 + 3, 2));
                Parts.Add(new Box(Math.Round(x + 0.5, 2), sy0, 4.0, Math.Round(x1 - 0.5, 2), sy1, 4.4));
                Slabs++;
            }
            var r = rng.NextDouble();
            if (r < 0.12) { x = Math.Round(x1 + R(0.1, 0.9), 2); SmallGaps++; }
            else if (r < 0.17) x = Math.Round(x1 + R(1.5, 3), 2);
            else x = x1;
        }
        return x;
    }

    /// <summary>All parts' vertices, 8 per part, in part order (shared by both object splits).</summary>
    private Mesh Vertices()
    {
        var m = new Mesh();
        foreach (var b in Parts)
            m.Vertices.AddRange(new[]
            {
                b.Corner(b.X0, b.Y0, b.Z0), b.Corner(b.X1, b.Y0, b.Z0), b.Corner(b.X1, b.Y1, b.Z0), b.Corner(b.X0, b.Y1, b.Z0),
                b.Corner(b.X0, b.Y0, b.Z1), b.Corner(b.X1, b.Y0, b.Z1), b.Corner(b.X1, b.Y1, b.Z1), b.Corner(b.X0, b.Y1, b.Z1),
            });
        return m;
    }

    private static void AddBoxFaces(MeshObject o, int b)
    {
        o.Faces.Add(new Face(b + 0, b + 3, b + 2, b + 1));
        o.Faces.Add(new Face(b + 4, b + 5, b + 6, b + 7));
        o.Faces.Add(new Face(b + 0, b + 1, b + 5, b + 4));
        o.Faces.Add(new Face(b + 1, b + 2, b + 6, b + 5));
        o.Faces.Add(new Face(b + 2, b + 3, b + 7, b + 6));
        o.Faces.Add(new Face(b + 3, b + 0, b + 4, b + 7));
    }

    /// <summary>One object per part, named <c>B0001</c>, <c>B0002</c>, … (each its own block).</summary>
    public Mesh AsObjects(Vec3? offset = null)
    {
        var m = Vertices();
        for (var i = 0; i < Parts.Count; i++) AddBoxFaces(m.AddObject($"B{i + 1:0000}"), 8 * i);
        Shift(m, offset);
        return m;
    }

    /// <summary>Every part in a single object <paramref name="name"/>.</summary>
    public Mesh AsOneObject(string name = "city", Vec3? offset = null)
    {
        var m = Vertices();
        var o = m.AddObject(name);
        for (var i = 0; i < Parts.Count; i++) AddBoxFaces(o, 8 * i);
        Shift(m, offset);
        return m;
    }

    private static void Shift(Mesh m, Vec3? offset)
    {
        if (offset is not { } d) return;
        for (var i = 0; i < m.Vertices.Count; i++) m.Vertices[i] += d;
    }
}
