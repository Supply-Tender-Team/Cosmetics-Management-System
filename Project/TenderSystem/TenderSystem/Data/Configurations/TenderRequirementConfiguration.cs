using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Tenders;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea (DB-04)  -  keys and unique indexes of TenderRequirements
public class TenderRequirementConfiguration : IEntityTypeConfiguration<TenderRequirement>
{
    public void Configure(EntityTypeBuilder<TenderRequirement> builder)
    {
        builder.HasOne(x => x.Tender).WithMany().HasForeignKey(x => x.TenderId);
        builder.HasIndex(x => new { x.TenderId, x.Name }).IsUnique();       // no two requirements with the same name in one tender
        builder.HasIndex(x => new { x.TenderId, x.SortOrder });
    }
}
