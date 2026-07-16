using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentPlatform.Editorial.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "editorial");

            migrationBuilder.CreateTable(
                name: "content_items",
                schema: "editorial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UseAi = table.Column<bool>(type: "bit", nullable: false),
                    ImageSource = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RiskLevel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TestMode = table.Column<bool>(type: "bit", nullable: false),
                    SourceHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawTitle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RawInput = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EditorialStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    MediaStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedByType = table.Column<int>(type: "int", nullable: false),
                    CreatedByRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApprovedByRef = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_revisions",
                schema: "editorial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ShortX = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InstagramCaption = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrimaryKeyword = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageAltText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_revisions_content_items_ContentItemId",
                        column: x => x.ContentItemId,
                        principalSchema: "editorial",
                        principalTable: "content_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media_assets",
                schema: "editorial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    TitleBurned = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_media_assets_content_items_ContentItemId",
                        column: x => x.ContentItemId,
                        principalSchema: "editorial",
                        principalTable: "content_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_content_items_EditorialStatus_MediaStatus",
                schema: "editorial",
                table: "content_items",
                columns: new[] { "EditorialStatus", "MediaStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_content_items_SourceHash",
                schema: "editorial",
                table: "content_items",
                column: "SourceHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_revisions_ContentItemId_RevisionNumber",
                schema: "editorial",
                table: "content_revisions",
                columns: new[] { "ContentItemId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_ContentItemId",
                schema: "editorial",
                table: "media_assets",
                column: "ContentItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_revisions",
                schema: "editorial");

            migrationBuilder.DropTable(
                name: "media_assets",
                schema: "editorial");

            migrationBuilder.DropTable(
                name: "content_items",
                schema: "editorial");
        }
    }
}
