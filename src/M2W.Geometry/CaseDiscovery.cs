using M2W.Geometry.Models;

namespace M2W.Geometry;

/// <summary>Finds case folders: every folder at any depth below a root that contains a <c>case.json</c>.</summary>
public static class CaseDiscovery
{
    /// <summary>
    /// Full paths of every folder under <paramref name="root"/> (including the root itself) that contains
    /// <c>case.json</c>. Folders whose name starts with '.' are skipped. The order is ordinal by the
    /// '/'-separated relative path, so it is the same on every platform.
    /// </summary>
    public static IReadOnlyList<string> FindCaseDirs(string root)
    {
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Cases folder not found: {root}");

        var found = new List<string>();
        Walk(full, found);
        return found
            .OrderBy(d => RelativePath(full, d), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Path of <paramref name="dir"/> relative to <paramref name="root"/>, with '/' separators ("." for the root).</summary>
    public static string RelativePath(string root, string dir) =>
        Path.GetRelativePath(root, dir).Replace('\\', '/');

    private static void Walk(string dir, List<string> found)
    {
        if (File.Exists(Path.Combine(dir, CaseFiles.Case))) found.Add(dir);
        IEnumerable<string> subdirs;
        try
        {
            subdirs = Directory.EnumerateDirectories(dir);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        foreach (var sub in subdirs)
        {
            var info = new DirectoryInfo(sub);
            if (info.Name.StartsWith('.')) continue;
            if (info.LinkTarget is not null) continue; // do not follow links (avoids cycles)
            Walk(sub, found);
        }
    }
}
