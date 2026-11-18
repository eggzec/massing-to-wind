using M2W.Geometry;

namespace M2W.Generator;

/// <summary>
/// Fault injectors that work on an emitted object's faces, the way exporters damage a clean model. None of them
/// changes the enclosed solid: the repaired answer is still the clean geometry. Faces in <c>touched</c> were changed
/// by an earlier injector and are left alone so faults do not stack on one face by accident.
/// </summary>
public static class MeshFaults
{
    private static Vec3 Normal(ObjMesh m, int[] f)
    {
        // Newell's method: robust for planar polygons with collinear vertices.
        double x = 0, y = 0, z = 0;
        for (var k = 0; k < f.Length; k++)
        {
            var a = m.V[f[k]];
            var b = m.V[f[(k + 1) % f.Length]];
            x += (a.Y - b.Y) * (a.Z + b.Z);
            y += (a.Z - b.Z) * (a.X + b.X);
            z += (a.X - b.X) * (a.Y + b.Y);
        }
        var n = new Vec3(x, y, z);
        return n / n.Length;
    }

    /// <summary>Directed edge (a, b) → face index that contains it.</summary>
    private static Dictionary<(int, int), int> DirectedEdges(ObjMesh m)
    {
        var map = new Dictionary<(int, int), int>();
        for (var fi = 0; fi < m.F.Count; fi++)
        {
            var f = m.F[fi];
            for (var k = 0; k < f.Length; k++) map[(f[k], f[(k + 1) % f.Length])] = fi;
        }
        return map;
    }

    private static int[] InsertAfter(int[] f, int a, int b, int p)
    {
        for (var k = 0; k < f.Length; k++)
            if (f[k] == a && f[(k + 1) % f.Length] == b)
            {
                var list = f.ToList();
                list.Insert(k + 1, p);
                return list.ToArray();
            }
        throw new InvalidOperationException("Edge not found in face.");
    }

    /// <summary>Replaces face <paramref name="fi"/> by <paramref name="poly"/>; for triangle style the polygon is
    /// fanned from vertex <paramref name="apex"/> (which must be on it). Returns the indices of the new faces.</summary>
    private static List<int> Replace(ObjMesh m, int fi, int[] poly, int apex, FaceStyle style)
    {
        if (style == FaceStyle.Polygons || poly.Length == 3)
        {
            m.F[fi] = poly;
            return new List<int> { fi };
        }
        var s = Array.IndexOf(poly, apex);
        var rot = poly.Skip(s).Concat(poly.Take(s)).ToArray(); // apex first
        var made = new List<int>();
        for (var k = 1; k + 1 < rot.Length; k++)
        {
            var tri = new[] { rot[0], rot[k], rot[k + 1] };
            if (k == 1) { m.F[fi] = tri; made.Add(fi); }
            else { m.F.Add(tri); made.Add(m.F.Count - 1); }
        }
        return made;
    }

