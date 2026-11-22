using M2W.Geometry.Models;
using Xunit;

namespace M2W.Generator.Tests;

/// <summary>The roof-step rule of AGREEMENT.md (D-037) on small hand-made models.</summary>
public class RoofFlushTests
{
    private static Block Box(string name, double u0, double v0, double u1, double v1, double height) =>
        new("bar", new[] { new Rect(u0, v0, u1, v1) }, new[] { height }) { Name = name };

    [Fact]
    public void A_step_below_minEdge_is_raised_to_the_higher_roof()
    {
        var hf = HeightField.Build(new[] { Box("B01", 0, 0, 20, 10, 13.6), Box("B02", 20, 0, 30, 10, 13.8) });
        var s = Assert.Single(hf.FlushRoofSteps(0.25).Shells());
        Assert.Equal(30 * 10 * 13.8, s.Volume, 6);
        Assert.Equal(13.8, s.MaxHeight, 9);
        Assert.Null(hf.FlushRoofSteps(0.25).FindRoofStep(0.25));
        Assert.NotNull(hf.FindRoofStep(0.25));
    }

    [Fact]
    public void A_step_at_or_above_minEdge_is_kept()
    {
        foreach (var high in new[] { 13.85, 13.9 })
        {
            var hf = HeightField.Build(new[] { Box("B01", 0, 0, 20, 10, 13.6), Box("B02", 20, 0, 30, 10, high) });
            Assert.True(hf.FlushRoofSteps(0.25).SameRoofs(hf), $"{high}");
        }
    }

    [Fact]
    public void Corner_contact_counts_as_touching()
    {
        // A (10 m) and B (10.2 m) meet only at the point (10, 10); the other two quadrants are 20 m.
        var blocks = new[]
        {
            Box("B01", 0, 0, 10, 10, 10), Box("B02", 10, 10, 20, 20, 10.2),
            Box("B03", 0, 10, 10, 20, 20), Box("B04", 10, 0, 20, 10, 20),
        };
        var flushed = HeightField.Build(blocks).FlushRoofSteps(0.25);
        Assert.Equal(10.2, flushed.Top[0, 0], 9);
        Assert.Equal(100 * (10.2 + 10.2 + 20 + 20), flushed.TotalVolume(), 6);
    }

    [Fact]
    public void Raising_repeats_until_no_step_is_left()
    {
        // A (10) touches B (10.2) and D (10.3). A-D is 0.3 apart at first, but once A is raised to 10.2 it is 0.1.
        var blocks = new[] { Box("B01", 0, 0, 10, 10, 10), Box("B02", 10, 0, 20, 10, 10.2), Box("B03", 0, 10, 10, 20, 10.3) };
        var flushed = HeightField.Build(blocks).FlushRoofSteps(0.25);
        Assert.Equal(300 * 10.3, flushed.TotalVolume(), 6);
        Assert.Null(flushed.FindRoofStep(0.25));
    }

    [Fact]
    public void A_chain_of_steps_can_depend_on_the_order_so_the_generator_rejects_it()
    {
        // 10 | 10.2 | 10.4 in a row: raising everything at once gives 10.4 everywhere; raising the highest first
        // lifts the middle to 10.4 and leaves a 0.4 m step at the first. The agreement does not decide, so such
        // layouts are redrawn (D-038).
        var blocks = new[] { Box("B01", 0, 0, 10, 10, 10), Box("B02", 10, 0, 20, 10, 10.2), Box("B03", 20, 0, 30, 10, 10.4) };
        var hf = HeightField.Build(blocks);
        var all = hf.FlushRoofSteps(0.25, HeightField.FlushOrder.Simultaneous);
        var high = hf.FlushRoofSteps(0.25, HeightField.FlushOrder.HighestFirst);
        Assert.Equal(300 * 10.4, all.TotalVolume(), 6);
        Assert.Equal(100 * (10 + 10.4 + 10.4), high.TotalVolume(), 6);
        Assert.False(all.SameRoofs(high));
    }
}

/// <summary>The roof-step rule on every generated case, and the roofStep fault.</summary>
public class RoofStepCaseTests
{
    public static IEnumerable<object[]> CaseIndexes() => GroundTruthTests.CaseIndexes();

    /// <summary>
    /// Self-check written from the AGREEMENT text alone: in the height field the expected answer is computed from,
    /// no two roofed cells that share an edge or a corner differ by more than 0 and less than the case's minEdge.
    /// (Before R1 this failed, e.g. crossing bars at 13.6 and 13.8 m.)
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Expected_roofs_have_no_touching_step_below_minEdge(int index)
    {
        var c = Cases.Seed1[index];
        var hf = c.ExpectedField;
        var minEdge = c.Case.Tolerances.MinEdge;
        Assert.Equal(minEdge, c.Expected.Checks.MinEdge_m);
        for (var i = 0; i < hf.Nx; i++)
            for (var j = 0; j < hf.Ny; j++)
                for (var di = -1; di <= 1; di++)
                    for (var dj = -1; dj <= 1; dj++)
                    {
                        int k = i + di, l = j + dj;
                        if ((di == 0 && dj == 0) || k < 0 || l < 0 || k >= hf.Nx || l >= hf.Ny) continue;
                        double a = hf.Top[i, j], b = hf.Top[k, l];
                        if (a <= 0 || b <= 0) continue;
                        var d = Math.Abs(a - b);
                        Assert.False(d > 1e-9 && d < minEdge - 1e-9,
                            $"{c.CaseId}: roofs {a} and {b} touch at cells ({i},{j}) and ({k},{l}): step {d} < minEdge {minEdge}");
                        // No step sits so close to minEdge that rounding could decide it.
                        Assert.False(Math.Abs(d - minEdge) < CaseBuilder.RoofStepBand * minEdge, $"{c.CaseId}: step {d} at minEdge");
                    }
        // expected.json is computed from this field.
        Assert.Equal(hf.TotalVolume(), c.Expected.Expected.Shells.Sum(s => s.Volume_m3), 4);
    }

