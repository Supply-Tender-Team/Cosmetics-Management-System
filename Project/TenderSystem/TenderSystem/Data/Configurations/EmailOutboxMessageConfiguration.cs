using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Messaging;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea  -  keys and indexes of EmailOutbox
public class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("EmailOutbox");

        // The same email can be queued only once. This is what makes duplicate prevention survive restarts and parallel requests.
        builder.HasIndex(x => x.DedupKey).IsUnique();

        // The worker looks for due rows by status and time.
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt });

        // Reports look for the emails of one tender (or other record).
        builder.HasIndex(x => new { x.RelatedEntity, x.RelatedId });
    }
}
