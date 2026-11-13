using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSystem.Data.Entities.Identity;

namespace TenderSystem.Data.Configurations;

// OWNER: Abdallah  -  keys and unique indexes of Departments
public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}
