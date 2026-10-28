using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLanguageExplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveredFileUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredFiles_InstalledGameId_FilePath",
                table: "DiscoveredFiles",
                columns: new[] { "InstalledGameId", "FilePath" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DiscoveredFiles_InstalledGameId_FilePath",
                table: "DiscoveredFiles");
        }
    }
}
