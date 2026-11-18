using M2W.Geometry;
using M2W.Repair.Model;
using M2W.Repair.Planar;

namespace M2W.Repair.Union;

/// <summary>Collects the triangles of one output shell. Vertices are keyed by (plan vertex, height), so faces built
/// separately share vertices exactly.</summary>
internal sealed class ShellBuilder
{
    private readonly IReadOnlyList<P2> _pts;
    private readonly Dictionary<(int, double), int> _index = new();
    private readonly List<Vec3> _verts = new();
    private readonly List<Tri> _tris = new();

    public ShellBuilder(IReadOnlyList<P2> pts) { _pts = pts; }

    private int V(int plan, double z)
    {
        if (!_index.TryGetValue((plan, z), out var i))
        {
            i = _verts.Count;
            _verts.Add(new Vec3(_pts[plan].X, _pts[plan].Y, z));
            _index[(plan, z)] = i;
        }
        return i;
    }

    /// <summary>Adds the roof at <paramref name="z"/> (normal up) and the ground face (normal down) of one plan face.</summary>
    public bool AddCap(List<int> outer, List<List<int>> holes, double z)
    {
        var tris = new List<Tri>();
        var outerPts = outer.Select(i => _pts[i]).ToList();
        var holePts = holes.Select(h => (IReadOnlyList<P2>)h.Select(i => _pts[i]).ToList()).ToList();
        if (!Triangulator.TryTriangulate(outerPts, holePts, tris)) return false;
        var ids = new List<int>(outer);
        foreach (var h in holes) ids.AddRange(h);
        foreach (var t in tris)
        {
            int a = ids[t.A], b = ids[t.B], c = ids[t.C];
            _tris.Add(new Tri(V(a, z), V(b, z), V(c, z)));
            _tris.Add(new Tri(V(a, 0), V(c, 0), V(b, 0)));
        }
        return true;
    }

    /// <summary>
    /// Adds the wall on plan edge u→v (solid on the left, wall facing right) between the lowest and highest height of
    /// the two columns. Each column lists, ascending, every height at which another face meets that vertex.
    /// </summary>
    public void AddWall(int u, int v, List<double> zu, List<double> zv)
    {
        int i = 0, j = 0;
        while (i + 1 < zu.Count || j + 1 < zv.Count)
        {
            var advanceV = i + 1 >= zu.Count || (j + 1 < zv.Count && zv[j + 1] <= zu[i + 1]);
            if (advanceV)
            {
                _tris.Add(new Tri(V(u, zu[i]), V(v, zv[j]), V(v, zv[j + 1])));
                j++;
            }
            else
            {
                _tris.Add(new Tri(V(u, zu[i]), V(v, zv[j]), V(u, zu[i + 1])));
                i++;
            }
        }
    }

    public OutputShell ToShell(IReadOnlyList<string> blocks) =>
        new() { Vertices = _verts, Triangles = _tris, Blocks = blocks };
}
