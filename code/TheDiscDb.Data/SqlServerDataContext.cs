namespace TheDiscDb.Web.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using HotChocolate;
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

        var releaseAffiliateLink = modelBuilder.Entity<ReleaseAffiliateLink>();
        releaseAffiliateLink.HasKey(x => x.Id);
        releaseAffiliateLink.Property(x => x.MediaItemSlug).HasMaxLength(200);
        releaseAffiliateLink.Property(x => x.BoxsetSlug).HasMaxLength(200);
        releaseAffiliateLink.Property(x => x.ReleaseSlug).HasMaxLength(200).IsRequired();
        releaseAffiliateLink.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        releaseAffiliateLink.Property(x => x.ProviderHandle).HasMaxLength(300).IsRequired();
        releaseAffiliateLink.Property(x => x.ProviderUrl).HasMaxLength(1000).IsRequired();
        releaseAffiliateLink.Property(x => x.MatchedUpc).HasMaxLength(20);
        releaseAffiliateLink.Property(x => x.MatchSource).HasMaxLength(50).IsRequired();
        releaseAffiliateLink.Property(x => x.Notes).HasMaxLength(500);
        // Two filtered unique indexes — one per parent-slug variant — because the CHECK
        // constraint enforces that exactly one of MediaItemSlug/BoxsetSlug is non-null per row.
        // EF Core's default unique-index filter for nullable columns requires ALL nullable
        // columns to be non-null, which would never match our data. The explicit HasFilter
        // here narrows uniqueness to rows where the chosen parent column is populated.
        releaseAffiliateLink
            .HasIndex(x => new { x.MediaItemSlug, x.ReleaseSlug, x.Provider })
            .IsUnique()
            .HasFilter("[MediaItemSlug] IS NOT NULL");
        releaseAffiliateLink
            .HasIndex(x => new { x.BoxsetSlug, x.ReleaseSlug, x.Provider })
            .IsUnique()
            .HasFilter("[BoxsetSlug] IS NOT NULL");
        releaseAffiliateLink.ToTable("ReleaseAffiliateLinks", t =>
        {
            // Exactly one of MediaItemSlug / BoxsetSlug must be populated AND non-empty. Empty
            // strings would pass an IS NOT NULL check but never resolve via the lookup service.
            // Uses standard SQL (<> '') rather than SQL Server-specific LEN(...) so the
            // constraint works in tests that spin up SQLite in-memory.
            t.HasCheckConstraint(
                "CK_ReleaseAffiliateLinks_OneParentSlug",
                "([MediaItemSlug] IS NOT NULL AND [MediaItemSlug] <> '' AND [BoxsetSlug] IS NULL) " +
                "OR ([MediaItemSlug] IS NULL AND [BoxsetSlug] IS NOT NULL AND [BoxsetSlug] <> '')");
        });

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
        userContribution.HasOne(x => x.Boxset)
            .WithMany()
            .HasForeignKey(x => x.BoxsetId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
        userContribution.HasIndex(x => x.BoxsetId);

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
        userMessage.HasIndex(x => x.BoxsetId);
        userMessage.HasIndex(x => new { x.ToUserId, x.IsRead });
        userMessage.HasOne(x => x.Contribution)
            .WithMany()
            .HasForeignKey(x => x.ContributionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
        userMessage.HasOne(x => x.Boxset)
            .WithMany()
            .HasForeignKey(x => x.BoxsetId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        var contributionBoxset = modelBuilder.Entity<UserContributionBoxset>();
        contributionBoxset.HasKey(x => x.Id);
        contributionBoxset.Property(x => x.Status)
            .HasConversion<string>();
        contributionBoxset.HasMany(x => x.Members)
            .WithOne(x => x.Boxset)
            .OnDelete(DeleteBehavior.Cascade);

        var contributionBoxsetMember = modelBuilder.Entity<UserContributionBoxsetMember>();
        contributionBoxsetMember.HasKey(x => x.Id);
        contributionBoxsetMember.HasOne(x => x.Disc)
            .WithOne()
            .HasForeignKey<UserContributionBoxsetMember>("DiscId")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
        contributionBoxsetMember.HasIndex("DiscId").IsUnique().HasFilter("[DiscId] IS NOT NULL");

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

        var engramDisc = modelBuilder.Entity<EngramDisc>();
        engramDisc.HasKey(x => x.Id);
        engramDisc.HasIndex(x => x.ContentHash).IsUnique();
        engramDisc.HasMany(x => x.Titles)
            .WithOne(x => x.Disc)
            .OnDelete(DeleteBehavior.Cascade);
        engramDisc.HasOne(x => x.EngramRelease)
            .WithMany(x => x.Discs)
            .HasForeignKey(x => x.EngramReleaseId)
            .OnDelete(DeleteBehavior.SetNull);
        engramDisc.HasIndex(x => x.EngramReleaseId);

        var engramTitle = modelBuilder.Entity<EngramTitle>();
        engramTitle.HasKey(x => x.Id);
        engramTitle.HasIndex(x => new { x.EngramDiscId, x.TitleIndex }).IsUnique();

        var engramRelease = modelBuilder.Entity<EngramRelease>();
        engramRelease.HasKey(x => x.Id);
        engramRelease.HasIndex(x => x.ReleaseId).IsUnique();
        engramRelease.Property(x => x.ReleaseId).HasMaxLength(128);
        engramRelease.HasOne(x => x.UserContribution)
            .WithMany()
            .HasForeignKey(x => x.UserContributionId)
            .OnDelete(DeleteBehavior.SetNull);
        engramRelease.HasIndex(x => x.UserContributionId);

        var userFileNameTemplate = modelBuilder.Entity<UserFileNameTemplate>();
        userFileNameTemplate.HasKey(x => x.Id);
        userFileNameTemplate.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        userFileNameTemplate.Property(x => x.ItemType).IsRequired().HasMaxLength(64);
        userFileNameTemplate.Property(x => x.Template).IsRequired().HasMaxLength(512);
        userFileNameTemplate.HasIndex(x => new { x.UserId, x.ItemType }).IsUnique();

        ConfigureEditSuggestionModel(modelBuilder);

        IdentityModelConfiguration.ConfigureIdentityModel(modelBuilder);
    }

    private static void ConfigureEditSuggestionModel(ModelBuilder modelBuilder)
    {
        var suggestion = modelBuilder.Entity<EditSuggestion>();
        suggestion.HasKey(x => x.Id);
        suggestion.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        suggestion.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        suggestion.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
        suggestion.Property(x => x.Summary).HasMaxLength(500);
        suggestion.Property(x => x.TargetEntityType).IsRequired().HasMaxLength(50);
        // 200 (parent slug max, matching ReleaseAffiliateLinks) + 1 separator +
        // 200 (release slug) + 8 buffer for child segments in future change types
        // (DiscSlug, etc.) is the reason for 410.
        suggestion.Property(x => x.TargetEntityKey).HasMaxLength(410);
        suggestion.Property(x => x.ReviewedByUserId).HasMaxLength(450);
        suggestion.HasMany(x => x.Changes)
            .WithOne(x => x.Suggestion)
            .HasForeignKey(x => x.SuggestionId)
            .OnDelete(DeleteBehavior.Cascade);
        // Queue paging: list by status, newest first.
        suggestion.HasIndex(x => new { x.Status, x.Created });
        // "My suggestions" list per user.
        suggestion.HasIndex(x => new { x.UserId, x.Created });
        // "Show me suggestions affecting this entity" lookups from detail pages.
        suggestion.HasIndex(x => new { x.TargetEntityType, x.TargetEntityKey });

        var change = modelBuilder.Entity<EditSuggestionChange>();
        change.HasKey(x => x.Id);
        change.Property(x => x.Type).IsRequired().HasMaxLength(80);
        change.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        change.Property(x => x.AppliedByUserId).HasMaxLength(450);
        change.Property(x => x.ConflictReason).HasMaxLength(500);
        change.Property(x => x.AdminNote).HasMaxLength(1000);
        // Snapshot + proposed payloads are JSON. Stored as nvarchar(max) for SQL Server 2022
        // compatibility; can migrate to native `json` column type when everywhere we run
        // supports it (SQL Server 2025+ / Azure SQL).
        change.Property(x => x.OriginalSnapshotJson).HasColumnType("nvarchar(max)");
        change.Property(x => x.ProposedJson).IsRequired().HasColumnType("nvarchar(max)");
        // Drives the batch file-sync query: WHERE Status = 'Applied' AND SyncedToFilesAt IS NULL.
        change.HasIndex(x => new { x.Status, x.SyncedToFilesAt });
        // Stable display order within a bundle.
        change.HasIndex(x => new { x.SuggestionId, x.Ordinal });

        var history = modelBuilder.Entity<EditSuggestionHistory>();
        history.HasKey(x => x.Id);
        history.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        history.Property(x => x.UserId).HasMaxLength(450);
        history.Property(x => x.Description).HasMaxLength(1000);
        // FK to the parent suggestion: cascade so deleting a suggestion clears its audit trail.
        history.HasOne<EditSuggestion>()
            .WithMany()
            .HasForeignKey(x => x.SuggestionId)
            .OnDelete(DeleteBehavior.Cascade);
        // Optional FK to the specific change row (nullable: some history entries
        // describe the whole bundle, not an individual change). SetNull on delete
        // so history survives if individual change rows are pruned.
        history.HasOne<EditSuggestionChange>()
            .WithMany()
            .HasForeignKey(x => x.ChangeId)
            .OnDelete(DeleteBehavior.SetNull);
        history.HasIndex(x => x.SuggestionId);

        var message = modelBuilder.Entity<EditSuggestionMessage>();
        message.HasKey(x => x.Id);
        message.Property(x => x.FromUserId).IsRequired().HasMaxLength(450);
        message.Property(x => x.ToUserId).IsRequired().HasMaxLength(450);
        message.HasOne(x => x.Suggestion)
            .WithMany()
            .HasForeignKey(x => x.SuggestionId)
            .OnDelete(DeleteBehavior.Cascade);
        message.HasIndex(x => x.SuggestionId);
        message.HasIndex(x => new { x.ToUserId, x.IsRead });
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
    public DbSet<UserContributionBoxset> UserContributionBoxsets { get; set; } = null!;
    public DbSet<UserContributionBoxsetMember> UserContributionBoxsetMembers { get; set; } = null!;
    public DbSet<EngramDisc> EngramDiscs { get; set; } = null!;
    public DbSet<EngramTitle> EngramTitles { get; set; } = null!;
    public DbSet<EngramRelease> EngramReleases { get; set; } = null!;
    public DbSet<UserFileNameTemplate> UserFileNameTemplates { get; set; } = null!;
    public DbSet<ReleaseAffiliateLink> ReleaseAffiliateLinks { get; set; } = null!;
    public DbSet<EditSuggestion> EditSuggestions { get; set; } = null!;
    public DbSet<EditSuggestionChange> EditSuggestionChanges { get; set; } = null!;
    public DbSet<EditSuggestionHistory> EditSuggestionHistory { get; set; } = null!;
    public DbSet<EditSuggestionMessage> EditSuggestionMessages { get; set; } = null!;
}

public class EngramDisc
{
    public int Id { get; set; }
    public int? EngramReleaseId { get; set; }
    public EngramRelease? EngramRelease { get; set; }
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
    public string? ScanLogPath { get; set; }
    public string EngramVersion { get; set; } = string.Empty;
    public string ExportVersion { get; set; } = string.Empty;
    public int ContributionTier { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public ICollection<EngramTitle> Titles { get; set; } = new List<EngramTitle>();
}

public class EngramRelease
{
    public int Id { get; set; }
    public string ReleaseId { get; set; } = string.Empty;
    public string? FrontImageUrl { get; set; }
    public string? BackImageUrl { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? UserContributionId { get; set; }
    public UserContribution? UserContribution { get; set; }
    public ICollection<EngramDisc> Discs { get; set; } = new List<EngramDisc>();
}

public class EngramTitle
{
    public int Id { get; set; }
    public int EngramDiscId { get; set; }
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
    public EngramDisc Disc { get; set; } = null!;
}

public class UserContributionBoxset : IHasId
{
    [JsonIgnore]
    public int Id { get; set; }
    [NotMapped]
    [GraphQLIgnore]
    public string EncodedId { get; set; } = default!;
    [JsonIgnore]
    public string UserId { get; set; } = default!;

    public DateTimeOffset Created { get; set; }
    public UserContributionStatus Status { get; set; } = UserContributionStatus.Pending;

    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string? FrontImageUrl { get; set; }
    public string? BackImageUrl { get; set; }
    public string? Asin { get; set; }
    public string? Upc { get; set; }
    public DateTimeOffset? ReleaseDate { get; set; }
    public string? Locale { get; set; }
    public string? RegionCode { get; set; }

    public ICollection<UserContributionBoxsetMember> Members { get; set; } = new HashSet<UserContributionBoxsetMember>();
}

public class UserContributionBoxsetMember
{
    public int Id { get; set; }
    public UserContributionBoxset Boxset { get; set; } = default!;
    public UserContributionDisc? Disc { get; set; }
    public int SortOrder { get; set; }

    public string? ExistingDiscPath { get; set; }
    public string? ExistingDiscName { get; set; }
    public string? ExistingDiscFormat { get; set; }
}