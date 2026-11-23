using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Ai;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea  -  keys of the Admin's test questions for the assistant (AI-20)
public class GoldenQuestionConfiguration : IEntityTypeConfiguration<GoldenQuestion>
{
    public void Configure(EntityTypeBuilder<GoldenQuestion> builder)
    {
        builder.ToTable("GoldenQuestions");
        builder.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById);
    }
}
