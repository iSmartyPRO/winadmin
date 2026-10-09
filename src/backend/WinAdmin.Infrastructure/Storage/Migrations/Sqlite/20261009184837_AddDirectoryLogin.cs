using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinAdmin.Infrastructure.Storage.Migrations.Sqlite
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
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Sid = table.Column<string>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryRefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
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
