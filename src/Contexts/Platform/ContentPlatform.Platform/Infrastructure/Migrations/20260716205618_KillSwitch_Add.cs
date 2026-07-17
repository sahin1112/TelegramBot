using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentPlatform.Platform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KillSwitch_Add : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kill_switches",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Engaged = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kill_switches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_kill_switches_Scope_Key",
                schema: "platform",
                table: "kill_switches",
                columns: new[] { "Scope", "Key" },
                unique: true,
                filter: "[Key] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kill_switches",
                schema: "platform");
        }
    }
}
