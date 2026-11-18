using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace M2W.Geometry.Json;

/// <summary>Thrown when a JSON file is unreadable, misses a required field or breaks a rule in AGREEMENT.md.</summary>
public sealed class ModelValidationException : Exception
{
    public ModelValidationException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Implemented by every JSON model. <see cref="Validate"/> lists rule violations (empty = valid).</summary>
public interface IValidatable
{
    IReadOnlyList<string> Validate();
}

/// <summary>
/// One place for JSON settings so every role reads and writes the same way.
/// Reading ignores unknown fields, enforces C# <c>required</c> members and then runs <see cref="IValidatable.Validate"/>.
/// Writing is indented with LF line endings and no BOM, so identical models give byte-identical files.
/// </summary>
public static class M2WJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            NumberHandling = JsonNumberHandling.Strict,
        };
        o.Converters.Add(new Vec3ArrayConverter());
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        o.MakeReadOnly(populateMissingResolver: true);
        return o;
    }

    /// <summary>Parses and validates. Throws <see cref="ModelValidationException"/> on any problem.</summary>
    public static T Parse<T>(string json, string source = "json") where T : class, IValidatable
    {
        T? model;
        try
        {
            model = JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ModelValidationException($"{source}: {ex.Message}", ex);
        }
        if (model is null) throw new ModelValidationException($"{source}: document is null.");
        var errors = model.Validate();
        if (errors.Count > 0)
            throw new ModelValidationException($"{source}: " + string.Join("; ", errors));
        return model;
    }

    /// <summary>Reads, parses and validates a file. A missing file throws <see cref="FileNotFoundException"/>.</summary>
    public static T ReadFile<T>(string path) where T : class, IValidatable
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"File not found: {path}", path);
        return Parse<T>(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path));
    }

    /// <summary>Validates, then serializes with LF line endings and a final newline.</summary>
    public static string Serialize<T>(T model) where T : class, IValidatable
    {
        var errors = model.Validate();
        if (errors.Count > 0)
            throw new ModelValidationException($"{typeof(T).Name} is invalid: " + string.Join("; ", errors));
        // .NET 8 indents with Environment.NewLine. Literal CR/LF inside strings are escaped, so this is safe.
        return JsonSerializer.Serialize(model, Options).Replace("\r\n", "\n") + "\n";
    }

    public static void WriteFile<T>(T model, string path) where T : class, IValidatable
    {
        File.WriteAllText(path, Serialize(model), Utf8NoBom);
    }
}

/// <summary>Reads and writes <see cref="Vec3"/> as a JSON array of exactly three numbers, e.g. <c>[530000.0, 180000.0, 0.0]</c>.</summary>
public sealed class Vec3ArrayConverter : JsonConverter<Vec3>
{
    public override Vec3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an array of 3 numbers.");
        var values = new double[3];
        var n = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number) throw new JsonException("Expected an array of 3 numbers.");
            if (n == 3) throw new JsonException("Expected an array of exactly 3 numbers, got more.");
            values[n++] = reader.GetDouble();
        }
        if (n != 3) throw new JsonException($"Expected an array of exactly 3 numbers, got {n}.");
        return new Vec3(values[0], values[1], values[2]);
    }

    public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
    {
        // "+ 0.0" turns negative zero into zero, so a shift of -origin never prints "-0".
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X + 0.0);
        writer.WriteNumberValue(value.Y + 0.0);
        writer.WriteNumberValue(value.Z + 0.0);
        writer.WriteEndArray();
    }
}

/// <summary>Small helpers used by the model <c>Validate</c> methods.</summary>
internal static class Rules
{
    public static void SchemaVersion(List<string> e, string? v)
    {
        if (v != Models.SchemaVersions.Current)
            e.Add($"schemaVersion must be \"{Models.SchemaVersions.Current}\", got {(v is null ? "null" : $"\"{v}\"")}");
    }

    public static void NotEmpty(List<string> e, string field, string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) e.Add($"{field} is required and must not be empty");
    }

    public static void NotNull(List<string> e, string field, object? v)
    {
        if (v is null) e.Add($"{field} is required");
    }

    public static void Positive(List<string> e, string field, double v)
    {
        if (!double.IsFinite(v) || v <= 0) e.Add($"{field} must be a finite number > 0, got {v}");
    }

    public static void NonNegative(List<string> e, string field, double v)
    {
        if (!double.IsFinite(v) || v < 0) e.Add($"{field} must be a finite number >= 0, got {v}");
    }

    public static void Finite(List<string> e, string field, Vec3 v)
    {
        if (!v.IsFinite) e.Add($"{field} must be finite");
    }
}
