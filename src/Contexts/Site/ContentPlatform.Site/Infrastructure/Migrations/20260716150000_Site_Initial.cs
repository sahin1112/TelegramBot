using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentPlatform.Site.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Site_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "site");

            migrationBuilder.CreateTable(
                name: "blog_posts",
                schema: "site",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Slug = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    MetaDescription = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CoverImageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CoverImageAlt = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Views = table.Column<long>(type: "bigint", nullable: false),
                    CommentsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blog_posts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_blog_posts_ContentItemId",
                schema: "site",
                table: "blog_posts",
                column: "ContentItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_blog_posts_PublishedAt",
                schema: "site",
                table: "blog_posts",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_blog_posts_Slug",
                schema: "site",
                table: "blog_posts",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blog_posts",
                schema: "site");
        }
    }
}
