using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentPlatform.Ingestion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IngestSince_Add : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IngestSince",
                schema: "ingestion",
                table: "sources",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IngestSince",
                schema: "ingestion",
                table: "sources");
        }
    }
}
