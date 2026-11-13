using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Requests;

namespace TenderSystem.Data.Configurations;

// OWNER: Dana  -  keys and unique indexes of PurchaseRequisitions
public class PurchaseRequisitionConfiguration : IEntityTypeConfiguration<PurchaseRequisition>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisition> builder)
    {
        builder.HasIndex(x => x.RequestNumber).IsUnique();
    }
}
