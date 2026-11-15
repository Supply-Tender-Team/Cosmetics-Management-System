using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Supplier.Models;

public sealed class RespondToInvitationViewModel
{
    [Range(1, int.MaxValue)]
    public int InvitationId { get; set; }

    [StringLength(500)]
    [Display(Name = "Reason for declining")]
    public string DeclineReason { get; set; } = string.Empty;
}
