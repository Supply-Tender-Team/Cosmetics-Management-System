using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Governance;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea  -  keys, unique indexes and checks of Contracts, DocumentVerifications and EvaluationSchedules

public class EvaluationScheduleConfiguration : IEntityTypeConfiguration<EvaluationSchedule>
{
    public void Configure(EntityTypeBuilder<EvaluationSchedule> builder)
    {
        // One deadline per tender. It also stops two people who share the tender at the same moment from writing two deadlines.
        builder.HasIndex(x => x.TenderId).IsUnique();
    }
}

public class DocumentVerificationConfiguration : IEntityTypeConfiguration<DocumentVerification>
{
    public void Configure(EntityTypeBuilder<DocumentVerification> builder)
    {
        // Each row is about exactly one supplier document OR exactly one guarantee.
        builder.ToTable(table => table.HasCheckConstraint("CK_DocumentVerifications_OneTarget",
            "([SupplierDocumentId] IS NOT NULL AND [BidGuaranteeId] IS NULL) OR ([SupplierDocumentId] IS NULL AND [BidGuaranteeId] IS NOT NULL)"));
        builder.HasIndex(x => x.SupplierDocumentId);
        builder.HasIndex(x => x.BidGuaranteeId);
    }
}

public class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.HasIndex(x => x.ContractNumber).IsUnique();
        // At most one live contract per tender. A Cancelled one does not count, so a new contract can replace it.
        builder.HasIndex(x => x.TenderId).IsUnique().HasFilter("[Status] <> 'Cancelled'");
        builder.HasIndex(x => x.AwardedBidId);
    }
}
