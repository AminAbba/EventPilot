using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventPilot.Web.Serialization;

// Require an input offset and return UTC.
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("An ISO 8601 timestamp is required.");
        var text = reader.GetString()!;
        var explicitOffset = text.EndsWith('Z') ||
            (text.Length >= 6 && text[^3] == ':' && (text[^6] == '+' || text[^6] == '-'));
        if (!explicitOffset || !reader.TryGetDateTimeOffset(out var value))
            throw new JsonException("Timestamps must include Z or an explicit UTC offset.");
        return value.UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime());
}
