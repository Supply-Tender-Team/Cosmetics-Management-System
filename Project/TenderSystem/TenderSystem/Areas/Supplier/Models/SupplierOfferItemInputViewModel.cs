using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Supplier.Models;

public sealed class SupplierOfferItemInputViewModel
{
    public int RequisitionItemId { get; set; }
    public int? BidItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string RequestedSpecifications { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "999999999999999")]
    public decimal? UnitPrice { get; set; }

    [StringLength(2000)]
    public string OfferedSpecifications { get; set; } = string.Empty;

    public List<IFormFile> SampleImages { get; set; } = [];

    public List<IFormFile> SupportingDocuments { get; set; } = [];
}
