using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Evaluation;

// OWNER: Marwa. One final confirmation and current recommendation per member and tender.
[Index(nameof(TenderId), nameof(EvaluatorUserId), IsUnique = true)]
public class EvaluationCommitteeSubmission
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public string EvaluatorUserId { get; set; } = string.Empty;
    public DateTime ReadyAt { get; set; }
    public int VotingRound { get; set; } = 1;
    public int? RecommendedBidId { get; set; }
    public DateTime? VotedAt { get; set; }
    public Tender Tender { get; set; } = null!;
    public ApplicationUser EvaluatorUser { get; set; } = null!;
    public Bid? RecommendedBid { get; set; }
}
