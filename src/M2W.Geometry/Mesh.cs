using System.Collections;

namespace M2W.Geometry;

/// <summary>
/// A polygon face: an ordered list of 0-based indices into <see cref="Mesh.Vertices"/>.
/// Counter-clockwise when seen from outside (AGREEMENT.md). Value equality on the index sequence.
/// </summary>
public sealed class Face : IReadOnlyList<int>, IEquatable<Face>
{
    private readonly int[] _indices;

    public Face(IEnumerable<int> indices)
    {
        _indices = indices.ToArray();
        if (_indices.Length < 3) throw new ArgumentException("A face needs at least 3 vertices.", nameof(indices));
        foreach (var i in _indices)
            if (i < 0) throw new ArgumentOutOfRangeException(nameof(indices), i, "Vertex indices are 0-based and non-negative.");
    }

    public Face(params int[] indices) : this((IEnumerable<int>)indices) { }

    public int Count => _indices.Length;
    public int this[int index] => _indices[index];
    public bool IsTriangle => _indices.Length == 3;

    /// <summary>The same vertices in reverse order (flips the normal).</summary>
    public Face Reversed() => new(_indices.Reverse());

    public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)_indices).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _indices.GetEnumerator();

    public bool Equals(Face? other) => other is not null && _indices.AsSpan().SequenceEqual(other._indices);
    public override bool Equals(object? obj) => obj is Face f && Equals(f);
    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var i in _indices) h.Add(i);
        return h.ToHashCode();
    }

    public override string ToString() => "f[" + string.Join(",", _indices) + "]";
}

/// <summary>
/// A named group of faces (one <c>o</c> block in OBJ). Input objects are named <c>B&lt;block&gt;_F&lt;floor&gt;</c>
/// or <c>B&lt;block&gt;</c>; output objects are <c>shell_NNN</c>. An empty name is the default group for faces
/// that appear before any <c>o</c> line; it may only be the first object of a mesh.
/// </summary>
public sealed class MeshObject
{
    public MeshObject(string name, IEnumerable<Face>? faces = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Faces = faces?.ToList() ?? new List<Face>();
    }

    public string Name { get; }
    public List<Face> Faces { get; }

    public override string ToString() => $"o {Name} ({Faces.Count} faces)";
}

/// <summary>
/// A polygon mesh: one shared vertex list and an ordered list of named objects whose faces index into it.
/// This mirrors the OBJ file layout so reading and writing round-trips exactly.
/// </summary>
public sealed class Mesh
{
    public Mesh() { }

    public Mesh(IEnumerable<Vec3> vertices, IEnumerable<MeshObject> objects)
    {
        Vertices.AddRange(vertices);
        Objects.AddRange(objects);
    }

    public List<Vec3> Vertices { get; } = new();
    public List<MeshObject> Objects { get; } = new();

    public int FaceCount => Objects.Sum(o => o.Faces.Count);

    /// <summary>All faces in file order.</summary>
    public IEnumerable<Face> AllFaces => Objects.SelectMany(o => o.Faces);

    /// <summary>Adds a vertex and returns its 0-based index.</summary>
    public int AddVertex(Vec3 v)
    {
        Vertices.Add(v);
        return Vertices.Count - 1;
    }

    /// <summary>Adds a new, empty named object and returns it.</summary>
    public MeshObject AddObject(string name)
    {
        var o = new MeshObject(name);
        Objects.Add(o);
        return o;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if any face index is out of range, any vertex is not finite,
    /// or an unnamed object is not first.
    /// </summary>
    public void Validate()
    {
        for (var i = 0; i < Vertices.Count; i++)
            if (!Vertices[i].IsFinite)
                throw new InvalidOperationException($"Vertex {i} is not finite: {Vertices[i]}.");

        for (var o = 0; o < Objects.Count; o++)
        {
            var obj = Objects[o];
            if (obj.Name.Length == 0 && o != 0)
                throw new InvalidOperationException($"Object {o} has no name; only the first object may be unnamed.");
            foreach (var f in obj.Faces)
                foreach (var i in f)
                    if (i >= Vertices.Count)
                        throw new InvalidOperationException(
                            $"Object '{obj.Name}' has a face index {i} but the mesh has {Vertices.Count} vertices.");
        }
    }

    /// <summary>
    /// True if both meshes have the same vertices (exact doubles), the same objects in the same order,
    /// and the same faces in the same order.
    /// </summary>
    public bool GeometryEquals(Mesh other)
    {
        if (Vertices.Count != other.Vertices.Count || Objects.Count != other.Objects.Count) return false;
        for (var i = 0; i < Vertices.Count; i++)
            if (!Vertices[i].Equals(other.Vertices[i])) return false;
        for (var o = 0; o < Objects.Count; o++)
        {
            var a = Objects[o];
            var b = other.Objects[o];
            if (a.Name != b.Name || !a.Faces.SequenceEqual(b.Faces)) return false;
        }
        return true;
    }
}
