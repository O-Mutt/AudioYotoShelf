using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioYotoShelf.Core.DTOs.Audiobookshelf;

/// <summary>
/// Audiobookshelf returns <c>media.metadata.series</c> as an array normally, but as a single
/// object when the items endpoint is filtered by a series. It may also emit <c>sequence</c> as a
/// number or a string. This converter normalizes all of those shapes to <see cref="AbsSeries"/>[].
/// </summary>
public class AbsSeriesArrayConverter : JsonConverter<AbsSeries[]?>
{
    public override AbsSeries[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        return root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().Select(ParseOne).ToArray(),
            JsonValueKind.Object => [ParseOne(root)],
            _ => null,
        };
    }

    private static AbsSeries ParseOne(JsonElement element)
    {
        var id = element.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        var name = element.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";

        string? sequence = null;
        if (element.TryGetProperty("sequence", out var seqEl))
        {
            sequence = seqEl.ValueKind switch
            {
                JsonValueKind.String => seqEl.GetString(),
                JsonValueKind.Number => seqEl.GetRawText(),
                _ => null,
            };
        }

        return new AbsSeries(id, name, sequence);
    }

    public override void Write(Utf8JsonWriter writer, AbsSeries[]? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var series in value)
        {
            writer.WriteStartObject();
            writer.WriteString("id", series.Id);
            writer.WriteString("name", series.Name);
            if (series.Sequence is null) writer.WriteNull("sequence");
            else writer.WriteString("sequence", series.Sequence);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
