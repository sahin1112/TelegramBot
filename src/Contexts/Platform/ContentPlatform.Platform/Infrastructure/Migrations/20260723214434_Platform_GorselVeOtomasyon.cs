using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentPlatform.Platform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Platform_GorselVeOtomasyon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AttentionBadges",
                schema: "platform",
                table: "categories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoContent",
                schema: "platform",
                table: "categories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoImage",
                schema: "platform",
                table: "categories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoPublish",
                schema: "platform",
                table: "categories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoVideo",
                schema: "platform",
                table: "categories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Card1x1",
                schema: "platform",
                table: "categories",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CardReels",
                schema: "platform",
                table: "categories",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttentionBadges",
                schema: "platform",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "AutoContent",
                schema: "platform",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "AutoImage",
                schema: "platform",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "AutoPublish",
                schema: "platform",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "AutoVideo",
                schema: "platform",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "Card1x1",
                schema: "platform",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "CardReels",
                schema: "platform",
                table: "categories");
        }
    }
}
