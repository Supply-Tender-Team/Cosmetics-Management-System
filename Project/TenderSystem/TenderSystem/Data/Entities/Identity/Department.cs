using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TenderSystem.Data.Entities.Requests;

namespace TenderSystem.Data.Entities.Identity;

/// <summary>Company departments such as Production, Packaging, R&D, Marketing, Finance.</summary>
// OWNER: Abdallah  |  table: Departments  |  design: docs-site (Schema Diagram)
public class Department
{
    public int Id { get; set; }
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<ApplicationUser> AspNetUsers { get; set; } = new List<ApplicationUser>();
    public ICollection<PurchaseRequisition> PurchaseRequisitions { get; set; } = new List<PurchaseRequisition>();
}
