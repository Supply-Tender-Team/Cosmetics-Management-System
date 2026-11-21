using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Shared.Models.Reports;

// One optional focus, independent of the regular Results filters.
public class AiReportRequestViewModel
{
    [Required, StringLength(80)] public string Type { get; set; } = string.Empty;
    public bool SpecificTopic { get; set; }
    [StringLength(20)] public string? Focus { get; set; }
    [StringLength(450)] public string? FocusId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
