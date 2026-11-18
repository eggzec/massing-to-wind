using M2W.Checker;
using M2W.Geometry;
using Xunit;
using static M2W.Checker.Tests.Kit;

namespace M2W.Checker.Tests;

/// <summary>mustMerge / mustStaySeparate: input blocks are located in the repaired shells by position.</summary>
public class BlockGroupTests
{
    private static readonly Vec3 MapOrigin = new(530000, 180000, 0);

    /// <summary>Input: block B01 as three stacked floors, block B02 whole, <paramref name="gap"/> apart, in map coordinates.</summary>
    private static Mesh Input(double gap)
    {
        var m = new Mesh();
        for (var f = 0; f < 3; f++)
            AddBox(m, m.AddObject($"B01_F{f + 1:00}"), 0, 0, 3 * f, 20, 10, 3 * (f + 1));
        AddBox(m, m.AddObject("B02"), 20 + gap, 0, 0, 30 + gap, 10, 9);
        return Moved(m, MapOrigin);
    }

    private static readonly string[][] Pair = { new[] { "B01", "B02" } };

    [Fact]
    public void Block_names_map_to_blocks()
    {
        Assert.Equal("B01", BlockLocator.BlockOf("B01_F03"));
        Assert.Equal("B02", BlockLocator.BlockOf("B02"));
        Assert.Null(BlockLocator.BlockOf("shell_000"));
        Assert.Null(BlockLocator.BlockOf(""));
    }

    [Fact]
    public void Must_merge_passes_when_the_gap_is_closed_into_one_shell()
    {
        var repaired = Boxes((0, 0, 0, 30.3, 10, 9));
        var r = Grade(repaired, Expect(new[] { BoxShell(30.3, 10, 9) }, merge: Pair), Input(0.3), Spec(MapOrigin));
        Assert.True(r.Pass, r.Failed());
    }

    [Fact]
    public void Must_merge_fails_when_the_blocks_stay_apart()
    {
        var repaired = Boxes((0, 0, 0, 20, 10, 9), (20.3, 0, 0, 30.3, 10, 9));
        var r = Grade(repaired, Expect(new[] { BoxShell(30.3, 10, 9) }, merge: Pair), Input(0.3), Spec(MapOrigin));
        var e = r.Get(CheckNames.MustMerge);
        Assert.False(e.Pass);
        Assert.Equal(1, e.Value);
        Assert.Contains("B01 in shell_000, B02 in shell_001", e.Detail());
    }

    [Fact]
    public void Must_merge_fails_for_touching_shells_that_were_not_joined()
    {
        // Walls coincide (touchingWalls) but the solver left two shells: points on the shared wall cannot vote.
        var repaired = Boxes((0, 0, 0, 20, 10, 9), (20, 0, 0, 30, 10, 9));
        var r = Grade(repaired, Expect(new[] { BoxShell(30, 10, 9) }, merge: Pair), Input(0), Spec(MapOrigin));
        Assert.False(r.Get(CheckNames.MustMerge).Pass, r.Get(CheckNames.MustMerge).Detail());

        // Same, with the second block listed first in the file, so the tie-break cannot help it either.
        var swapped = Boxes((20, 0, 0, 30, 10, 9), (0, 0, 0, 20, 10, 9));
        Assert.False(Grade(swapped, Expect(new[] { BoxShell(30, 10, 9) }, merge: Pair), Input(0), Spec(MapOrigin)).Get(CheckNames.MustMerge).Pass);
    }

    [Fact]
    public void Must_stay_separate_passes_when_the_blocks_stay_apart()
    {
        var repaired = Boxes((0, 0, 0, 20, 10, 9), (21.1, 0, 0, 31.1, 10, 9));
        var r = Grade(repaired, Expect(new[] { BoxShell(20, 10, 9), BoxShell(10, 10, 9) }, apart: Pair), Input(1.1), Spec(MapOrigin));
        Assert.True(r.Pass, r.Failed());
    }

    [Fact]
    public void Must_stay_separate_fails_when_the_blocks_were_merged()
    {
        var repaired = Boxes((0, 0, 0, 31.1, 10, 9));
        var e = Grade(repaired, Expect(new[] { BoxShell(20, 10, 9), BoxShell(10, 10, 9) }, apart: Pair), Input(1.1), Spec(MapOrigin))
            .Get(CheckNames.MustStaySeparate);
        Assert.False(e.Pass);
        Assert.Equal(1, e.Value);
    }

    [Fact]
    public void A_block_moved_to_the_ground_is_still_found()
    {
        // B02 floated 0.4 m in the input (floatingBlock); the solver snapped it down.
        var input = new Mesh();
        AddBox(input, input.AddObject("B01"), 0, 0, 0, 20, 10, 9);
        AddBox(input, input.AddObject("B02"), 25, 0, 0.4, 35, 10, 9.4);
        var repaired = Boxes((0, 0, 0, 20, 10, 9), (25, 0, 0, 35, 10, 9));
        var r = Grade(repaired, Expect(new[] { BoxShell(20, 10, 9), BoxShell(10, 10, 9) }, apart: Pair), input);
        Assert.True(r.Pass, r.Failed());
    }

    [Fact]
    public void A_block_the_solver_deleted_is_not_found()
    {
        var input = new Mesh();
        AddBox(input, input.AddObject("B01"), 0, 0, 0, 20, 10, 9);
        AddBox(input, input.AddObject("B02"), 60, 0, 0, 70, 10, 9);
        var repaired = Boxes((0, 0, 0, 20, 10, 9));
        var e = Grade(repaired, Expect(new[] { BoxShell(20, 10, 9) }, apart: Pair), input).Get(CheckNames.MustStaySeparate);
        Assert.False(e.Pass);
        Assert.Contains("B02 in not found", e.Detail());
    }

    [Fact]
    public void A_group_naming_a_block_that_is_not_in_the_input_fails()
    {
        var e = Grade(Boxes((0, 0, 0, 30.3, 10, 9)), Expect(new[] { BoxShell(30.3, 10, 9) }, merge: new[] { new[] { "B01", "B09" } }), Input(0.3), Spec(MapOrigin))
            .Get(CheckNames.MustMerge);
        Assert.False(e.Pass);
    }

    [Fact]
    public void Groups_fail_if_the_input_cannot_be_read()
    {
        var e = Grade(Boxes((0, 0, 0, 30.3, 10, 9)), Expect(new[] { BoxShell(30.3, 10, 9) }, merge: Pair), input: null).Get(CheckNames.MustMerge);
        Assert.False(e.Pass);
        Assert.Contains("cannot locate blocks", e.Detail());
    }
}
