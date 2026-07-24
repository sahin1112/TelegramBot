using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentPlatform.Ingestion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Ingestion_GorselVeOtomasyon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoContent",
                schema: "ingestion",
                table: "sources",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoImage",
                schema: "ingestion",
                table: "sources",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoVideo",
                schema: "ingestion",
                table: "sources",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Card1x1",
                schema: "ingestion",
                table: "sources",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardReels",
                schema: "ingestion",
                table: "sources",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoContent",
                schema: "ingestion",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "AutoImage",
                schema: "ingestion",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "AutoVideo",
                schema: "ingestion",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "Card1x1",
                schema: "ingestion",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "CardReels",
                schema: "ingestion",
                table: "sources");
        }
    }
}
