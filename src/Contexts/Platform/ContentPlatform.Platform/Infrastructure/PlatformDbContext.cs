using ContentPlatform.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Platform.Infrastructure;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    public DbSet<SocialAccount> SocialAccounts => Set<SocialAccount>();
    public DbSet<PublicationTarget> PublicationTargets => Set<PublicationTarget>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<KillSwitch> KillSwitches => Set<KillSwitch>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<SocialAccount>(e =>
        {
            e.ToTable("social_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Platform).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
            e.Property(x => x.CredentialsEncrypted).IsRequired();          // şifreli metin
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasMany(x => x.Targets).WithOne().HasForeignKey(t => t.SocialAccountId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Targets).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasIndex(x => new { x.Platform, x.Status });
        });

        b.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.Slug).IsRequired().HasMaxLength(140);
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.DefaultImageSource).HasMaxLength(16);
            e.Property(x => x.ScheduleMode).HasMaxLength(16);
            e.Property(x => x.DailyTimes).HasMaxLength(256);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
        });

        b.Entity<SystemSetting>(e =>
        {
            e.ToTable("system_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).IsRequired().HasMaxLength(200);
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Value).IsRequired();
        });

        b.Entity<PublicationTarget>(e =>
        {
            e.ToTable("publication_targets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Platform).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.Platform, x.Role, x.IsActive });
            e.Property(x => x.ExternalTargetId).IsRequired().HasMaxLength(128);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasIndex(x => new { x.SocialAccountId, x.ExternalTargetId }).IsUnique();
        });

        b.Entity<KillSwitch>(e =>
        {
            e.ToTable("kill_switches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Scope).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => new { x.Scope, x.Key }).IsUnique();
        });
    }
}
