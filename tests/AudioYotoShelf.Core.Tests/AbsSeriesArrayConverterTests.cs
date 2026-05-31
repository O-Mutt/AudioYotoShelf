using System.Text.Json;
using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using FluentAssertions;

namespace AudioYotoShelf.Core.Tests;

public class AbsSeriesArrayConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static AbsBookMetadata Deserialize(string seriesJson) =>
        JsonSerializer.Deserialize<AbsBookMetadata>(
            $$"""{ "title": "Book", "series": {{seriesJson}} }""", Options)!;

    [Fact]
    public void Deserialize_SeriesAsArray_ParsesAllEntries()
    {
        var metadata = Deserialize("""[{ "id": "s1", "name": "HP", "sequence": "1" }, { "id": "s2", "name": "Side", "sequence": "2" }]""");

        metadata.Series.Should().HaveCount(2);
        metadata.Series![0].Id.Should().Be("s1");
        metadata.Series[1].Sequence.Should().Be("2");
    }

    [Fact]
    public void Deserialize_SeriesAsSingleObject_WrapsInArray()
    {
        // ABS returns a single object when the items endpoint is filtered by series.
        var metadata = Deserialize("""{ "id": "s1", "name": "Harry Potter", "sequence": "3" }""");

        metadata.Series.Should().HaveCount(1);
        metadata.Series![0].Id.Should().Be("s1");
        metadata.Series[0].Sequence.Should().Be("3");
    }

    [Fact]
    public void Deserialize_SequenceAsNumber_CoercesToString()
    {
        var metadata = Deserialize("""{ "id": "s1", "name": "HP", "sequence": 4 }""");

        metadata.Series![0].Sequence.Should().Be("4");
    }

    [Fact]
    public void Deserialize_SeriesNull_ReturnsNull()
    {
        var metadata = Deserialize("null");
        metadata.Series.Should().BeNull();
    }

    [Fact]
    public void Roundtrip_SerializesBackToArrayShape()
    {
        var metadata = Deserialize("""{ "id": "s1", "name": "HP", "sequence": "1" }""");

        var json = JsonSerializer.Serialize(metadata,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        json.Should().Contain("\"series\":[");
        json.Should().Contain("\"sequence\":\"1\"");
    }
}
