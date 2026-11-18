namespace M2W.Geometry.Tests;

internal static class RepoPaths
{
    /// <summary>The repository root: the first folder above the test binaries that holds AGREEMENT.md.</summary>
    public static string Root { get; } = FindRoot();

    public static string Example0007 => Path.Combine(Root, "cases", "_example", "case_0007");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGREEMENT.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (AGREEMENT.md) not found.");
    }
}
