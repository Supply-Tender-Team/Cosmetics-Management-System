using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Bids;

namespace TenderSystem.Data.Entities.Requests;

/// <summary>The lines of a request: item, quantity, specification.</summary>
// OWNER: Dev 4  |  table: PurchaseRequisitionItems  |  design: docs-site (Schema Diagram)
public class PurchaseRequisitionItem
{
    public int Id { get; set; }
    public int RequisitionId { get; set; }
    [StringLength(200)]
    public string ItemName { get; set; } = string.Empty;
    [Precision(18, 2)]
    public decimal Quantity { get; set; }
    [StringLength(30)]
    public string Unit { get; set; } = string.Empty;
    public string Specifications { get; set; } = string.Empty;

    // Navigation properties
    public PurchaseRequisition Requisition { get; set; } = null!;
    public ICollection<BidItem> BidItems { get; set; } = new List<BidItem>();
}
