namespace TenderSystem.Areas.Requester.Models;

public class RequesterDashboardReviewNoteViewModel
{
    public int Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}
