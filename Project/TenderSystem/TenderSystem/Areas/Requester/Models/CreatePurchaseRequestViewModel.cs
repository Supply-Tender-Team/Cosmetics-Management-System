using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Requester.Models;

public class CreatePurchaseRequestViewModel
{
    [Required, StringLength(200)]
    [Display(Name = "Request title")]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    [Display(Name = "Reason for request")]
    public string Reason { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(Low|Medium|High|Urgent)$", ErrorMessage = "Choose a valid priority.")]
    public string Priority { get; set; } = string.Empty;

    [Required, DataType(DataType.Date)]
    [Display(Name = "Target date")]
    public DateTime? TargetDate { get; set; }

    [MinLength(1, ErrorMessage = "Add at least one request item.")]
    public List<CreatePurchaseRequestItemViewModel> Items { get; set; } = [new()];
}
