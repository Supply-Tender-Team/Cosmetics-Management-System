using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Entities.Evaluation;

/// <summary>Ollama output. Request and response JSON are stored as text (no MongoDB needed).</summary>
// OWNER: Dev 6  |  table: AiBidAnalyses  |  design: docs-site (Schema Diagram)
public class AiBidAnalysis
{
    public int Id { get; set; }
    public int BidId { get; set; }
    public int TenderId { get; set; }
    [StringLength(100)]
    public string Model { get; set; } = string.Empty;
    public string RequestJson { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    [Precision(18, 2)]
    public decimal? ExtractedPrice { get; set; }
    public int? ExtractedDeliveryDays { get; set; }
    public string? MissingFiles { get; set; }
    public string? Strengths { get; set; }
    public string? Weaknesses { get; set; }
    public string? AiRecommendation { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Bid Bid { get; set; } = null!;
    public Tender Tender { get; set; } = null!;
    public ApplicationUser RequestedByUser { get; set; } = null!;
}
