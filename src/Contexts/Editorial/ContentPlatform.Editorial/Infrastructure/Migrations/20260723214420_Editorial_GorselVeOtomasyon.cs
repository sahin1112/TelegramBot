using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentPlatform.Editorial.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Editorial_GorselVeOtomasyon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoContent",
                schema: "editorial",
                table: "content_items",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoImage",
                schema: "editorial",
                table: "content_items",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoPublish",
                schema: "editorial",
                table: "content_items",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoVideo",
                schema: "editorial",
                table: "content_items",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BadgeAuto",
                schema: "editorial",
                table: "content_items",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BadgeOverride",
                schema: "editorial",
                table: "content_items",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Card1x1Pool",
                schema: "editorial",
                table: "content_items",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CardReelsPool",
                schema: "editorial",
                table: "content_items",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ContentAttempts",
                schema: "editorial",
                table: "content_items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ContentGen",
                schema: "editorial",
                table: "content_items",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ImageAttempts",
                schema: "editorial",
                table: "content_items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ImageGen",
                schema: "editorial",
                table: "content_items",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "VideoAttempts",
                schema: "editorial",
                table: "content_items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VideoGen",
                schema: "editorial",
                table: "content_items",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoContent",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "AutoImage",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "AutoPublish",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "AutoVideo",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "BadgeAuto",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "BadgeOverride",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "Card1x1Pool",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "CardReelsPool",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "ContentAttempts",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "ContentGen",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "ImageAttempts",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "ImageGen",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "VideoAttempts",
                schema: "editorial",
                table: "content_items");

            migrationBuilder.DropColumn(
                name: "VideoGen",
                schema: "editorial",
                table: "content_items");
        }
    }
}
