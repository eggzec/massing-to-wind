using M2W.Geometry;
using M2W.Repair.Model;

namespace M2W.Repair.Steps;

/// <summary>
/// Moves whole blocks vertically so they stand on Z = 0. A block (all objects <c>B&lt;nn&gt;_F..</c> with the same
/// <c>B&lt;nn&gt;</c>) whose lowest point is within <c>groundSnap</c> of the ground, above or below, is translated by
/// minus that height. The move is rigid, so the block's height is unchanged. Blocks further away are left alone:
/// they may stand on another block.
/// </summary>
public static class GroundSnapper
{
    public sealed record Move(string Block, double Dz);

    /// <summary>Snaps in place and returns the moves made, in block-name order.</summary>
    public static List<Move> Snap(IReadOnlyList<Solid> solids, double groundSnap)
    {
        var moves = new List<Move>();
        foreach (var group in solids.GroupBy(s => s.Block).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var minZ = group.SelectMany(s => s.Vertices).Select(v => v.Z).DefaultIfEmpty(0).Min();
            if (minZ == 0 || Math.Abs(minZ) > groundSnap + 1e-9) continue;
            var shift = new Vec3(0, 0, -minZ);
            foreach (var s in group)
                for (var i = 0; i < s.Vertices.Count; i++)
                {
                    var v = s.Vertices[i] + shift;
                    // Land the lowest vertices exactly on the ground, not 1e-16 off it.
                    s.Vertices[i] = Math.Abs(v.Z) < 1e-9 ? v with { Z = 0 } : v;
                }
            moves.Add(new Move(group.Key, -minZ));
        }
        return moves;
    }
}
