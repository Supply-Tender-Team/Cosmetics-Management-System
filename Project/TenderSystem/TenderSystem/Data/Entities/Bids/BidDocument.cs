using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TenderSystem.Data.Entities.Bids;

/// <summary>Files attached to a bid (datasheets, images, videos, samples).</summary>
// OWNER: Sara  |  table: BidDocuments  |  design: docs-site (Schema Diagram)
public class BidDocument
{
    public int Id { get; set; }
    public int BidId { get; set; }
    [StringLength(50)]
    public string DocType { get; set; } = string.Empty;
    [StringLength(255)]
    public string FileName { get; set; } = string.Empty;
    [StringLength(300)]
    public string FilePath { get; set; } = string.Empty;
    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
    /// <summary>Set when the file belongs to one item of the offer (a datasheet of that item).</summary>
    public int? BidItemId { get; set; }
    /// <summary>Set when the file belongs to one guarantee (the scan of the guarantee).</summary>
    public int? BidGuaranteeId { get; set; }

    // Navigation properties
    public Bid Bid { get; set; } = null!;
    public BidItem? BidItem { get; set; }
    public BidGuarantee? BidGuarantee { get; set; }
}
