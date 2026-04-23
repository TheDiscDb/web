namespace TheDiscDb.Web.Data;

using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;

public class SqlServerDataContext : DbContext
{
    public SqlServerDataContext(DbContextOptions<SqlServerDataContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var mediaItem = modelBuilder.Entity<MediaItem>();
        mediaItem.HasKey(x => x.Id);
        mediaItem.HasMany(x => x.Releases).WithOne(x => x.MediaItem);
        mediaItem.HasIndex(x => x.Slug).IsUnique();
        mediaItem.HasOne(x => x.Externalids).WithOne(x => x.MediaItem);
        mediaItem.HasMany(x => x.MediaItemGroups).WithOne(x => x.MediaItem);

        var release = modelBuilder.Entity<Release>();
        release.HasMany(x => x.Discs).WithOne(x => x.Release);
        release.HasMany(x => x.ReleaseGroups).WithOne(x => x.Release);
        release.HasMany(x => x.Contributors)
            .WithMany(x => x.Releases)
            .UsingEntity(j => j.ToTable("ReleaseContributor"));

        var releaseGroup = modelBuilder.Entity<ReleaseGroup>();
        releaseGroup.HasOne(x => x.Release).WithMany(x => x.ReleaseGroups);
        releaseGroup.HasOne(x => x.Group).WithMany(x => x.ReleaseGroups);
        releaseGroup.HasIndex(x => new { x.ReleaseId, x.GroupId }).IsUnique();

        var contributor = modelBuilder.Entity<Contributor>();
        contributor
            .HasIndex(u => u.Name)
            .IsUnique();

        var disc = modelBuilder.Entity<Disc>();
        disc.HasMany(x => x.Titles).WithOne(x => x.Disc);

        var title = modelBuilder.Entity<Title>();
        title.HasMany(x => x.Tracks).WithOne(x => x.Title);
        title.HasOne(x => x.Item).WithOne(x => x.DiscItem);

        var track = modelBuilder.Entity<Track>();

        var boxset = modelBuilder.Entity<Boxset>();
        boxset.HasOne(x => x.Release).WithOne(x => x.Boxset);

        var discItemReference = modelBuilder.Entity<DiscItemReference>();

        var externalIds = modelBuilder.Entity<ExternalIds>();

        var mediaItemGroups = modelBuilder.Entity<MediaItemGroup>();
        mediaItemGroups.HasOne(x => x.MediaItem).WithMany(x => x.MediaItemGroups);
        mediaItemGroups.HasOne(x => x.Group).WithMany(x => x.MediaItemGroups);

        var groups = modelBuilder.Entity<Group>();
        groups.HasMany(x => x.MediaItemGroups).WithOne(x => x.Group);
        groups.HasMany(x => x.ReleaseGroups).WithOne(x => x.Group);
        groups.HasIndex(x => x.Slug).IsUnique();

        var userContribution = modelBuilder.Entity<UserContribution>();
        userContribution.HasKey(x => x.Id);
        userContribution.Property(x => x.Status)
            .HasConversion<string>();
        userContribution.HasMany(x => x.Discs)
            .WithOne(x => x.UserContribution)
            .OnDelete(DeleteBehavior.Cascade);
        userContribution.HasMany(x => x.HashItems)
            .WithOne(x => x.UserContribution)
            .OnDelete(DeleteBehavior.Cascade);

        var userDiscContribution = modelBuilder.Entity<UserContributionDisc>();
        userDiscContribution.HasKey(x => x.Id);
        userDiscContribution.HasMany(x => x.Items)
            .WithOne(x => x.Disc)
            .OnDelete(DeleteBehavior.Cascade);

        var userContributionDiscHashItem = modelBuilder.Entity<UserContributionDiscHashItem>();
        userContributionDiscHashItem.HasKey(x => x.Id);

        var userContributiondiscItem = modelBuilder.Entity<UserContributionDiscItem>();
        userContributiondiscItem.HasKey(x => x.Id);
        userContributiondiscItem.HasMany(x => x.Chapters)
            .WithOne(x => x.Item)
            .OnDelete(DeleteBehavior.Cascade);
        userContributiondiscItem.HasMany(x => x.AudioTracks)
            .WithOne(x => x.Item)
            .OnDelete(DeleteBehavior.Cascade);

        var userContributionAudioTrack = modelBuilder.Entity<UserContributionAudioTrack>();
        var userContributionChapter = modelBuilder.Entity<UserContributionChapter>();

        var contributionHistory = modelBuilder.Entity<ContributionHistory>();
        contributionHistory.HasKey(x => x.Id);
        contributionHistory.Property(x => x.Type)
            .HasConversion<string>();
        contributionHistory.HasIndex(x => x.ContributionId);

        var userMessage = modelBuilder.Entity<UserMessage>();
        userMessage.HasKey(x => x.Id);
        userMessage.Property(x => x.Type)
            .HasConversion<string>();
        userMessage.HasIndex(x => x.ContributionId);
        userMessage.HasIndex(x => new { x.ToUserId, x.IsRead });

        var apiKeyEntity = modelBuilder.Entity<ApiKey>();
        apiKeyEntity.HasKey(x => x.Id);
        apiKeyEntity.HasIndex(x => x.KeyHash).IsUnique();
        apiKeyEntity.HasIndex(x => x.IsActive);
        apiKeyEntity.Property(x => x.Roles).HasMaxLength(500);
        apiKeyEntity.Property(x => x.OwnerEmail).HasMaxLength(256);

        var usageLog = modelBuilder.Entity<ApiKeyUsageLog>();
        usageLog.HasKey(x => x.Id);
        usageLog.HasIndex(x => new { x.ApiKeyId, x.Timestamp });
        usageLog.Property(x => x.OperationName).HasMaxLength(256);
        usageLog.HasOne(x => x.ApiKey)
            .WithMany(x => x.UsageLogs)
            .HasForeignKey(x => x.ApiKeyId)
            .OnDelete(DeleteBehavior.Cascade);

        var engramSubmission = modelBuilder.Entity<EngramSubmission>();
        engramSubmission.HasKey(x => x.Id);
        engramSubmission.HasIndex(x => x.ContentHash).IsUnique();
        engramSubmission.HasMany(x => x.Titles)
            .WithOne(x => x.Submission)
            .OnDelete(DeleteBehavior.Cascade);
        engramSubmission.HasOne(x => x.UserContribution)
            .WithMany()
            .HasForeignKey(x => x.UserContributionId)
            .OnDelete(DeleteBehavior.SetNull);

        var engramTitle = modelBuilder.Entity<EngramTitle>();
        engramTitle.HasKey(x => x.Id);
        engramTitle.HasIndex(x => new { x.EngramSubmissionId, x.TitleIndex }).IsUnique();

        IdentityModelConfiguration.ConfigureIdentityModel(modelBuilder);
    }

