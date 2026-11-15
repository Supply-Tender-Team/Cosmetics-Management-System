using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Committee.Models.Votes;

// OWNER: Abdallah (helping Dana with TOP-02).
/// <summary>
/// The only values the browser sends for a vote. The member, the bid and the tender status are
/// always read on the server (CommitteeVotingService), never taken from this form.
/// </summary>
public class CastVoteViewModel
{
    [Required]
    public int TenderId { get; set; }

    [Required(ErrorMessage = "Choose Approve or Reject.")]
    [RegularExpression("^(Approve|Reject)$", ErrorMessage = "Choose Approve or Reject.")]
    public string? Vote { get; set; }

    [StringLength(1000)]
    [Display(Name = "Reason")]
    public string? Reason { get; set; }
}
