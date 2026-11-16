using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea  -  a position has one person and a person holds one position
public class StandingCommitteeMemberConfiguration : IEntityTypeConfiguration<StandingCommitteeMember>
{
    public void Configure(EntityTypeBuilder<StandingCommitteeMember> builder)
    {
        builder.HasIndex(x => x.PositionCode).IsUnique();
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
