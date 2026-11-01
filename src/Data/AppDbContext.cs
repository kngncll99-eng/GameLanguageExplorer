namespace GameLanguageExplorer.Data;

using GameLanguageExplorer.Core.Entities;
using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Game> Games { get; set; }
    public DbSet<GamePlatform> GamePlatforms { get; set; }
    public DbSet<CatalogSource> CatalogSources { get; set; }
    public DbSet<GameProfile> GameProfiles { get; set; }
    public DbSet<GameVersion> GameVersions { get; set; }
    public DbSet<InstalledGame> InstalledGames { get; set; }
    public DbSet<DiscoveredFile> DiscoveredFiles { get; set; }
    public DbSet<FileFormat> FileFormats { get; set; }
    public DbSet<FormatEvidence> FormatEvidences { get; set; }
    public DbSet<LocalizationProject> LocalizationProjects { get; set; }
    public DbSet<LocalizationEntry> LocalizationEntries { get; set; }
    public DbSet<GlossaryTerm> GlossaryTerms { get; set; }
    public DbSet<TranslationMemoryEntry> TranslationMemoryEntries { get; set; }
    public DbSet<BackupRecord> BackupRecords { get; set; }
    public DbSet<PluginRegistration> PluginRegistrations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(500);
            entity.HasIndex(e => e.Name);
        });

        modelBuilder.Entity<GamePlatform>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.GameId, e.PlatformName }).IsUnique();
            entity.HasOne(e => e.Game)
                .WithMany(g => g.Platforms)
                .HasForeignKey(e => e.GameId);
            entity.HasOne(e => e.CatalogSource)
                .WithMany(c => c.Platforms)
                .HasForeignKey(e => e.CatalogSourceId);
        });

        modelBuilder.Entity<CatalogSource>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<GameProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GameId);
            entity.HasOne(e => e.Game)
                .WithMany(g => g.Profiles)
                .HasForeignKey(e => e.GameId);
        });

        modelBuilder.Entity<GameVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GameId);
            entity.HasOne(e => e.Game)
                .WithMany(g => g.Versions)
                .HasForeignKey(e => e.GameId);
        });

        modelBuilder.Entity<InstalledGame>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GameId);
            entity.HasIndex(e => e.InstallPath);
            entity.HasOne(e => e.Game)
                .WithMany(g => g.Installations)
                .HasForeignKey(e => e.GameId);
        });

        modelBuilder.Entity<DiscoveredFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.InstalledGameId);
            entity.HasIndex(e => e.Sha256);

            // Aynı kurulumda aynı yol iki kez kaydedilemez: tekrar taramalar mükerrer üretmez.
            entity.HasIndex(e => new { e.InstalledGameId, e.FilePath }).IsUnique();

            entity.HasOne(e => e.InstalledGame)
                .WithMany(g => g.DiscoveredFiles)
                .HasForeignKey(e => e.InstalledGameId);
        });

        modelBuilder.Entity<FileFormat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<FormatEvidence>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DiscoveredFileId);
            entity.HasOne(e => e.DiscoveredFile)
                .WithMany()
                .HasForeignKey(e => e.DiscoveredFileId);
        });

        modelBuilder.Entity<LocalizationProject>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.InstalledGameId);
            entity.HasOne(e => e.InstalledGame)
                .WithMany()
                .HasForeignKey(e => e.InstalledGameId);
        });

        modelBuilder.Entity<LocalizationEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.LocalizationProjectId);
            entity.HasIndex(e => e.Key);
            entity.HasOne(e => e.Project)
                .WithMany(p => p.Entries)
                .HasForeignKey(e => e.LocalizationProjectId);
        });

        modelBuilder.Entity<GlossaryTerm>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SourceTerm).IsUnique();
        });

        modelBuilder.Entity<TranslationMemoryEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceLanguage).IsRequired().HasMaxLength(32);
            entity.Property(e => e.TargetLanguage).IsRequired().HasMaxLength(32);
            entity.HasIndex(e => new { e.SourceText, e.SourceLanguage, e.TargetLanguage });
        });

        modelBuilder.Entity<BackupRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OriginalFilePath);
        });

        modelBuilder.Entity<PluginRegistration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PluginName).IsUnique();
        });
    }
}