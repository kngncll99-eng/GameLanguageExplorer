using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLanguageExplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationMemoryLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TranslationMemoryEntries_SourceText",
                table: "TranslationMemoryEntries");

            migrationBuilder.AddColumn<string>(
                name: "SourceLanguage",
                table: "TranslationMemoryEntries",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TargetLanguage",
                table: "TranslationMemoryEntries",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationMemoryEntries_SourceText_SourceLanguage_TargetLanguage",
                table: "TranslationMemoryEntries",
                columns: new[] { "SourceText", "SourceLanguage", "TargetLanguage" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TranslationMemoryEntries_SourceText_SourceLanguage_TargetLanguage",
                table: "TranslationMemoryEntries");

            migrationBuilder.DropColumn(
                name: "SourceLanguage",
                table: "TranslationMemoryEntries");

            migrationBuilder.DropColumn(
                name: "TargetLanguage",
                table: "TranslationMemoryEntries");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationMemoryEntries_SourceText",
                table: "TranslationMemoryEntries",
                column: "SourceText");
        }
    }
}
