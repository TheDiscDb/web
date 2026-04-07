using Fantastic.FileSystem;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDiscDb.Data.Import;
using TheDiscDb.Web.Data;

namespace TheDiscDb.DatabaseMigration;

public class DataSeeder
{
    private readonly DataImporter dataImporter;
    private readonly IFileSystem fileSystem;
    private readonly IOptions<DatabaseMigrationOptions> options;
    private readonly RoleManager<IdentityRole> roleManager;
    private readonly UserManager<TheDiscDbUser> userManager;
    private readonly SqlServerDataContext dbContext;
    private readonly IConfiguration configuration;
    private readonly ILogger<DataSeeder> logger;

    public DataSeeder(DataImporter dataImporter, IFileSystem fileSystem, IOptions<DatabaseMigrationOptions> options, RoleManager<IdentityRole> roleManager, UserManager<TheDiscDbUser> userManager, SqlServerDataContext dbContext, IConfiguration configuration, ILogger<DataSeeder> logger)
    {
        this.dataImporter = dataImporter ?? throw new ArgumentNullException(nameof(dataImporter));
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
        this.userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        this.dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SeedDataAsync(CancellationToken cancellationToken)
    {
        var plan = await CreateSeedPlan(cancellationToken);
        await SeedItems(plan.Movies, cancellationToken);
        await SeedItems(plan.Series, cancellationToken);
        await SeedItems(plan.Sets, cancellationToken);
    }

    public async Task<SeedPlan> CreateSeedPlan(CancellationToken cancellationToken)
    {
        var movies = await GetRandomSubdirectories(
            this.fileSystem.Path.Combine(options.Value.DataDirectoryRoot, "movie"),
            options.Value.MaxItemsToImportPerMediaType,
            cancellationToken);
        var series = await GetRandomSubdirectories(
            this.fileSystem.Path.Combine(options.Value.DataDirectoryRoot, "series"),
            options.Value.MaxItemsToImportPerMediaType,
            cancellationToken);
        var sets = await GetRandomSubdirectories(
            this.fileSystem.Path.Combine(options.Value.DataDirectoryRoot, "sets"),
            options.Value.MaxItemsToImportPerMediaType,
            cancellationToken);

        return new SeedPlan(movies.ToList(), series.ToList(), sets.ToList());
    }

    public async Task SeedItems(IEnumerable<string> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            this.logger.LogInformation("Importing {Path}", item);
            try
            {
                await dataImporter.Import(item, cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Unable to import {Path}", item);
            }
        }
    }

    private async Task SeedFromFolder(string name, CancellationToken cancellationToken)
    {
        var items = await GetRandomSubdirectories(this.fileSystem.Path.Combine(options.Value.DataDirectoryRoot, name), options.Value.MaxItemsToImportPerMediaType, cancellationToken);
        await SeedItems(items, cancellationToken);
    }

    private async Task<IEnumerable<string>> GetRandomSubdirectories(string directory, int max, CancellationToken cancellationToken)
    {
        var subDirectories = await this.fileSystem.Directory.GetDirectories(directory, cancellationToken);
        var randomized = subDirectories.OrderBy(i => Guid.NewGuid()).Take(max);
        return randomized;
    }

    public async Task SeedUsers(CancellationToken cancellationToken)
    {
        string[] roles = { DefaultRoles.Administrator, DefaultRoles.Contributor };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var adminUser = await userManager.FindByEmailAsync("luke@foust.com");
        if (adminUser != null)
        {
            bool isAdmin = await userManager.IsInRoleAsync(adminUser, DefaultRoles.Administrator);

            if (!isAdmin)
            {
                await userManager.AddToRoleAsync(adminUser, DefaultRoles.Administrator);
            }
        }

        // var regularUser = await userManager.FindByEmailAsync("web@thediscdb.com");
        // if (regularUser == null)
        // {
        //     regularUser = new TheDiscDbUser
        //     {
        //         Email = "web@thediscdb.com",
        //         EmailConfirmed = true,
        //         UserName = "thediscdb"
        //     };

        //     await userManager.CreateAsync(regularUser);
        //     await userManager.AddToRoleAsync(regularUser, DefaultRoles.Contributor);
        // }
    }

    public async Task SeedApiKeys(CancellationToken cancellationToken)
    {
        var apiKeySection = configuration.GetSection("GraphQL:ApiKeyAuthentication");

        await SeedApiKey(
            apiKeySection.GetValue<string>("AdminApiKey"),
            "Seeded Admin Key",
            "system@thediscdb.com",
            [DefaultRoles.Administrator],
            logUsage: false,
            cancellationToken);

        await SeedApiKey(
            apiKeySection.GetValue<string>("PublicApiKey"),
            "Public Read-Only Key",
            "system@thediscdb.com",
            null,
            logUsage: false,
            cancellationToken);
    }

    private async Task SeedApiKey(string? plainTextKey, string name, string ownerEmail, string[]? roles, bool logUsage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(plainTextKey))
        {
            logger.LogInformation("No key configured for '{Name}' — skipping", name);
            return;
        }

        var keyHash = ApiKey.HashKey(plainTextKey);
        var existing = await dbContext.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == keyHash, cancellationToken);

        if (existing != null)
        {
            if (existing.LogUsage != logUsage)
            {
                existing.LogUsage = logUsage;
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Updated LogUsage to {LogUsage} for API key '{Name}'", logUsage, name);
            }
            else
            {
                logger.LogInformation("API key '{Name}' already exists — skipping", name);
            }
            return;
        }

        var apiKey = ApiKey.Create(plainTextKey, name, ownerEmail, roles, logUsage: logUsage);
        dbContext.ApiKeys.Add(apiKey);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded API key '{Name}' (prefix: {KeyPrefix})", name, apiKey.KeyPrefix);
    }
}

public record SeedPlan(
    IReadOnlyList<string> Movies,
    IReadOnlyList<string> Series,
    IReadOnlyList<string> Sets);
