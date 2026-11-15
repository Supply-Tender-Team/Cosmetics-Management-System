using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Evaluation;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea  -  the newest summary of a tender and audience is found by this index
public class AiTenderSummaryConfiguration : IEntityTypeConfiguration<AiTenderSummary>
{
    public void Configure(EntityTypeBuilder<AiTenderSummary> builder)
    {
        builder.HasIndex(x => new { x.TenderId, x.Audience, x.CreatedAt });
    }
}
