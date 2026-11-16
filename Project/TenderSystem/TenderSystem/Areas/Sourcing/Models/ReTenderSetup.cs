using Microsoft.AspNetCore.Mvc.Rendering;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Areas.Sourcing.Models;

public sealed class ReTenderSetup
{
    public required Tender Source { get; init; }
    public DateTime? Deadline { get; init; }
    public string? DepartmentReviewerId { get; init; }
    public List<SelectListItem> DepartmentReviewers { get; } = [];
}
