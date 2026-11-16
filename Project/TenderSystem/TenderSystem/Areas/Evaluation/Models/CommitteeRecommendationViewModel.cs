namespace TenderSystem.Areas.Evaluation.Models;

public class CommitteeRecommendationViewModel
{
    public int TenderId { get; set; }
    public bool IsReady { get; set; }
    public bool CanReady { get; set; }
    public bool AllReady { get; set; }
    public bool IsSubmitted { get; set; }
    public int ReadyMembers { get; set; }
    public int VotedMembers { get; set; }
    public int VotingRound { get; set; } = 1;
    public int? MyRecommendedBidId { get; set; }
    public string? ConfigurationMessage { get; set; }
    public string StageKey => $"{ReadyMembers}:{VotedMembers}:{VotingRound}:{IsSubmitted}:{MyRecommendedBidId}";
    public List<RecommendationOfferViewModel> Offers { get; set; } = new();
}
