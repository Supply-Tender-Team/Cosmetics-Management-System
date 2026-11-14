namespace TenderSystem.Areas.Manager.Models;

public class ManagerClarificationMessageViewModel
{
    public string SenderName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}
