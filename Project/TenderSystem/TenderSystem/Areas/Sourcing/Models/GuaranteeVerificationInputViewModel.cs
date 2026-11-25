using System.ComponentModel.DataAnnotations;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Models;

public sealed class GuaranteeVerificationInputViewModel
{
    [Range(1, int.MaxValue)]
    public int TenderId { get; set; }

    [Range(1, int.MaxValue)]
    public int BidId { get; set; }

    [Range(1, int.MaxValue)]
    public int GuaranteeId { get; set; }

    [Required]
    [StringLength(20)]
    public string Status { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Method { get; set; } = Statuses.VerificationMethod.Manual;

    [StringLength(1000)]
    public string? Reason { get; set; }
}
