using System.Text.Json.Serialization;

namespace Tally.Models;

/// <summary>An entry in the article list (used for autocomplete when entering items).</summary>
public sealed class Article : ModelBase
{
    private string _id = Ids.New();
    private string _name = "";
    private string _category = "";

    [JsonPropertyName("id")]
    public string Id { get => _id; set => SetData(ref _id, value ?? Ids.New()); }

    [JsonPropertyName("name")]
    public string Name { get => _name; set => SetData(ref _name, value ?? ""); }

    /// <summary>Category name (not the ID – exactly as in the HTML version).</summary>
    [JsonPropertyName("category")]
    public string Category { get => _category; set => SetData(ref _category, value ?? ""); }
}

public sealed class Category : ModelBase
{
    private string _id = Ids.New();
    private string _name = "";

    [JsonPropertyName("id")]
    public string Id { get => _id; set => SetData(ref _id, value ?? Ids.New()); }

    [JsonPropertyName("name")]
    public string Name { get => _name; set => SetData(ref _name, value ?? ""); }

    /// <summary>In case a selection list ever displays this object directly: show the name instead of the class name.</summary>
    public override string ToString() => Name;
}
