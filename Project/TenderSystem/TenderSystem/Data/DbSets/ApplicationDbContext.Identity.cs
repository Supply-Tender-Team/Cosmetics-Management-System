using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data;

// OWNER: Abdallah  -  DbSet properties for the "Identity" tables only.
public partial class ApplicationDbContext
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<EmployeeProfile> EmployeeProfiles => Set<EmployeeProfile>();
    public DbSet<SupplierProfile> SupplierProfiles => Set<SupplierProfile>();
    public DbSet<SupplierDocument> SupplierDocuments => Set<SupplierDocument>();
}
