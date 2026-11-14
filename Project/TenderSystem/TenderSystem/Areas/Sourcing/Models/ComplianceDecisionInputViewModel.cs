using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Sourcing.Models;

public sealed class ComplianceDecisionInputViewModel
{
    [Range(1, int.MaxValue)]
    public int TenderId { get; set; }

    [Range(1, int.MaxValue)]
    public int BidId { get; set; }

    [Required]
    [StringLength(30)]
    public string Result { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Reason { get; set; }
}
