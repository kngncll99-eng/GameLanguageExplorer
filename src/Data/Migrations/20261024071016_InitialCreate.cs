using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLanguageExplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OriginalFilePath = table.Column<string>(type: "TEXT", nullable: false),
                    BackupFilePath = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsRestored = table.Column<bool>(type: "INTEGER", nullable: false),
                    RestoredAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", nullable: true),
                    IsApiAvailable = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SyncError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileFormats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Extensions = table.Column<string>(type: "TEXT", nullable: false),
                    SupportsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsWrite = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileFormats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Developer = table.Column<string>(type: "TEXT", nullable: true),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    GameEngine = table.Column<string>(type: "TEXT", nullable: true),
                    CoverImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlossaryTerms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceTerm = table.Column<string>(type: "TEXT", nullable: false),
                    TargetTerm = table.Column<string>(type: "TEXT", nullable: false),
                    Context = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlossaryTerms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PluginRegistrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PluginName = table.Column<string>(type: "TEXT", nullable: false),
                    AssemblyPath = table.Column<string>(type: "TEXT", nullable: false),
                    SupportedFormats = table.Column<string>(type: "TEXT", nullable: false),
                    SupportedEngines = table.Column<string>(type: "TEXT", nullable: false),
                    SupportsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsWrite = table.Column<bool>(type: "INTEGER", nullable: false),
                    Dependencies = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PluginRegistrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TranslationMemoryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceText = table.Column<string>(type: "TEXT", nullable: false),
                    TargetText = table.Column<string>(type: "TEXT", nullable: false),
                    UsageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranslationMemoryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GamePlatforms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlatformName = table.Column<string>(type: "TEXT", nullable: false),
                    PlatformGameId = table.Column<string>(type: "TEXT", nullable: false),
                    StoreUrl = table.Column<string>(type: "TEXT", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CatalogSourceId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamePlatforms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GamePlatforms_CatalogSources_CatalogSourceId",
                        column: x => x.CatalogSourceId,
                        principalTable: "CatalogSources",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GamePlatforms_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    KnownFileExtensions = table.Column<string>(type: "TEXT", nullable: true),
                    PossibleFileLocations = table.Column<string>(type: "TEXT", nullable: true),
                    ArchiveFormat = table.Column<string>(type: "TEXT", nullable: true),
                    LocalizationFormat = table.Column<string>(type: "TEXT", nullable: true),
                    AvailableTools = table.Column<string>(type: "TEXT", nullable: true),
                    FileReadabilityStatus = table.Column<string>(type: "TEXT", nullable: true),
                    FileEditabilityStatus = table.Column<string>(type: "TEXT", nullable: true),
                    ExportStatus = table.Column<string>(type: "TEXT", nullable: true),
                    SourceOfInformation = table.Column<string>(type: "TEXT", nullable: true),
                    VerificationStatus = table.Column<string>(type: "TEXT", nullable: true),
                    LastVerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConfidenceScore = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameProfiles_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    VersionNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BuildNumber = table.Column<string>(type: "TEXT", nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlatformSpecificInfo = table.Column<string>(type: "TEXT", nullable: true),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameVersions_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstalledGames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    InstallPath = table.Column<string>(type: "TEXT", nullable: false),
                    ExecutableName = table.Column<string>(type: "TEXT", nullable: true),
                    SizeInBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    LastPlayedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    InstalledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastScanAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsInstalled = table.Column<bool>(type: "INTEGER", nullable: false),
                    InstallSource = table.Column<string>(type: "TEXT", nullable: true),
                    InstallSourceId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstalledGames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstalledGames_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveredFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InstalledGameId = table.Column<int>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    DetectedFormat = table.Column<string>(type: "TEXT", nullable: false),
                    DetectionConfidence = table.Column<string>(type: "TEXT", nullable: false),
                    IsReadable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsWritable = table.Column<bool>(type: "INTEGER", nullable: false),
                    AdapterUsed = table.Column<string>(type: "TEXT", nullable: true),
                    LastValidationResult = table.Column<string>(type: "TEXT", nullable: true),
                    LastValidatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DiscoveredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveredFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveredFiles_InstalledGames_InstalledGameId",
                        column: x => x.InstalledGameId,
                        principalTable: "InstalledGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocalizationProjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    InstalledGameId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceLanguage = table.Column<string>(type: "TEXT", nullable: false),
                    TargetLanguage = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalizationProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocalizationProjects_InstalledGames_InstalledGameId",
                        column: x => x.InstalledGameId,
                        principalTable: "InstalledGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormatEvidences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DiscoveredFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    EvidenceType = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceValue = table.Column<string>(type: "TEXT", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    MatchedPattern = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormatEvidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormatEvidences_DiscoveredFiles_DiscoveredFileId",
                        column: x => x.DiscoveredFileId,
                        principalTable: "DiscoveredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocalizationEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocalizationProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    SourceText = table.Column<string>(type: "TEXT", nullable: false),
                    TranslatedText = table.Column<string>(type: "TEXT", nullable: true),
                    Context = table.Column<string>(type: "TEXT", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: true),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    IsTranslated = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsReviewed = table.Column<bool>(type: "INTEGER", nullable: false),
                    NeedsReview = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalizationEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocalizationEntries_LocalizationProjects_LocalizationProjectId",
                        column: x => x.LocalizationProjectId,
                        principalTable: "LocalizationProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_OriginalFilePath",
                table: "BackupRecords",
                column: "OriginalFilePath");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogSources_Name",
                table: "CatalogSources",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredFiles_InstalledGameId",
                table: "DiscoveredFiles",
                column: "InstalledGameId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredFiles_Sha256",
                table: "DiscoveredFiles",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_FileFormats_Name",
                table: "FileFormats",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormatEvidences_DiscoveredFileId",
                table: "FormatEvidences",
                column: "DiscoveredFileId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePlatforms_CatalogSourceId",
                table: "GamePlatforms",
                column: "CatalogSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePlatforms_GameId",
                table: "GamePlatforms",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePlatforms_PlatformName_PlatformGameId",
                table: "GamePlatforms",
                columns: new[] { "PlatformName", "PlatformGameId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameProfiles_GameId",
                table: "GameProfiles",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_Name",
                table: "Games",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_GameVersions_GameId",
                table: "GameVersions",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GlossaryTerms_SourceTerm",
                table: "GlossaryTerms",
                column: "SourceTerm",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstalledGames_GameId",
                table: "InstalledGames",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledGames_InstallPath",
                table: "InstalledGames",
                column: "InstallPath");

            migrationBuilder.CreateIndex(
                name: "IX_LocalizationEntries_Key",
                table: "LocalizationEntries",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_LocalizationEntries_LocalizationProjectId",
                table: "LocalizationEntries",
                column: "LocalizationProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_LocalizationProjects_InstalledGameId",
                table: "LocalizationProjects",
                column: "InstalledGameId");

            migrationBuilder.CreateIndex(
                name: "IX_PluginRegistrations_PluginName",
                table: "PluginRegistrations",
                column: "PluginName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TranslationMemoryEntries_SourceText",
                table: "TranslationMemoryEntries",
                column: "SourceText");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupRecords");

            migrationBuilder.DropTable(
                name: "FileFormats");

            migrationBuilder.DropTable(
                name: "FormatEvidences");

            migrationBuilder.DropTable(
                name: "GamePlatforms");

            migrationBuilder.DropTable(
                name: "GameProfiles");

            migrationBuilder.DropTable(
                name: "GameVersions");

            migrationBuilder.DropTable(
                name: "GlossaryTerms");

            migrationBuilder.DropTable(
                name: "LocalizationEntries");

            migrationBuilder.DropTable(
                name: "PluginRegistrations");

            migrationBuilder.DropTable(
                name: "TranslationMemoryEntries");

            migrationBuilder.DropTable(
                name: "DiscoveredFiles");

            migrationBuilder.DropTable(
                name: "CatalogSources");

            migrationBuilder.DropTable(
                name: "LocalizationProjects");

            migrationBuilder.DropTable(
                name: "InstalledGames");

            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
