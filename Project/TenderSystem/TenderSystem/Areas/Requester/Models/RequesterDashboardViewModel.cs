namespace TenderSystem.Areas.Requester.Models;

public class RequesterDashboardViewModel
{
    public string DepartmentName { get; set; } = "Department";
    public int TotalRequests { get; set; }
    public int DraftRequests { get; set; }
    public int SubmittedRequests { get; set; }
    public int ApprovedRequests { get; set; }
    public int ReturnedRequests { get; set; }
    public int RejectedRequests { get; set; }
    public int ConvertedToTenderRequests { get; set; }
    public int ApprovalPercent => TotalRequests == 0 ? 0 : (int)Math.Round(ApprovedRequests * 100.0 / TotalRequests);
    public List<PurchaseRequestDetailsViewModel> RecentRequests { get; set; } = [];
    public List<PurchaseRequestDetailsViewModel> ReviewNoteRequests { get; set; } = [];
    public List<RequesterDashboardReviewNoteViewModel> RecentReviewNotes { get; set; } = [];
}
