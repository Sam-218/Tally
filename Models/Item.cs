using System.Text.Json.Serialization;

namespace Tally.Models;

/// <summary>A line item (drink) on a party's bill.</summary>
public sealed class Item : ModelBase
{
    private string _id = Ids.New();
    private string _name = "";
    private string _category = "";
    private decimal _quantity = 1;
    private decimal _unitPrice;
    private decimal _totalPrice;
    private string _store = "";

    [JsonPropertyName("id")]
    public string Id { get => _id; set => SetData(ref _id, value ?? Ids.New()); }

    [JsonPropertyName("name")]
    public string Name { get => _name; set => SetData(ref _name, value ?? ""); }

    [JsonPropertyName("category")]
    public string Category { get => _category; set => SetData(ref _category, value ?? ""); }

    [JsonPropertyName("quantity")]
    public decimal Quantity { get => _quantity; set => SetData(ref _quantity, value); }

    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get => _unitPrice; set => SetData(ref _unitPrice, value); }

    [JsonPropertyName("totalPrice")]
    public decimal TotalPrice { get => _totalPrice; set => SetData(ref _totalPrice, value); }

    [JsonPropertyName("store")]
    public string Store { get => _store; set => SetData(ref _store, value ?? ""); }
}
