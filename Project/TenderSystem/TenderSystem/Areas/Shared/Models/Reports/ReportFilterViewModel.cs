using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Shared.Models.Reports;

public class ReportFilterViewModel
{
    [StringLength(40)] public string? Type { get; set; }
    [StringLength(100)] public string? Search { get; set; }
    [StringLength(40)] public string? Status { get; set; }
    [StringLength(40)] public string? TenderNumber { get; set; }
    [DataType(DataType.Date)] public DateTime? From { get; set; }
    [DataType(DataType.Date)] public DateTime? To { get; set; }
    public int? DepartmentId { get; set; }
    public int? SupplierId { get; set; }
    [StringLength(450)] public string? PersonId { get; set; }
    [StringLength(20)] public string Sort { get; set; } = "newest";
    [Range(1, 1000000)] public int Page { get; set; } = 1;
}
