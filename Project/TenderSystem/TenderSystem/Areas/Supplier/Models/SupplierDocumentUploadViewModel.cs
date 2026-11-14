using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace TenderSystem.Areas.Supplier.Models;

public sealed class SupplierDocumentUploadViewModel
{
    [Required]
    [Display(Name = "Document type")]
    public string DocType { get; set; } = string.Empty;

    [Required]
    [Display(Name = "File")]
    public IFormFile? File { get; set; }

    [Display(Name = "Expiry date")]
    [DataType(DataType.Date)]
    public DateTime? ExpiryDate { get; set; }
}
