using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Requests;

namespace TenderSystem.Data;

// OWNER: Dana  -  DbSet properties for the "Requests" tables only.
public partial class ApplicationDbContext
{
    public DbSet<PurchaseRequisition> PurchaseRequisitions => Set<PurchaseRequisition>();
    public DbSet<PurchaseRequisitionItem> PurchaseRequisitionItems => Set<PurchaseRequisitionItem>();
}
