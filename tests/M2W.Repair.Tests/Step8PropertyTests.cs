using FsCheck;
using FsCheck.Xunit;
using M2W.Geometry;
using M2W.Geometry.Models;
using M2W.Repair.Planar;
using M2W.Repair.Union;
using static M2W.Repair.Tests.Step5GapTests;

namespace M2W.Repair.Tests;

/// <summary>
/// M4 properties on random sites that carry the faults together: blocks placed at random (overlapping, touching,
/// or a random gap below minGap from the previous block), stacked floors with a random ledge below or above minEdge,
/// blocks floating or sunk within groundSnap, objects turned inside out, duplicate vertices.
/// Block positions carry small per-block offsets (multiples of 0.0037 m / 0.0029 m), so no gap or ledge lands
/// within rounding of minGap or minEdge, where moving or rotating could legitimately flip the decision.
/// </summary>
public class Step8PropertyTests
{
    /// <summary>FsCheck keeps integers within its size (about ±100); three of them give millions of distinct sites.</summary>
    private static int Seed(int a, int b, int c) => unchecked(a * 1_000_003 + b * 7_919 + c);

    private static Mesh FaultySite(System.Random rnd, out double rotation)
    {
        var m = new Mesh();
        var n = rnd.Next(1, 5);
        double px = 0, py = 0, pw = 0;
        for (var b = 0; b < n; b++)
        {
            var w = 4 + rnd.Next(0, 1600) / 100.0;
            var d = 4 + rnd.Next(0, 1600) / 100.0;
            double x, y;
            if (b > 0 && rnd.NextDouble() < 0.5)
            {
                // Next to the previous block with a gap below minGap (0.02 .. 0.98 m).
                x = px + pw + 0.02 + rnd.Next(0, 97) / 100.0;
                y = py + rnd.Next(-500, 500) / 100.0;
            }
            else
            {
                x = rnd.Next(0, 4000) / 100.0;
                y = rnd.Next(0, 4000) / 100.0;
            }
            x += 0.0037 * b; y += 0.0029 * b;
            (px, py, pw) = (x, y, w);
            var floors = rnd.Next(1, 5);
            var fh = 3.0 + rnd.Next(0, 10) / 10.0;
            var dz = rnd.NextDouble() < 0.3 ? (rnd.Next(-40, 41) / 100.0) : 0; // floating / sunk within groundSnap
            var ledge = rnd.NextDouble() < 0.3 ? (rnd.NextDouble() < 0.6 ? 0.02 + rnd.Next(0, 19) / 100.0 + 0.0013 : 0.3 + rnd.Next(0, 20) / 100.0) : 0;
            for (var f = 0; f < floors; f++)
            {
                var grow = f == 0 && floors > 1 ? ledge : 0;
                var name = floors == 1 ? $"B{b + 1:00}" : $"B{b + 1:00}_F{f + 1:00}";
                AddPrism(m, name, Rect(x, y, x + w, y + d + grow), f * fh + dz, (f + 1) * fh + dz);
                var o = m.Objects[^1];
                if (rnd.NextDouble() < 0.2)
                    for (var i = 0; i < o.Faces.Count; i++) o.Faces[i] = new Face(o.Faces[i].Reverse().ToArray());
                if (rnd.NextDouble() < 0.2)
                {
                    var copy = new Dictionary<int, int>();
                    for (var i = 0; i < o.Faces.Count; i += 2)
                        o.Faces[i] = new Face(o.Faces[i].Select(v =>
                        {
                            if (!copy.TryGetValue(v, out var c)) { c = m.Vertices.Count; m.Vertices.Add(m.Vertices[v]); copy[v] = c; }
                            return c;
                        }).ToArray());
                }
            }
        }
        rotation = rnd.NextDouble() * 2 * Math.PI;
        return m;
    }

    private static Mesh Transform(Mesh m, double angle, Vec3 offset)
    {
        var c = Math.Cos(angle); var s = Math.Sin(angle);
        var r = new Mesh();
        r.Vertices.AddRange(m.Vertices.Select(v =>
            new Vec3(Math.Round(c * v.X - s * v.Y, 6), Math.Round(s * v.X + c * v.Y, 6), v.Z) + offset));
        foreach (var o in m.Objects) r.Objects.Add(new MeshObject(o.Name, o.Faces));
        return r;
    }

    private static (int shells, double volume, int euler) Measure(Mesh m)
    {
        var vol = TestMeshes.AssertClosedShells(m);
        var euler = 0;
        foreach (var o in m.Objects)
            euler += o.Faces.SelectMany(f => f).Distinct().Count() - MeshMeasures.EdgeUseCounts(o.Faces).Count + o.Faces.Count;
        return (m.Objects.Count, vol, euler);
    }

