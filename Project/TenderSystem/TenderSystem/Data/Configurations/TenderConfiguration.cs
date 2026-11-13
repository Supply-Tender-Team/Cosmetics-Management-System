using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Configurations;

// OWNER: Dev 4  -  keys and unique indexes of Tenders
public class TenderConfiguration : IEntityTypeConfiguration<Tender>
{
    public void Configure(EntityTypeBuilder<Tender> builder)
    {
        builder.HasIndex(x => x.TenderNumber).IsUnique();
    }
}
