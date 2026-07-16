using ContentPlatform.Ingestion.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Ingestion.Infrastructure;

public sealed class IngestionDbContext(DbContextOptions<IngestionDbContext> options) : DbContext(options)
{
    public const string Schema = "ingestion";

    public DbSet<Source> Sources => Set<Source>();
    public DbSet<SeenItem> SeenItems => Set<SeenItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<Source>(e =>
        {
            e.ToTable("sources");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Url).HasMaxLength(1000);
            e.HasIndex(x => new { x.IsActive, x.Type });
        });

        b.Entity<SeenItem>(e =>
        {
            e.ToTable("seen_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceHash).IsRequired().HasMaxLength(128);
            e.HasIndex(x => x.SourceHash).IsUnique();
        });
    }
}
