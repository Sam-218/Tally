using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tally.Models;

/// <summary>
/// Reads/writes the date as "yyyy-MM-dd" – exactly the format used by the HTML version.
/// Empty or broken values become null instead of aborting the whole load.
/// </summary>
public sealed class DateStringConverter : JsonConverter<DateTime?>
{
    public override bool HandleNull => true;

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }

        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text)) return null;

        // also cope with "2025-06-01T00:00:00.000Z": only the first 10 characters count
        var datePart = text.Trim();
        if (datePart.Length > 10) datePart = datePart[..10];

        return DateTime.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var result)
            ? result.Date
            : null;
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteStringValue("");
        else writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
