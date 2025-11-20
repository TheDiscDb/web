namespace TheDiscDb.Web.Data;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.InputModels;

public class SqlServerDataContext : DbContext
{
    public SqlServerDataContext(DbContextOptions<SqlServerDataContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
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
        //userContribution.HasOne(x => x.User).WithOne()
        //    .HasForeignKey<UserContribution>(x => x.UserId);
        userContribution.HasMany(x => x.Discs).WithOne(x => x.UserContribution);
        userContribution.HasMany(x => x.HashItems).WithOne(x => x.UserContribution);

        var userDiscContribution = modelBuilder.Entity<UserContributionDisc>();
        userDiscContribution.HasKey(x => x.Id);
        userDiscContribution.HasMany(x => x.Items).WithOne(x => x.Disc);

        var userContributionDiscHashItem = modelBuilder.Entity<UserContributionDiscHashItem>();
        userContributionDiscHashItem.HasKey(x => x.Id);

        var userContributiondiscItem = modelBuilder.Entity<UserContributionDiscItem>();
        userContributiondiscItem.HasKey(x => x.Id);
        userContributiondiscItem.HasMany(x => x.Chapters).WithOne(x => x.Item);
        userContributiondiscItem.HasMany(x => x.AudioTracks).WithOne(x => x.Item);

        var userContributionAudioTrack = modelBuilder.Entity<UserContributionAudioTrack>();
        var userContributionChapter = modelBuilder.Entity<UserContributionChapter>();

        BuildIdentityModel(modelBuilder);
    }

    private static void BuildIdentityModel(ModelBuilder modelBuilder)
    {
        // Taken from Asp.net core docs https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/security/authentication/customize-identity-model.md
        modelBuilder.Entity<TheDiscDbUser>(b =>
        {
            // Primary key
            b.HasKey(u => u.Id);

            // Indexes for "normalized" username and email, to allow efficient lookups
            b.HasIndex(u => u.NormalizedUserName).HasDatabaseName("UserNameIndex").IsUnique();
            b.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex");

            // Maps to the AspNetUsers table
            b.ToTable("AspNetUsers");

            // A concurrency token for use with the optimistic concurrency checking
            b.Property(u => u.ConcurrencyStamp).IsConcurrencyToken();

            // Limit the size of columns to use efficient database types
            b.Property(u => u.UserName).HasMaxLength(256);
            b.Property(u => u.NormalizedUserName).HasMaxLength(256);
            b.Property(u => u.Email).HasMaxLength(256);
            b.Property(u => u.NormalizedEmail).HasMaxLength(256);

            // The relationships between User and other entity types
            // Note that these relationships are configured with no navigation properties

            // Each User can have many UserClaims
            b.HasMany<IdentityUserClaim<string>>().WithOne().HasForeignKey(uc => uc.UserId).IsRequired();

            // Each User can have many UserLogins
            b.HasMany<IdentityUserLogin<string>>().WithOne().HasForeignKey(ul => ul.UserId).IsRequired();

            // Each User can have many UserTokens
            b.HasMany<IdentityUserToken<string>>().WithOne().HasForeignKey(ut => ut.UserId).IsRequired();

            // Each User can have many entries in the UserRole join table
            b.HasMany<IdentityUserRole<string>>().WithOne().HasForeignKey(ur => ur.UserId).IsRequired();
        });

        modelBuilder.Entity<IdentityUserClaim<string>>(b =>
        {
            // Primary key
            b.HasKey(uc => uc.Id);

            // Maps to the AspNetUserClaims table
            b.ToTable("AspNetUserClaims");
        });

        modelBuilder.Entity<IdentityUserLogin<string>>(b =>
        {
            // Composite primary key consisting of the LoginProvider and the key to use
            // with that provider
            b.HasKey(l => new { l.LoginProvider, l.ProviderKey });

            // Limit the size of the composite key columns due to common DB restrictions
            b.Property(l => l.LoginProvider).HasMaxLength(128);
            b.Property(l => l.ProviderKey).HasMaxLength(128);

            // Maps to the AspNetUserLogins table
            b.ToTable("AspNetUserLogins");
        });

        modelBuilder.Entity<IdentityUserToken<string>>(b =>
        {
            // Composite primary key consisting of the UserId, LoginProvider and Name
            b.HasKey(t => new { t.UserId, t.LoginProvider, t.Name });

            // Limit the size of the composite key columns due to common DB restrictions
            b.Property(t => t.LoginProvider).HasMaxLength(128);
            b.Property(t => t.Name).HasMaxLength(128);

            // Maps to the AspNetUserTokens table
            b.ToTable("AspNetUserTokens");
        });

        modelBuilder.Entity<IdentityRole>(b =>
        {
            // Primary key
            b.HasKey(r => r.Id);

            // Index for "normalized" role name to allow efficient lookups
            b.HasIndex(r => r.NormalizedName).HasDatabaseName("RoleNameIndex").IsUnique();

            // Maps to the AspNetRoles table
            b.ToTable("AspNetRoles");

            // A concurrency token for use with the optimistic concurrency checking
            b.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();

            // Limit the size of columns to use efficient database types
            b.Property(u => u.Name).HasMaxLength(256);
            b.Property(u => u.NormalizedName).HasMaxLength(256);

            // The relationships between Role and other entity types
            // Note that these relationships are configured with no navigation properties

            // Each Role can have many entries in the UserRole join table
            b.HasMany<IdentityUserRole<string>>().WithOne().HasForeignKey(ur => ur.RoleId).IsRequired();

            // Each Role can have many associated RoleClaims
            b.HasMany<IdentityRoleClaim<string>>().WithOne().HasForeignKey(rc => rc.RoleId).IsRequired();
        });

        modelBuilder.Entity<IdentityRoleClaim<string>>(b =>
        {
            // Primary key
            b.HasKey(rc => rc.Id);

            // Maps to the AspNetRoleClaims table
            b.ToTable("AspNetRoleClaims");
        });

        modelBuilder.Entity<IdentityUserRole<string>>(b =>
        {
            // Primary key
            b.HasKey(r => new { r.UserId, r.RoleId });

            // Maps to the AspNetUserRoles table
            b.ToTable("AspNetUserRoles");
        });
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
}

public class TheDiscDbUser : Microsoft.AspNetCore.Identity.IdentityUser
{
}

public enum UserContributionStatus
{
    Pending,
    Approved,
    ChangesRequested,
    Rejected,
    Imported
}

public class UserContribution
{
    public int Id { get; set; }
    public string UserId { get; set; }
    //public TheDiscDbUser User { get; set; } = null!;
    public DateTimeOffset Created { get; set; }
    public UserContributionStatus Status { get; set; } = UserContributionStatus.Pending;
    public ICollection<UserContributionDisc> Discs { get; set; } = new HashSet<UserContributionDisc>();
    public ICollection<UserContributionDiscHashItem> HashItems { get; set; } = new HashSet<UserContributionDiscHashItem>();

