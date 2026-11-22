using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea  -  keys and indexes of the websites allowed for web mode (AI-19)
public class WebAllowedDomainConfiguration : IEntityTypeConfiguration<WebAllowedDomain>
{
    public void Configure(EntityTypeBuilder<WebAllowedDomain> builder)
    {
        builder.ToTable("WebAllowedDomains");
        builder.HasIndex(x => x.Domain).IsUnique();
        builder.HasOne(x => x.AddedBy).WithMany().HasForeignKey(x => x.AddedById);
    }
}
