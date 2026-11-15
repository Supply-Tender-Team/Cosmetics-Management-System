using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Shared.Models;

public sealed class ClarificationCreateViewModel
{
    [Required(ErrorMessage = "Choose the process record this question belongs to.")]
    public string RouteKey { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Subject { get; set; } = string.Empty;

    [Required, StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    public List<ClarificationRouteOption> Routes { get; set; } = [];
}

public sealed record ClarificationRouteOption(
    string Key,
    string ContextLabel,
    string StageLabel,
    string RecipientName,
    string RecipientRole);

public sealed class ClarificationReplyViewModel
{
    public int Id { get; set; }

    [Required, StringLength(2000)]
    public string Body { get; set; } = string.Empty;
}