    public string MediaType { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string ExternalProvider { get; set; } = string.Empty;
    public DateTimeOffset ReleaseDate { get; set; }
    public string Asin { get; set; } = string.Empty;
    public string Upc { get; set; } = string.Empty;
    public string FrontImageUrl { get; set; } = string.Empty;
    public string BackImageUrl { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    public string ReleaseSlug { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string RegionCode { get; set; } = string.Empty;

    // These two are mostly used for display but not needed to generate the release
    public string Title { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
}

public class UserContributionDisc
{
    public int Id { get; set; }
    [JsonIgnore]
    public UserContribution UserContribution { get; set; } = null!;
    public string ContentHash { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool LogsUploaded { get; set; } = false;
    public ICollection<UserContributionDiscItem> Items { get; set; } = new HashSet<UserContributionDiscItem>();
}

public class UserContributionDiscItem
{
    public int Id { get; set; }
    [JsonIgnore]
    public UserContributionDisc Disc { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public int ChapterCount { get; set; } = 0;
    public int SegmentCount { get; set; } = 0;
    public string SegmentMap { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string Episode { get; set; } = string.Empty;
    public ICollection<UserContributionChapter> Chapters { get; set; } = new HashSet<UserContributionChapter>();
    public ICollection<UserContributionAudioTrack> AudioTracks { get; set; } = new HashSet<UserContributionAudioTrack>();
}

public class UserContributionChapter
{
    public int Id { get; set; }
    public int Index { get; set; }
    public string Title { get; set; }
    [JsonIgnore]
    public UserContributionDiscItem Item { get; set; }
}

public class UserContributionAudioTrack
{
    public int Id { get; set; }
    public int Index { get; set; }
    public string Title { get; set; }
    [JsonIgnore]
    public UserContributionDiscItem Item { get; set; }
}

public class  UserContributionDiscHashItem
{
    public int Id { get; set; }
    [JsonIgnore]
    public UserContribution UserContribution { get; set; }
    public string DiscHash { get; set; }
    public int Index { get; set; }
    public string Name { get; set; }
    public DateTime CreationTime { get; set; }
    public long Size { get; set; }
}