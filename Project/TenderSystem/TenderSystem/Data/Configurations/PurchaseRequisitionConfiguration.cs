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

        // Evaluation Preferences: each weight is a percentage. (Technical + Financial = 100 is checked by the form when the request is submitted.)
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_PurchaseRequisitions_TechnicalWeight",
                "[TechnicalEvaluationWeight] IS NULL OR ([TechnicalEvaluationWeight] >= 0 AND [TechnicalEvaluationWeight] <= 100)");
            table.HasCheckConstraint("CK_PurchaseRequisitions_FinancialWeight",
                "[FinancialEvaluationWeight] IS NULL OR ([FinancialEvaluationWeight] >= 0 AND [FinancialEvaluationWeight] <= 100)");
        });
    }
}
