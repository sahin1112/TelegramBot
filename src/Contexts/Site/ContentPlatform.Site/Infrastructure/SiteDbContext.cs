using ContentPlatform.Site.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentPlatform.Site.Infrastructure;

public sealed class SiteDbContext(DbContextOptions<SiteDbContext> options) : DbContext(options)
{
    public const string Schema = "site";

    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<BlogPost>(e =>
        {
            e.ToTable("blog_posts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Slug).IsRequired().HasMaxLength(140);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.ContentItemId).IsUnique();          // idempotency
            e.HasIndex(x => x.PublishedAt);
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.MetaDescription).HasMaxLength(320);
            e.Property(x => x.BodyHtml).IsRequired();
            e.Property(x => x.CoverImageUrl).HasMaxLength(1000);
            e.Property(x => x.CoverImageAlt).HasMaxLength(300);
            e.Property(x => x.Tags)
                .HasConversion(
                    v => string.Join('\n', v),
                    v => v.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList());
        });
    }
}
