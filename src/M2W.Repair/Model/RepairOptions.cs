namespace M2W.Repair.Model;

/// <summary>
/// Internal switches for one repair, passed down explicitly so that repairs running side by side (tests in
/// parallel) never affect each other (D-048). The defaults are what <c>m2w repair</c> uses; no option changes the
/// output, only how it is computed.
/// </summary>
internal sealed record RepairOptions
{
    public static readonly RepairOptions Default = new();

    /// <summary>Gap closing scans every roof level over the whole plan instead of only where walls of different
    /// parts come close. Slower; tests use it to check that the fast scan finds the same gaps.</summary>
    public bool FullGapScan { get; init; }
}
