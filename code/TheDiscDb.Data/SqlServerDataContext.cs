namespace TheDiscDb.Web.Data;

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
        release.HasMany(x => x.Contributors)
            .WithMany(x => x.Releases)
            .UsingEntity(j => j.ToTable("ReleaseContributor"));

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
}