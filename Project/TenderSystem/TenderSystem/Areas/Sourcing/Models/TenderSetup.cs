using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace TenderSystem.Areas.Sourcing.Models;

public class TenderSetup
{
    public static readonly (string Title, string Code)[] Seats =
    [
        ("Department Manager", "Department"),
        ("Department Financial Officer", "Financial"),
        ("Sourcing Department Representative", "Technical"),
        ("Financial Manager", "SourcingManager")
    ];

    public int RequestId { get; set; }
    [Required, StringLength(200)]
    public string Title { get; set; } = "";
    [Required]
    public string Description { get; set; } = "";
    [Required]
    public string Terms { get; set; } = "";
    [Required]
    public DateTime? Deadline { get; set; }
    public List<TenderQuestion> Questions { get; set; } =
    [
        new() { Name = "What evidence shows you meet the requested specifications and quantities?", Weight = 24 },
        new() { Name = "Which comparable projects did you complete for companies, including for-profit clients?", Weight = 12 },
        new() { Name = "If relevant to this request, what share of past work served young people, and what were the outcomes?", Weight = 8 },
        new() { Name = "How do you validate samples, control quality and trace defective batches?", Weight = 16 },
        new() { Name = "How will you meet the delivery deadline and recover from a supply interruption?", Weight = 12 },
        new() { Name = "What support and corrective actions will you provide after delivery?", Weight = 8 }
    ];
    public List<string> Reviewers { get; set; } = ["", "", "", ""];
    public List<SelectListItem> Requests { get; set; } = [];
    public List<SelectListItem> Accounts { get; set; } = [];
}

public class TenderQuestion
{
    [Required, StringLength(150)]
    public string Name { get; set; } = "";
    [Range(typeof(decimal), "0.01", "80")]
    public decimal Weight { get; set; }
}

public record TenderOfferScore(string Supplier, decimal Price, string Currency, decimal?[] Technical, decimal? Average, int? Financial, decimal? Total);
