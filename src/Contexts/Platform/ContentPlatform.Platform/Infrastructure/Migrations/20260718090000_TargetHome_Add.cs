using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentPlatform.Platform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TargetHome_Add : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ana sayfa "Sosyalde ..." şeridi Sosyal Hesaplar'daki hedeflerden gelir:
            // ShowOnHome = ana sayfada yayınla seçimi, PublicUrl = herkese açık takip linki,
            // FollowerCount = gösterilecek takipçi/üye sayısı (opsiyonel).
            migrationBuilder.AddColumn<bool>(
                name: "ShowOnHome",
                schema: "platform",
                table: "publication_targets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PublicUrl",
                schema: "platform",
                table: "publication_targets",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FollowerCount",
                schema: "platform",
                table: "publication_targets",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_publication_targets_ShowOnHome_IsActive",
                schema: "platform",
                table: "publication_targets",
                columns: new[] { "ShowOnHome", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_publication_targets_ShowOnHome_IsActive",
                schema: "platform",
                table: "publication_targets");

            migrationBuilder.DropColumn(
                name: "ShowOnHome",
                schema: "platform",
                table: "publication_targets");

            migrationBuilder.DropColumn(
                name: "PublicUrl",
                schema: "platform",
                table: "publication_targets");

            migrationBuilder.DropColumn(
                name: "FollowerCount",
                schema: "platform",
                table: "publication_targets");
        }
    }
}
