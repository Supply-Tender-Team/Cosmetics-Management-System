using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Requester.Models;

public class CreatePurchaseRequestItemViewModel
{
    [Required, StringLength(200)]
    [Display(Name = "Item name")]
    public string ItemName { get; set; } = string.Empty;

    [Required, Range(typeof(decimal), "0.01", "9999999999999999.99", ErrorMessage = "Quantity must be greater than zero.")]
    public decimal? Quantity { get; set; }

    [Required, StringLength(30)]
    public string Unit { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Specifications")]
    public string Specifications { get; set; } = string.Empty;
}