    public DbSet<MediaItem> MediaItems { get; set; } = null!;
    public DbSet<Boxset> BoxSets { get; set; } = null!;
    public DbSet<Chapter> Chapters { get; set; } = null!;
    public DbSet<Disc> Discs { get; set; } = null!;
    public DbSet<DiscItemReference> DiscItemReferences { get; set; } = null!;
    public DbSet<ExternalIds> ExternalIds { get; set; } = null!;
    public DbSet<Release> Releases { get; set; } = null!;
    public DbSet<Title> Titles { get; set; } = null!;
    public DbSet<Track> Tracks { get; set; } = null!;
    public DbSet<Group> Groups { get; set; } = null!;
    public DbSet<MediaItemGroup> MediaItemGroup { get; set; } = null!;
    public DbSet<ReleaseGroup> ReleaseGroups { get; set; } = null!;

    public DbSet<TheDiscDbUser> Users { get; set; } = null!;
    public DbSet<UserContribution> UserContributions { get; set; } = null!;
    public DbSet<UserContributionDisc> UserContributionDiscs { get; set; } = null!;
    public DbSet<UserContributionDiscItem> UserContributionDiscItems { get; set; } = null!;
    public DbSet<UserContributionChapter> UserContributionChapters { get; set; } = null!;
    public DbSet<UserContributionAudioTrack> UserContributionAudioTracks { get; set; } = null!;
    public DbSet<UserContributionDiscHashItem> UserContributionDiscHashItems { get; set; } = null!;
    public DbSet<ContributionHistory> ContributionHistory { get; set; } = null!;
    public DbSet<UserMessage> UserMessages { get; set; } = null!;
    public DbSet<Contributor> Contributors { get; set; } = null!;
    public DbSet<ApiKey> ApiKeys { get; set; } = null!;
    public DbSet<ApiKeyUsageLog> ApiKeyUsageLogs { get; set; } = null!;
    public DbSet<EngramSubmission> EngramSubmissions { get; set; } = null!;
    public DbSet<EngramTitle> EngramTitles { get; set; } = null!;
}

public class EngramSubmission
{
    public int Id { get; set; }
    public string? ReleaseId { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int? DiscNumber { get; set; }
    public int? TmdbId { get; set; }
    public string? DetectedTitle { get; set; }
    public int? DetectedSeason { get; set; }
    public string? ClassificationSource { get; set; }
    public double? ClassificationConfidence { get; set; }
    public string? Upc { get; set; }
    public string? FrontImageUrl { get; set; }
    public string? BackImageUrl { get; set; }
    public string? ScanLogPath { get; set; }
    public string EngramVersion { get; set; } = string.Empty;
    public string ExportVersion { get; set; } = string.Empty;
    public int ContributionTier { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public ICollection<EngramTitle> Titles { get; set; } = new List<EngramTitle>();
    public int? UserContributionId { get; set; }
    public UserContribution? UserContribution { get; set; }
}

public class EngramTitle
{
    public int Id { get; set; }
    public int EngramSubmissionId { get; set; }
    public int TitleIndex { get; set; }
    public string? SourceFilename { get; set; }
    public int? DurationSeconds { get; set; }
    public long? SizeBytes { get; set; }
    public int? ChapterCount { get; set; }
    public int? SegmentCount { get; set; }
    public string? SegmentMap { get; set; }
    public string? TitleType { get; set; }
    public string? Season { get; set; }
    public string? Episode { get; set; }
    public double? MatchConfidence { get; set; }
    public string? MatchSource { get; set; }
    public string? Edition { get; set; }
    public EngramSubmission Submission { get; set; } = null!;
}