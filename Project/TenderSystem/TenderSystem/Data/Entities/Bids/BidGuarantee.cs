using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Data.Entities.Bids;

/// <summary>Financial guarantee attached to a supplier bid.</summary>
// OWNER: Sara  |  table: BidGuarantees
public class BidGuarantee
{
    public int Id { get; set; }
    public int BidId { get; set; }

    /// <summary>Allowed values: BankGuarantee, JudicialGuarantee.</summary>
    [StringLength(30)]
    public string GuaranteeType { get; set; } = string.Empty;

    [StringLength(100)]
    public string? ReferenceNumber { get; set; }

    [StringLength(200)]
    public string? IssuerName { get; set; }

    [StringLength(200)]
    public string BeneficiaryName { get; set; } = string.Empty;

    [Precision(18, 2)]
    public decimal Amount { get; set; }

    [StringLength(3)]
    public string Currency { get; set; } = "JOD";

    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    [StringLength(200)]
    public string? GuarantorName { get; set; }

    [StringLength(50)]
    public string? GuarantorNationalNumber { get; set; }

    [StringLength(300)]
    public string? GuarantorAddress { get; set; }

    [StringLength(30)]
    public string? GuarantorPhone { get; set; }

    /// <summary>Unique when it is filled in (see Data/Configurations/BidGuaranteeConfiguration.cs).</summary>
    [StringLength(64)]
    public string? VerificationCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Bid Bid { get; set; } = null!;
    public ICollection<BidDocument> BidDocuments { get; set; } = new List<BidDocument>();
}
