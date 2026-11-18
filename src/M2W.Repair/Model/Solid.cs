using M2W.Geometry;

namespace M2W.Repair.Model;

/// <summary>A triangle by 0-based vertex indices into its <see cref="Solid"/>. Counter-clockwise seen from outside.</summary>
public readonly record struct Tri(int A, int B, int C)
{
    public Tri Reversed() => new(A, C, B);

    public int this[int k] => k switch { 0 => A, 1 => B, _ => C };

    /// <summary>The same triangle rotated so the smallest index comes first (orientation kept).</summary>
    public Tri Canonical()
    {
        if (A <= B && A <= C) return this;
        if (B <= A && B <= C) return new Tri(B, C, A);
        return new Tri(C, A, B);
    }

    /// <summary>Key that ignores orientation and rotation (sorted indices).</summary>
    public (int, int, int) SortedKey()
    {
        int a = A, b = B, c = C;
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return (a, b, c);
    }
}

/// <summary>
/// One input object (<c>o</c> line) as the solver works on it: its own vertex list, so steps on one object never move
/// another object's vertices, and its faces as polygons (input) or triangles (after triangulation).
/// </summary>
public sealed class Solid
{
    public Solid(string name, string block)
    {
        Name = name;
        Block = block;
    }

    /// <summary>The input object name, e.g. <c>B01_F03</c>.</summary>
    public string Name { get; }

    /// <summary>The block the object belongs to: <c>B01</c> for <c>B01_F03</c>, otherwise the object name.</summary>
    public string Block { get; }

    public List<Vec3> Vertices { get; } = new();

    /// <summary>Input polygons (index lists). Emptied by triangulation.</summary>
    public List<int[]> Polygons { get; } = new();

    public List<Tri> Triangles { get; } = new();

    /// <summary>Block name from an object name: the part before <c>_F</c> if the name follows <c>B&lt;block&gt;_F&lt;floor&gt;</c>.</summary>
    public static string BlockOf(string objectName)
    {
        var i = objectName.IndexOf("_F", StringComparison.Ordinal);
        if (i > 1 && objectName[0] == 'B' && i + 2 < objectName.Length && objectName.AsSpan(i + 2).IndexOfAnyExceptInRange('0', '9') < 0)
            return objectName[..i];
        return objectName;
    }
}
