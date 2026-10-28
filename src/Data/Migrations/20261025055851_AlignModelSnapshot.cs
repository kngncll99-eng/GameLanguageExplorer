using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLanguageExplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GamePlatforms_GameId",
                table: "GamePlatforms");

            migrationBuilder.DropIndex(
                name: "IX_GamePlatforms_PlatformName_PlatformGameId",
                table: "GamePlatforms");

            migrationBuilder.CreateIndex(
                name: "IX_GamePlatforms_GameId_PlatformName",
                table: "GamePlatforms",
                columns: new[] { "GameId", "PlatformName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GamePlatforms_GameId_PlatformName",
                table: "GamePlatforms");

            migrationBuilder.CreateIndex(
                name: "IX_GamePlatforms_GameId",
                table: "GamePlatforms",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePlatforms_PlatformName_PlatformGameId",
                table: "GamePlatforms",
                columns: new[] { "PlatformName", "PlatformGameId" },
                unique: true);
        }
    }
}
