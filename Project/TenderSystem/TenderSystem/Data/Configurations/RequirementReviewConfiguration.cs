using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Bids;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea (DB-04)  -  keys and unique indexes of RequirementReviews
public class RequirementReviewConfiguration : IEntityTypeConfiguration<RequirementReview>
{
    public void Configure(EntityTypeBuilder<RequirementReview> builder)
    {
        builder.HasOne(x => x.Bid).WithMany().HasForeignKey(x => x.BidId);
        builder.HasOne(x => x.Requirement).WithMany(x => x.Reviews).HasForeignKey(x => x.RequirementId);
        builder.HasOne(x => x.ReviewedByUser).WithMany().HasForeignKey(x => x.ReviewedByUserId);
        builder.HasIndex(x => new { x.BidId, x.RequirementId }).IsUnique();   // one review per offer and requirement
        builder.HasIndex(x => x.RequirementId);
    }
}