    /// <summary>Footprint outline segments of each shell (edges of its ground triangles used once).</summary>
    private static List<List<(P2 a, P2 b)>> Footprints(Mesh m)
    {
        var result = new List<List<(P2, P2)>>();
        foreach (var o in m.Objects)
        {
            var count = new Dictionary<(int, int), int>();
            foreach (var f in o.Faces)
            {
                if (!f.All(i => m.Vertices[i].Z == 0)) continue;
                for (var k = 0; k < 3; k++)
                {
                    int a = f[k], b = f[(k + 1) % 3];
                    var key = a < b ? (a, b) : (b, a);
                    count[key] = count.GetValueOrDefault(key) + 1;
                }
            }
            result.Add(count.Where(kv => kv.Value == 1).Select(kv =>
                (new P2(m.Vertices[kv.Key.Item1].X, m.Vertices[kv.Key.Item1].Y), new P2(m.Vertices[kv.Key.Item2].X, m.Vertices[kv.Key.Item2].Y))).ToList());
        }
        return result;
    }

    /// <summary>
    /// Any faulty site repairs to valid output: status repaired, closed outward shells, ground at 0, no edge shorter
    /// than minEdge, and no two shells closer than minGap.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Faulty_site_repairs_to_valid_output(int s1, int s2, int s3)
    {
        var rnd = new System.Random(Seed(s1, s2, s3));
        var site = Transform(FaultySite(rnd, out var angle), angle, Vec3.Zero);
        var outcome = TestMeshes.Repair(site);
        var mesh = outcome.Repaired!;
        TestMeshes.AssertClosedShells(mesh);
        var shortest = double.PositiveInfinity;
        foreach (var o in mesh.Objects)
            foreach (var f in o.Faces)
                for (var k = 0; k < 3; k++)
                    shortest = Math.Min(shortest, (mesh.Vertices[f[(k + 1) % 3]] - mesh.Vertices[f[k]]).Length);
        var minZ = mesh.Vertices.Min(v => v.Z);
        var feet = Footprints(mesh);
        var gap = double.PositiveInfinity;
        for (var i = 0; i < feet.Count; i++)
        for (var j = i + 1; j < feet.Count; j++)
            foreach (var (a, b) in feet[i])
                foreach (var (c, d) in feet[j])
                    gap = Math.Min(gap, GapCloser.SegmentDistance(a, b, c, d));
        var ok = outcome.Report.Status == RepairStatus.Repaired && minZ == 0 && shortest >= 0.25 - 1e-6 && gap >= 1.0 - 1e-6;
        return ok.Label($"status {outcome.Report.Status}, min z {minZ}, shortest {shortest}, gap {gap}, " +
                        $"warnings: {string.Join("; ", outcome.Report.Warnings.Where(w => !w.Contains("minAngleDeg")))}");
    }

    /// <summary>Moving to map coordinates and rotating a faulty site does not change shell count, volume or topology.</summary>
    [Property(MaxTest = 60)]
    public Property Moving_and_rotating_a_faulty_site_keeps_volume_and_topology(int s1, int s2, int s3)
    {
        var rnd = new System.Random(Seed(s1, s2, s3));
        var site = FaultySite(rnd, out var angle);
        var origin = new Vec3(530000 + rnd.Next(0, 1000), 180000 + rnd.Next(0, 1000), rnd.Next(0, 50));
        var a = TestMeshes.Repair(site);
        var b = TestMeshes.Repair(Transform(site, angle, origin), origin);
        var ma = Measure(a.Repaired!);
        var mb = Measure(b.Repaired!);
        return (a.Report.Status == RepairStatus.Repaired && b.Report.Status == RepairStatus.Repaired
                && ma.shells == mb.shells && ma.euler == mb.euler
                && Math.Abs(ma.volume - mb.volume) <= 1e-4 * Math.Max(1, ma.volume))
            .Label($"plain {ma} {a.Report.Status} vs moved {mb} {b.Report.Status}: {string.Join("; ", a.Report.Warnings.Concat(b.Report.Warnings).Where(w => !w.Contains("minAngleDeg")))}");
    }

    /// <summary>Repairing the repaired output of a faulty site again changes nothing.</summary>
    [Property(MaxTest = 60)]
    public Property Repair_of_a_faulty_site_is_idempotent(int s1, int s2, int s3)
    {
        var rnd = new System.Random(Seed(s1, s2, s3));
        var site = Transform(FaultySite(rnd, out var angle), angle, Vec3.Zero);
        var once = TestMeshes.Repair(site);
        var twice = TestMeshes.Repair(once.Repaired!);
        var m1 = Measure(once.Repaired!);
        var m2 = Measure(twice.Repaired!);
        var sameOps = !twice.Report.Operations.Any(o => o.Op is "closeGap" or "raiseThinRegion" or "alignJog" or "alignJogCut"
            or "cutBump" or "fillNotch" or "squareCorner" or "collapseShortEdges" or "groundSnap" or "fixOrientation");
        return (twice.Report.Status == RepairStatus.Repaired && m1.shells == m2.shells && m1.euler == m2.euler
                && Math.Abs(m1.volume - m2.volume) <= 1e-6 * Math.Max(1, m1.volume) && sameOps)
            .Label($"once {m1} vs twice {m2}; second-run ops: {string.Join(",", twice.Report.Operations.Select(o => o.Op))}");
    }
}
