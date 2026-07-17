using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentPlatform.Editorial.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContentAudit_Add : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "content_audit",
                schema: "editorial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Event = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ActorRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_audit", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_content_audit_ContentItemId_CreatedAt",
                schema: "editorial",
                table: "content_audit",
                columns: new[] { "ContentItemId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_audit",
                schema: "editorial");
        }
    }
}
