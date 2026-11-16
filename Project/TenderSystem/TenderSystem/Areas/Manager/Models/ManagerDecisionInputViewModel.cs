using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Manager.Models;

public class ManagerDecisionInputViewModel
{
    [Required(ErrorMessage = "Choose a decision.")]
    public string Decision { get; set; } = string.Empty;

    [Required(ErrorMessage = "Add a comment for the review record.")]
    [StringLength(300)]
    [Display(Name = "Manager comment")]
    public string Comment { get; set; } = string.Empty;
}
