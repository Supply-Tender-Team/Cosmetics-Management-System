using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Requester.Models;

public class CreatePurchaseRequestItemViewModel
{
    public static readonly (string Label, string[] Units)[] UnitGroups =
    [
        ("Count and packaging", ["piece", "unit", "dozen", "pack", "set", "box", "carton", "case"]),
        ("Weight", ["mg", "mcg", "g", "kg", "oz", "lb", "t"]),
        ("Volume", ["ml", "l"]),
        ("Rolls and length", ["roll", "m", "sheet"])
    ];

    public static IEnumerable<string> AllowedUnits => UnitGroups.SelectMany(group => group.Units);

    public static bool IsAllowedUnit(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && AllowedUnits.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    [Required, StringLength(200)]
    [Display(Name = "Item name")]
    public string ItemName { get; set; } = string.Empty;

    [Required, Range(typeof(decimal), "0.01", "9999999999999999.99", ErrorMessage = "Quantity must be greater than zero.")]
    public decimal? Quantity { get; set; }

    [Required, StringLength(30)]
    [RegularExpression("^(?i:piece|unit|dozen|pack|set|box|carton|case|mg|mcg|g|kg|oz|lb|t|ml|l|roll|m|sheet)$", ErrorMessage = "Choose a valid quantity unit.")]
    public string Unit { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Specifications")]
    public string Specifications { get; set; } = string.Empty;
}
