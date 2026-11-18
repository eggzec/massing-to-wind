using M2W.Geometry;

namespace M2W.Checker;

/// <summary>Index-based topology of one shell: edge use, orientation, vertex fans and Euler characteristic.</summary>
public sealed class Topology
{
    public int FreeEdges { get; private init; }
    public int NonManifoldEdges { get; private init; }
    public int NonManifoldVertices { get; private init; }

    /// <summary>Edges shared by exactly two faces that both traverse it in the same direction.</summary>
    public int InconsistentEdges { get; private init; }

    public int V { get; private init; }
    public int E { get; private init; }
    public int F { get; private init; }
    public int Euler => V - E + F;

    public static Topology Of(Shell shell)
    {
        // Per undirected edge: total uses and uses in the A→B (low→high index) direction.
        var uses = new Dictionary<Edge, (int Total, int Forward)>();
        void Add(int a, int b)
        {
            var e = new Edge(a, b);
            uses.TryGetValue(e, out var u);
            uses[e] = (u.Total + 1, u.Forward + (a < b ? 1 : 0));
        }
        foreach (var (a, b, c) in shell.Triangles)
        {
            Add(a, b);
            Add(b, c);
            Add(c, a);
        }

        int free = 0, nonManifold = 0, inconsistent = 0;
        foreach (var u in uses.Values)
        {
            if (u.Total == 1) free++;
            else if (u.Total > 2) nonManifold++;
            else if (u.Forward != 1) inconsistent++;
        }

        return new Topology
        {
            FreeEdges = free,
            NonManifoldEdges = nonManifold,
            InconsistentEdges = inconsistent,
            NonManifoldVertices = CountNonManifoldVertices(shell),
            V = shell.UsedVertices.Count(),
            E = uses.Count,
            F = shell.Triangles.Count,
        };
    }

    /// <summary>
    /// A vertex is non-manifold if the faces around it do not form one connected fan (faces joined through edges
    /// that contain the vertex), e.g. two closed boxes that share only a corner.
    /// </summary>
    private static int CountNonManifoldVertices(Shell shell)
    {
        var incident = new Dictionary<int, List<int>>();
        for (var t = 0; t < shell.Triangles.Count; t++)
        {
            var (a, b, c) = shell.Triangles[t];
            foreach (var v in new[] { a, b, c })
            {
                if (!incident.TryGetValue(v, out var list)) incident[v] = list = new List<int>();
                list.Add(t);
            }
        }

        var bad = 0;
        foreach (var (v, faces) in incident)
        {
            // Union faces around v that share a second vertex (i.e. an edge through v).
            var parent = Enumerable.Range(0, faces.Count).ToArray();
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            var byOther = new Dictionary<int, int>();
            for (var k = 0; k < faces.Count; k++)
            {
                var (a, b, c) = shell.Triangles[faces[k]];
                foreach (var w in new[] { a, b, c })
                {
                    if (w == v) continue;
                    if (byOther.TryGetValue(w, out var first)) parent[Find(k)] = Find(first);
                    else byOther[w] = k;
                }
            }
            var roots = 0;
            for (var k = 0; k < faces.Count; k++) if (Find(k) == k) roots++;
            if (roots > 1) bad++;
        }
        return bad;
    }
}
