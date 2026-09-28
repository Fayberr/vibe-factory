using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactorySim.Persistence;

/// <summary>Shared System.Text.Json settings for content packs and saves.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = Create(indented: false);
    public static readonly JsonSerializerOptions Pretty = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        o.Converters.Add(new BigNumConverter());
        o.Converters.Add(new GridPosConverter());
        return o;
    }
}

/// <summary>Writes BigNum as its exact string form ("1.5e3"); reads strings or plain JSON numbers.</summary>
public sealed class BigNumConverter : JsonConverter<BigNum>
{
    public override BigNum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String => BigNum.Parse(reader.GetString()!),
            _ => throw new JsonException($"Expected number or string for BigNum, got {reader.TokenType}."),
        };

    public override void Write(Utf8JsonWriter writer, BigNum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>Compact [x, y, z] form (z optional when reading).</summary>
public sealed class GridPosConverter : JsonConverter<GridPos>
{
    public override GridPos Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("GridPos must be an array [x, y, z].");
        Span<int> v = stackalloc int[3];
        int n = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (n >= 3) throw new JsonException("GridPos has more than 3 components.");
            v[n++] = reader.GetInt32();
        }
        if (n < 2) throw new JsonException("GridPos needs at least [x, y].");
        return new GridPos(v[0], v[1], v[2]);
    }

    public override void Write(Utf8JsonWriter writer, GridPos value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}
