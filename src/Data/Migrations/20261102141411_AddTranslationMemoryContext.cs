using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLanguageExplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationMemoryContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TranslationMemoryEntries_SourceText_SourceLanguage_TargetLanguage",
                table: "TranslationMemoryEntries");

            migrationBuilder.AddColumn<string>(
                name: "Context",
                table: "TranslationMemoryEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PluralCount",
                table: "TranslationMemoryEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: -1);

            migrationBuilder.AddColumn<int>(
                name: "PluralIndex",
                table: "TranslationMemoryEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: -1);

            migrationBuilder.AddColumn<string>(
                name: "ScopeKey",
                table: "TranslationMemoryEntries",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            // Eski kayıtların bağlamı bilinmiyor. Veri silmeden her kaydı kendi
            // "legacy:<id>" kapsamına taşıyarak yeni bağlamlı kayıtlarla otomatik
            // eşleşmelerini engelle ve olası mükerrerleri benzersiz indekse hazırla.
            migrationBuilder.Sql(
                "UPDATE \"TranslationMemoryEntries\" " +
                "SET \"ScopeKey\" = 'legacy:' || \"Id\" " +
                "WHERE \"ScopeKey\" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationMemoryEntries_SourceText_SourceLanguage_TargetLanguage_ScopeKey_Context_PluralIndex_PluralCount",
                table: "TranslationMemoryEntries",
                columns: new[] { "SourceText", "SourceLanguage", "TargetLanguage", "ScopeKey", "Context", "PluralIndex", "PluralCount" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TranslationMemoryEntries_SourceText_SourceLanguage_TargetLanguage_ScopeKey_Context_PluralIndex_PluralCount",
                table: "TranslationMemoryEntries");

            migrationBuilder.DropColumn(
                name: "Context",
                table: "TranslationMemoryEntries");

            migrationBuilder.DropColumn(
                name: "PluralCount",
                table: "TranslationMemoryEntries");

            migrationBuilder.DropColumn(
                name: "PluralIndex",
                table: "TranslationMemoryEntries");

            migrationBuilder.DropColumn(
                name: "ScopeKey",
                table: "TranslationMemoryEntries");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationMemoryEntries_SourceText_SourceLanguage_TargetLanguage",
                table: "TranslationMemoryEntries",
                columns: new[] { "SourceText", "SourceLanguage", "TargetLanguage" });
        }
    }
}
