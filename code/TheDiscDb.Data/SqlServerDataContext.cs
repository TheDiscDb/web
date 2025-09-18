namespace TheDiscDb.Web.Data;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
}

public class TheDiscDbUser : Microsoft.AspNetCore.Identity.IdentityUser
{
}