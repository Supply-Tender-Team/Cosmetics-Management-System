using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Requests;

namespace TenderSystem.Data.Entities.Bids;

/// <summary>Itemized prices inside a bid.</summary>
// OWNER: Sara  |  table: BidItems  |  design: docs-site (Schema Diagram)
public class BidItem
{
    public int Id { get; set; }
    public int BidId { get; set; }
    public int RequisitionItemId { get; set; }
    [Precision(18, 2)]
    public decimal UnitPrice { get; set; }
    [Precision(18, 2)]
    public decimal Quantity { get; set; }
    [Precision(18, 2)]
    public decimal TaxAmount { get; set; }
    /// <summary>Tax percentage of this item (for example 16). Old offers have 0; the offer form calculates the rate for new offers.</summary>
    [Precision(5, 2)]
    public decimal TaxRate { get; set; }
    [Precision(18, 2)]
    public decimal ShippingCost { get; set; }
    [Precision(18, 2)]
    public decimal? InstallationCost { get; set; }
    public string OfferedSpecifications { get; set; } = string.Empty;

    // Navigation properties
    public Bid Bid { get; set; } = null!;
    public PurchaseRequisitionItem RequisitionItem { get; set; } = null!;
}
