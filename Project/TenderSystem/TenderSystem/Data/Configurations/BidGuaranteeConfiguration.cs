using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Bids;

namespace TenderSystem.Data.Configurations;

// OWNER: Sara  -  unique index of BidGuarantees
public class BidGuaranteeConfiguration : IEntityTypeConfiguration<BidGuarantee>
{
    public void Configure(EntityTypeBuilder<BidGuarantee> builder)
    {
        // A verification code identifies one guarantee, so it may appear only once. Guarantees without a code are not limited.
        builder.HasIndex(x => x.VerificationCode).IsUnique().HasFilter("[VerificationCode] IS NOT NULL");
    }
}
