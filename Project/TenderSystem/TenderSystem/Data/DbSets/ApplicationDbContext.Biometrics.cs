using Microsoft.EntityFrameworkCore;
using TenderSystem.Data.Entities.Biometrics;

namespace TenderSystem.Data;

// OWNER: Dev 2  -  DbSet properties for the "Biometrics" tables only.
public partial class ApplicationDbContext
{
    public DbSet<BiometricCredential> BiometricCredentials => Set<BiometricCredential>();
    public DbSet<FaceEnrollment> FaceEnrollments => Set<FaceEnrollment>();
    public DbSet<BiometricAuthLog> BiometricAuthLogs => Set<BiometricAuthLog>();
}
