using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Configurations;

// OWNER: Dev 3  -  keys and unique indexes of EmployeeProfiles
public class EmployeeProfileConfiguration : IEntityTypeConfiguration<EmployeeProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeProfile> builder)
    {
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => x.EmployeeNumber).IsUnique();
    }
}
