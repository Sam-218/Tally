using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tally.Services;

namespace Tally.Models;

/// <summary>
/// The complete saved dataset. The JSON format matches the export file of the old
/// HTML version (events / activeEventId / articles / categories), so old files
/// can be loaded directly.
/// </summary>
public sealed class AppData
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("events")]
    public ObservableCollection<Party> Parties { get; set; } = new();

    [JsonPropertyName("activeEventId")]
    public string? ActiveEventId { get; set; }

    [JsonPropertyName("articles")]
    public ObservableCollection<Article>? Articles { get; set; }

    [JsonPropertyName("categories")]
    public ObservableCollection<Category>? Categories { get; set; }

    /// <summary>Fresh data with default articles and categories (first launch).</summary>
    public static AppData CreateDefault()
    {
        var data = new AppData();
        data.Normalize();
        return data;
    }

    /// <summary>
    /// Replaces missing parts with default values (e.g. an old file without an article list)
    /// and cleans up broken entries. Returns true if something was added.
    /// </summary>
    public bool Normalize()
    {
        var changed = false;

        Parties ??= new ObservableCollection<Party>();

        if (Categories is null)
        {
            Categories = new ObservableCollection<Category>(
                DefaultArticles.DefaultCategories.Select(n => new Category { Name = n }));
            changed = true;
        }

        if (Articles is null)
        {
            Articles = new ObservableCollection<Article>(
                DefaultArticles.All.Select(a => new Article { Name = a.Name, Category = a.Category }));
            changed = true;
        }

        // remove null entries (e.g. "events": [null])
        for (var i = Parties.Count - 1; i >= 0; i--)
            if (Parties[i] is null) { Parties.RemoveAt(i); changed = true; }
        for (var i = Articles.Count - 1; i >= 0; i--)
            if (Articles[i] is null) { Articles.RemoveAt(i); changed = true; }
        for (var i = Categories.Count - 1; i >= 0; i--)
            if (Categories[i] is null) { Categories.RemoveAt(i); changed = true; }
        foreach (var party in Parties)
        {
            for (var i = party.Items.Count - 1; i >= 0; i--)
                if (party.Items[i] is null) { party.Items.RemoveAt(i); changed = true; }
            for (var i = party.Guests.Count - 1; i >= 0; i--)
                if (party.Guests[i] is null) { party.Guests.RemoveAt(i); changed = true; }
        }

        if (ActiveEventId != null && Parties.All(p => p.Id != ActiveEventId))
        {
            ActiveEventId = Parties.FirstOrDefault()?.Id;
            changed = true;
        }

        return changed;
    }

    // ---------------------------------------------------------------- JSON

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        // keeps umlauts and € readable in the file (instead of ä etc.)
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Throws an exception for broken JSON or an unusable structure.</summary>
    public static AppData FromJson(string json)
    {
        var data = JsonSerializer.Deserialize<AppData>(json, Options)
                   ?? throw new JsonException(LocalizationManager.T("S_ErrorFileHasNoData"));

        // A valid file (even with an empty party list) has at least the "events" field.
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("events", out var ev) ||
            ev.ValueKind != JsonValueKind.Array)
            throw new JsonException(LocalizationManager.T("S_ErrorInvalidFileMissingEvents"));

        data.Normalize();
        return data;
    }
}
