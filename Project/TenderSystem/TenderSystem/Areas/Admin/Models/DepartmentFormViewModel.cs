using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Admin.Models;

public class DepartmentFormViewModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [Required]
    [StringLength(20)]
    public string Code { get; set; } = "";
}
