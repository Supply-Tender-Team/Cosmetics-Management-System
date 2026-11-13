using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Biometrics;

namespace TenderSystem.Data.Configurations;

// OWNER: Wadea  -  keys and unique indexes of BiometricCredentials
public class BiometricCredentialConfiguration : IEntityTypeConfiguration<BiometricCredential>
{
    public void Configure(EntityTypeBuilder<BiometricCredential> builder)
    {
        builder.HasIndex(x => x.CredentialId).IsUnique();
    }
}
