using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContentPlatform.Editorial.Infrastructure.Migrations
{
    /// <summary>
    /// content_audit.ActorRef ve content_items.CreatedByRef 200 → 1000 karakter.
    /// Neden: ingestion "ingestion:&lt;kaynak URL&gt;" yazar; Google News RSS makale URL'leri 200
    /// karakteri rahat aşıyor → "String or binary data would be truncated" ile içerik kaydı tümden
    /// başarısız oluyordu (20.07.2026 worker loglarındaki 32 DbUpdateException'ın kökü).
    /// </summary>
    public partial class WidenActorRefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ActorRef",
                schema: "editorial",
                table: "content_audit",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByRef",
                schema: "editorial",
                table: "content_items",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ActorRef",
                schema: "editorial",
                table: "content_audit",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByRef",
                schema: "editorial",
                table: "content_items",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);
        }
    }
}
