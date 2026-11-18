using System.Globalization;
using System.Text;

namespace M2W.Geometry;

/// <summary>
/// Writes Wavefront OBJ as defined in AGREEMENT.md: UTF-8 without BOM, LF line endings, 6 decimal places,
/// invariant culture, 1-based indices. All vertices first, then each object (<c>o name</c>) with its faces.
/// Output is a pure function of the mesh (and optional comments), so it is byte-identical for identical input.
/// An unnamed first object with no faces produces no lines (OBJ has no way to express it), so it is not read back.
/// </summary>
public static class ObjWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteFile(Mesh mesh, string path, IEnumerable<string>? comments = null)
    {
        File.WriteAllText(path, WriteString(mesh, comments), Utf8NoBom);
    }

    public static string WriteString(Mesh mesh, IEnumerable<string>? comments = null)
    {
        mesh.Validate();
        var sb = new StringBuilder(mesh.Vertices.Count * 40 + mesh.FaceCount * 20);

        if (comments is not null)
            foreach (var c in comments)
                foreach (var line in c.Replace("\r", "").Split('\n'))
                    sb.Append("# ").Append(line).Append('\n');

        foreach (var v in mesh.Vertices)
            sb.Append("v ").Append(Format(v.X)).Append(' ').Append(Format(v.Y)).Append(' ').Append(Format(v.Z)).Append('\n');

        foreach (var obj in mesh.Objects)
        {
            if (obj.Name.Length > 0)
            {
                if (obj.Name.Contains('\n') || obj.Name.Contains('\r') || obj.Name.Contains('#'))
                    throw new ArgumentException($"Object name '{obj.Name}' cannot contain a newline or '#'.");
                sb.Append("o ").Append(obj.Name).Append('\n');
            }
            foreach (var f in obj.Faces)
            {
                sb.Append('f');
                foreach (var i in f) sb.Append(' ').Append((i + 1).ToString(CultureInfo.InvariantCulture));
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>Formats a coordinate with 6 decimals; negative zero (including tiny negatives) is written as 0.</summary>
    public static string Format(double value)
    {
        var s = value.ToString("F6", CultureInfo.InvariantCulture);
        return s == "-0.000000" ? "0.000000" : s;
    }
}