    private static List<BuiltCase> Single() =>
        Cases.Seed1.Where(c => c.Recipe.Kind == Recipe.SingleFault && c.Recipe.Has("roofStep")).ToList();

    [Fact]
    public void RoofStep_cases_cover_both_sides_of_minEdge()
    {
        var single = Single();
        Assert.True(single.Count >= 20);
        var ratios = single.Select(c => Math.Round(Group(c).Step / c.Case.Tolerances.MinEdge, 2)).ToList();
        foreach (var r in new[] { 0.4, 0.8, 0.96, 1.04, 1.5 }) Assert.True(ratios.Count(x => x == r) >= 4, $"ratio {r}");
        Assert.True(ratios.Count(x => x < 1) >= 10 && ratios.Count(x => x > 1) >= 8);
        foreach (var k in new[] { "touching", "overlap", "tier" })
            Assert.Contains(single, c => Group(c).Variant.StartsWith(k, StringComparison.Ordinal));
        Assert.Contains(single, c => c.Case.Tolerances.MinEdge != Tolerances.DefaultMinEdge);
        Assert.True(Cases.Seed1.Count(c => c.Recipe.Kind == Recipe.Combined && c.Recipe.Has("roofStep")) >= 3);
        Assert.Contains(Cases.Seed1, c => c.Recipe.Kind == Recipe.Combined && c.Recipe.Has("roofStep")
            && Group(c).Step < c.Case.Tolerances.MinEdge);
    }

    private static Group Group(BuiltCase c) => Assert.Single(c.Groups, g => g.Relation == Relation.RoofStep);

    /// <summary>The designed model has the step; the expected answer raises the lower roof by exactly the step
    /// (below minEdge) or keeps it (above).</summary>
    [Fact]
    public void RoofStep_answer_raises_the_lower_roof_only_below_minEdge()
    {
        foreach (var c in Cases.Seed1.Where(c => c.Recipe.Has("roofStep")))
        {
            var g = Group(c);
            var minEdge = c.Case.Tolerances.MinEdge;
            Assert.Equal(g.Roofs.High - g.Roofs.Low, g.Step, 6);
            Assert.Equal(new[] { Math.Round(g.Step, 6) }, HeightField.Build(g.Clean).TouchingSteps());

            var design = HeightField.Build(c.ExpectedBlocks);
            var flushed = c.ExpectedField;
            var name = g.Clean[0].Name;
            var shell = flushed.Shells().Single(s => s.Blocks.Contains(name));
            var before = design.Shells().Single(s => s.Blocks.Contains(name));
            Assert.Equal(g.Clean.Select(b => b.Name).OrderBy(n => n, StringComparer.Ordinal), shell.Blocks);
            if (g.Step < minEdge)
            {
                double lowArea = 0;
                for (var i = 0; i < design.Nx; i++)
                    for (var j = 0; j < design.Ny; j++)
                        if (design.Comp[i, j] == before.Component && Math.Abs(design.Top[i, j] - g.Roofs.Low) < 1e-9) lowArea += design.CellArea(i, j);
                Assert.True(lowArea > 0, c.CaseId);
                Assert.Equal(before.Volume + g.Step * lowArea, shell.Volume, 4);
            }
            else Assert.Equal(before.Volume, shell.Volume, 6);
            Assert.Equal(before.MaxHeight, shell.MaxHeight, 9);
        }
    }

    /// <summary>The low roof deck of a "tier" roofStep is in the written input (its walls are shorter than minEdge),
    /// and is written as polygons so it brings no sliver triangles with it.</summary>
    [Fact]
    public void Tier_roof_decks_show_in_the_input()
    {
        var tiers = Cases.Seed1.Where(c => c.Recipe.Has("roofStep") && Group(c).Variant.StartsWith("tier", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(tiers);
        foreach (var c in tiers)
        {
            Assert.Equal(FaceStyle.Polygons, c.Style);
            var g = Group(c);
            var zs = c.Input.Vertices.Select(v => Math.Round(v.Z - c.Case.Origin.Z, 3)).Distinct().ToHashSet();
            // Vertical moves (floating/sunk) shift every z of a block, so compare differences, not levels.
            Assert.Contains(zs, z => zs.Contains(Math.Round(z + g.Step, 3)));
        }
    }
}
