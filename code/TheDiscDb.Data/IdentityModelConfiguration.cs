namespace TheDiscDb.Web.Data;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

internal static class IdentityModelConfiguration
{
    public static void ConfigureIdentityModel(ModelBuilder modelBuilder)
    {
        // Taken from Asp.net core docs https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/security/authentication/customize-identity-model.md
        modelBuilder.Entity<TheDiscDbUser>(b =>
        {
            b.HasKey(u => u.Id);

            b.HasIndex(u => u.NormalizedUserName).HasDatabaseName("UserNameIndex").IsUnique();
            b.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex");

            b.ToTable("AspNetUsers");

            b.Property(u => u.ConcurrencyStamp).IsConcurrencyToken();

            b.Property(u => u.UserName).HasMaxLength(256);
            b.Property(u => u.NormalizedUserName).HasMaxLength(256);
            b.Property(u => u.Email).HasMaxLength(256);
            b.Property(u => u.NormalizedEmail).HasMaxLength(256);

            b.HasMany<IdentityUserClaim<string>>().WithOne().HasForeignKey(uc => uc.UserId).IsRequired();
            b.HasMany<IdentityUserLogin<string>>().WithOne().HasForeignKey(ul => ul.UserId).IsRequired();
            b.HasMany<IdentityUserToken<string>>().WithOne().HasForeignKey(ut => ut.UserId).IsRequired();
            b.HasMany<IdentityUserRole<string>>().WithOne().HasForeignKey(ur => ur.UserId).IsRequired();
        });

        modelBuilder.Entity<IdentityUserClaim<string>>(b =>
        {
            b.HasKey(uc => uc.Id);
            b.ToTable("AspNetUserClaims");
        });

        modelBuilder.Entity<IdentityUserLogin<string>>(b =>
        {
            b.HasKey(l => new { l.LoginProvider, l.ProviderKey });
            b.Property(l => l.LoginProvider).HasMaxLength(128);
            b.Property(l => l.ProviderKey).HasMaxLength(128);
            b.ToTable("AspNetUserLogins");
        });

        modelBuilder.Entity<IdentityUserToken<string>>(b =>
        {
            b.HasKey(t => new { t.UserId, t.LoginProvider, t.Name });
            b.Property(t => t.LoginProvider).HasMaxLength(128);
            b.Property(t => t.Name).HasMaxLength(128);
            b.ToTable("AspNetUserTokens");
        });

        modelBuilder.Entity<IdentityRole>(b =>
        {
            b.HasKey(r => r.Id);
            b.HasIndex(r => r.NormalizedName).HasDatabaseName("RoleNameIndex").IsUnique();
            b.ToTable("AspNetRoles");
            b.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();
            b.Property(u => u.Name).HasMaxLength(256);
            b.Property(u => u.NormalizedName).HasMaxLength(256);

            b.HasMany<IdentityUserRole<string>>().WithOne().HasForeignKey(ur => ur.RoleId).IsRequired();
            b.HasMany<IdentityRoleClaim<string>>().WithOne().HasForeignKey(rc => rc.RoleId).IsRequired();
        });

        modelBuilder.Entity<IdentityRoleClaim<string>>(b =>
        {
            b.HasKey(rc => rc.Id);
            b.ToTable("AspNetRoleClaims");
        });

        modelBuilder.Entity<IdentityUserRole<string>>(b =>
        {
            b.HasKey(r => new { r.UserId, r.RoleId });
            b.ToTable("AspNetUserRoles");
        });
    }
}
