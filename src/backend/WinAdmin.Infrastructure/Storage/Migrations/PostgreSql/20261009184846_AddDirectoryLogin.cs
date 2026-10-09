using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinAdmin.Infrastructure.Storage.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddDirectoryLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DirectoryRefreshTokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Sid = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    RevokedAt = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryRefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryRefreshTokens_Sid",
                table: "DirectoryRefreshTokens",
                column: "Sid");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryRefreshTokens_TokenHash",
                table: "DirectoryRefreshTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectoryRefreshTokens");

            migrationBuilder.DropTable(
                name: "PlatformSettings");
        }
    }
}
