using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WinAdmin.Infrastructure.Storage.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddExcludedUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExcludedUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcludedUsers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExcludedUsers_UserName",
                table: "ExcludedUsers",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExcludedUsers");
        }
    }
}
