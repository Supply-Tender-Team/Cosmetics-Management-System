namespace TenderSystem.Areas.Manager.Models;

public class ManagerClarificationThreadViewModel
{
    public int Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<ManagerClarificationMessageViewModel> Messages { get; set; } = [];
}
