using TenderSystem.Data.Entities.Identity;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Sourcing.Models;

public class TenderSetup
{
    // The four committee positions (labels and seat codes live in CommitteePositions). The first one is the Department Manager
    // of the request's department; the other three are put in place by the Admin (Admin > Evaluation committee).
    public static readonly (string Title, string Code)[] Seats =
        CommitteePositions.All.Select(position => (position.Title, position.Code)).ToArray();

    public int RequestId { get; set; }
    [Required, StringLength(200)]
    public string Title { get; set; } = "";
    [Required]
    public string Description { get; set; } = "";
    [Required]
    public string Terms { get; set; } = "";
    [Required]
    public DateTime? Deadline { get; set; }
    public DateTime? RequestTargetDate { get; set; }
    public DateTime? RequestSubmittedAt { get; set; }
    public decimal TechnicalWeight { get; set; }
    public decimal FinancialWeight { get; set; }
    public string RequestReason { get; set; } = "";
    public string EvaluationFocus { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public string RequestNumber { get; set; } = "";
    public string? SupplierCategory { get; set; }
    public int[] SupplierIds { get; set; } = [];
    public List<SelectListItem> Suppliers { get; set; } = [];
    public List<string> SupplierCategories { get; set; } = [];
    public bool RequiresTopCommittee { get; set; }
    public List<TenderQuestion> Questions { get; set; } = [];
    public List<TenderQuestion> FinancialQuestions { get; set; } = [];
    public IReadOnlyList<TenderSystem.Data.Entities.Requests.PurchaseRequisitionItem> RequestItems { get; set; } = [];
    public string? DepartmentReviewerId { get; set; }
    public List<SelectListItem> Requests { get; set; } = [];
    public List<SelectListItem> DepartmentReviewers { get; set; } = [];
    public ApplicationUser?[] FixedReviewers { get; set; } = [];
    public bool CommitteeReady => DepartmentReviewers.Count > 0
        && FixedReviewers.Length == Seats.Length - 1
        && FixedReviewers.All(member => member is not null);
}

public class TenderQuestion
{
    [Required, StringLength(150)]
    public string Name { get; set; } = "";
    [Range(typeof(decimal), "0.01", "100")]
    public decimal Weight { get; set; }
}

public record TenderOfferScore(string Supplier, decimal Price, string Currency, decimal?[] Technical, decimal? Average, decimal? Financial, decimal? Total);