    private static bool PickEdge(ObjMesh m, Rng rng, HashSet<int> touched, out int fi, out int gi, out int a, out int b)
    {
        var edges = DirectedEdges(m);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            fi = rng.Int(0, m.F.Count);
            if (touched.Contains(fi)) continue;
            var f = m.F[fi];
            var k = rng.Int(0, f.Length);
            a = f[k];
            b = f[(k + 1) % f.Length];
            if (!edges.TryGetValue((b, a), out gi) || gi == fi || touched.Contains(gi)) continue;
            if (Vec3.Distance(m.V[a], m.V[b]) < 2.0) continue;
            return true;
        }
        fi = gi = a = b = -1;
        return false;
    }

    /// <summary>
    /// Sliver: a thin triangle (a, b, p) along a shared edge, with p a hair inside the face (or exactly on the edge
    /// when <paramref name="degenerate"/>, a zero-area triangle). The face is re-routed through p, so the surface stays
    /// closed and the solid is unchanged. The sliver's smallest angle is well below <paramref name="minAngleDeg"/>.
    /// </summary>
    public static int InsertSlivers(ObjMesh m, Rng rng, int count, double minAngleDeg, bool degenerate, FaceStyle style, HashSet<int> touched)
    {
        var made = 0;
        for (var n = 0; n < count; n++)
        {
            if (!PickEdge(m, rng, touched, out var fi, out var gi, out var a, out var b)) break;
            var pa = m.V[a];
            var pb = m.V[b];
            var len = Vec3.Distance(pa, pb);
            var t = rng.Step(0.3, 0.7, 0.01);
            var inward = Vec3.Cross(Normal(m, m.F[fi]), pb - pa);
            inward /= inward.Length;
            var limit = 0.3 * (minAngleDeg * Math.PI / 180) * Math.Min(t, 1 - t) * len; // angle ≈ atan(0.3 θ) < θ
            var delta = degenerate ? 0 : Math.Round(Math.Min(0.02, rng.Range(0.3, 1.0) * limit), 4);
            if (!degenerate && delta < 0.0005) delta = 0.0005;
            var p = m.V.Count;
            m.V.Add(pa + (pb - pa) * t + inward * delta);
            var newF = Replace(m, fi, InsertAfter(m.F[fi], a, b, p), p, style);
            m.F.Add(new[] { a, b, p });
            touched.Add(gi);
            touched.Add(m.F.Count - 1);
            foreach (var x in newF) touched.Add(x);
            made++;
        }
        return made;
    }

    /// <summary>
    /// Short edge: an extra vertex on a shared edge, <paramref name="length"/> from one end, inserted into both faces
    /// (exporters leave these where they split edges). The geometry is unchanged; the new edge is shorter than minEdge.
    /// </summary>
    public static int InsertShortEdges(ObjMesh m, Rng rng, int count, Func<double> length, FaceStyle style, HashSet<int> touched)
    {
        var made = 0;
        for (var n = 0; n < count; n++)
        {
            if (!PickEdge(m, rng, touched, out var fi, out var gi, out var a, out var b)) break;
            var pa = m.V[a];
            var pb = m.V[b];
            var eps = length();
            var p = m.V.Count;
            m.V.Add(pa + (pb - pa) * (eps / Vec3.Distance(pa, pb)));
            var f1 = Replace(m, fi, InsertAfter(m.F[fi], a, b, p), p, style);
            var f2 = Replace(m, gi, InsertAfter(m.F[gi], b, a, p), p, style);
            foreach (var x in f1.Concat(f2)) touched.Add(x);
            made++;
        }
        return made;
    }

    /// <summary>Open surface: deletes up to <paramref name="count"/> faces that share no vertex with each other or with a
    /// touched face, so each hole is one planar loop. Returns how many were deleted.</summary>
    public static int DeleteFaces(ObjMesh m, Rng rng, int count, HashSet<int> touched)
    {
        var blocked = new HashSet<int>();
        foreach (var t in touched) foreach (var v in m.F[t]) blocked.Add(v);
        var order = Enumerable.Range(0, m.F.Count).ToList();
        rng.Shuffle(order);
        var chosen = new List<int>();
        foreach (var fi in order)
        {
            if (chosen.Count == count) break;
            if (touched.Contains(fi) || m.F[fi].Any(blocked.Contains)) continue;
            chosen.Add(fi);
            foreach (var v in m.F[fi]) blocked.Add(v);
        }
        RemoveFaces(m, chosen, touched);
        return chosen.Count;
    }

    /// <summary>Open surface: deletes every downward face at the object's lowest level (exports without a ground face).</summary>
    public static int DeleteBottom(ObjMesh m, HashSet<int> touched)
    {
        var zMin = m.V.Min(v => v.Z);
        var chosen = new List<int>();
        for (var fi = 0; fi < m.F.Count; fi++)
        {
            var f = m.F[fi];
            if (touched.Contains(fi)) continue;
            if (f.All(i => m.V[i].Z == zMin) && Normal(m, f).Z < -0.99) chosen.Add(fi);
        }
        RemoveFaces(m, chosen, touched);
        return chosen.Count;
    }

    private static void RemoveFaces(ObjMesh m, List<int> chosen, HashSet<int> touched)
    {
        var set = chosen.ToHashSet();
        var map = new Dictionary<int, int>();
        var kept = new List<int[]>();
        for (var fi = 0; fi < m.F.Count; fi++)
        {
            if (set.Contains(fi)) continue;
            map[fi] = kept.Count;
            kept.Add(m.F[fi]);
        }
        m.F.Clear();
        m.F.AddRange(kept);
        var newTouched = touched.Where(map.ContainsKey).Select(t => map[t]).ToList();
        touched.Clear();
        foreach (var t in newTouched) touched.Add(t);
    }

    /// <summary>Flipped faces: reverses the vertex order of a random share of the faces (all of them if fraction ≥ 1,
    /// an inside-out object). Returns how many were flipped.</summary>
    public static int FlipFaces(ObjMesh m, Rng rng, double fraction)
    {
        var n = 0;
        for (var fi = 0; fi < m.F.Count; fi++)
        {
            if (fraction < 1 && !rng.Chance(fraction)) continue;
            Array.Reverse(m.F[fi]);
            n++;
        }
        if (n == 0 && m.F.Count > 0)
        {
            var fi = rng.Int(0, m.F.Count);
            Array.Reverse(m.F[fi]);
            n = 1;
        }
        return n;
    }

    /// <summary>
    /// Duplicate vertices: the selected faces stop sharing vertices; each gets its own copies ("polygon soup" when
    /// fraction ≥ 1). Some copies are nudged by up to <paramref name="jitter"/> per axis (kept well inside the weld
    /// tolerance). Returns how many copies were made.
    /// </summary>
    public static int SplitVertices(ObjMesh m, Rng rng, double fraction, double jitter, double jitterShare)
    {
        var made = 0;
        for (var fi = 0; fi < m.F.Count; fi++)
        {
            if (fraction < 1 && !rng.Chance(fraction)) continue;
            var f = m.F[fi];
            for (var k = 0; k < f.Length; k++)
            {
                var v = m.V[f[k]];
                if (jitter > 0 && rng.Chance(jitterShare))
                    v += new Vec3(Nudge(rng, jitter), Nudge(rng, jitter), Nudge(rng, jitter));
                f[k] = m.V.Count;
                m.V.Add(v);
                made++;
            }
        }
        return made;
    }

    private static double Nudge(Rng rng, double jitter) => Math.Round(rng.Range(-jitter, jitter), 6);

    /// <summary>Drops unused vertices, keeping the order of the rest.</summary>
    public static void Compact(ObjMesh m)
    {
        var used = new bool[m.V.Count];
        foreach (var f in m.F) foreach (var i in f) used[i] = true;
        var map = new int[m.V.Count];
        var kept = new List<Vec3>();
        for (var i = 0; i < m.V.Count; i++)
        {
            if (!used[i]) continue;
            map[i] = kept.Count;
            kept.Add(m.V[i]);
        }
        m.V.Clear();
        m.V.AddRange(kept);
        foreach (var f in m.F) for (var k = 0; k < f.Length; k++) f[k] = map[f[k]];
    }

    public static void MoveZ(ObjMesh m, double dz)
    {
        for (var i = 0; i < m.V.Count; i++) m.V[i] = m.V[i] with { Z = Math.Round(m.V[i].Z + dz, 6) };
    }
}
