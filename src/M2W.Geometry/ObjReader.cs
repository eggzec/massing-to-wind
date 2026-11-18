using System.Globalization;

namespace M2W.Geometry;

/// <summary>Thrown when an OBJ file breaks the rules in AGREEMENT.md. Carries the 1-based line number.</summary>
public sealed class ObjFormatException : FormatException
{
    public ObjFormatException(int line, string message)
        : base(line > 0 ? $"OBJ line {line}: {message}" : $"OBJ: {message}")
    {
        Line = line;
    }

    public int Line { get; }
}

/// <summary>
/// Reads Wavefront OBJ as defined in AGREEMENT.md.
/// <list type="bullet">
/// <item>Allowed: <c>v x y z</c>, <c>f i j k …</c> (1-based; negative = relative; <c>i/t/n</c> keeps <c>i</c>),
/// <c>o name</c>, <c># comment</c>, blank lines.</item>
/// <item>Ignored: <c>vn</c>, <c>vt</c>, <c>usemtl</c>, <c>mtllib</c>, <c>g</c>, <c>s</c>.</item>
/// <item>Anything else is an error. CR before LF is tolerated on read.</item>
/// </list>
/// </summary>
public static class ObjReader
{
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal) { "vn", "vt", "usemtl", "mtllib", "g", "s" };

    public static Mesh ReadFile(string path)
    {
        using var reader = new StreamReader(path, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Read(reader);
    }

    public static Mesh ReadString(string text) => Read(new StringReader(text));

    public static Mesh Read(TextReader reader)
    {
        var mesh = new Mesh();
        MeshObject? current = null;
        var lineNo = 0;
        // Positive indices may refer to vertices defined later in the file, so they are checked at the end.
        var maxIndexLine = 0;
        var maxIndex = -1;

        while (reader.ReadLine() is { } raw)
        {
            lineNo++;
            var line = raw;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (line.Length == 0) continue;

            var space = line.IndexOfAny(new[] { ' ', '\t' });
            var keyword = space < 0 ? line : line[..space];
            var rest = space < 0 ? "" : line[(space + 1)..].Trim();

            switch (keyword)
            {
                case "v":
                    mesh.Vertices.Add(ParseVertex(rest, lineNo));
                    break;

                case "f":
                {
                    var tokens = Split(rest);
                    if (tokens.Length < 3) throw new ObjFormatException(lineNo, $"a face needs at least 3 vertices, got {tokens.Length}.");
                    var indices = new int[tokens.Length];
                    for (var k = 0; k < tokens.Length; k++)
                    {
                        var idx = ParseIndex(tokens[k], mesh.Vertices.Count, lineNo);
                        indices[k] = idx;
                        if (idx > maxIndex) { maxIndex = idx; maxIndexLine = lineNo; }
                    }
                    current ??= mesh.AddObject("");
                    current.Faces.Add(new Face(indices));
                    break;
                }

                case "o":
                    if (rest.Length == 0) throw new ObjFormatException(lineNo, "'o' needs a name.");
                    current = mesh.AddObject(rest);
                    break;

                default:
                    if (!Ignored.Contains(keyword))
                        throw new ObjFormatException(lineNo, $"unsupported keyword '{keyword}'.");
                    break;
            }
        }

        if (maxIndex >= mesh.Vertices.Count)
            throw new ObjFormatException(maxIndexLine, $"face refers to vertex {maxIndex + 1} but the file has {mesh.Vertices.Count} vertices.");

        return mesh;
    }

    private static string[] Split(string s) =>
        s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    private static Vec3 ParseVertex(string rest, int lineNo)
    {
        var t = Split(rest);
        // Extra values (w or vertex colours from some exporters) are ignored; fewer than 3 is an error.
        if (t.Length < 3) throw new ObjFormatException(lineNo, $"a vertex needs 3 coordinates, got {t.Length}.");
        return new Vec3(ParseDouble(t[0], lineNo), ParseDouble(t[1], lineNo), ParseDouble(t[2], lineNo));
    }

    private static double ParseDouble(string s, int lineNo)
    {
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d))
            throw new ObjFormatException(lineNo, $"'{s}' is not a finite number.");
        return d;
    }

    private static int ParseIndex(string token, int vertexCountSoFar, int lineNo)
    {
        var slash = token.IndexOf('/');
        var s = slash >= 0 ? token[..slash] : token;
        if (!int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i))
            throw new ObjFormatException(lineNo, $"'{token}' is not a vertex index.");
        if (i == 0) throw new ObjFormatException(lineNo, "vertex index 0 is invalid (OBJ indices are 1-based).");
        if (i > 0) return i - 1;
        var resolved = vertexCountSoFar + i; // -1 is the last vertex defined so far
        if (resolved < 0) throw new ObjFormatException(lineNo, $"relative index {i} points before the first vertex.");
        return resolved;
    }
}
