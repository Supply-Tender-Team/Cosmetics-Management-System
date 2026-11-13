using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Configurations;

// OWNER: Dev 4  -  keys and unique indexes of TenderSuppliers
public class TenderSupplierConfiguration : IEntityTypeConfiguration<TenderSupplier>
{
    public void Configure(EntityTypeBuilder<TenderSupplier> builder)
    {
        builder.HasKey(x => new { x.TenderId, x.SupplierProfileId });
    }
}
