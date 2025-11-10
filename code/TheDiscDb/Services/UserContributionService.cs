using System.Text;
using FluentResults;
using MakeMkv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sqids;
using TheDiscDb.Data.Import;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services.Server;

public class UserContributionService : IUserContributionService
{
    private readonly IDbContextFactory<SqlServerDataContext> dbContextFactory;
    private readonly UserManager<TheDiscDbUser> userManager;
    private readonly SqidsEncoder<int> idEncoder;
    private readonly IStaticAssetStore assetStore;

    public UserContributionService(IDbContextFactory<SqlServerDataContext> dbContextFactory, UserManager<TheDiscDbUser> userManager, SqidsEncoder<int> idEncoder, IStaticAssetStore assetStore)
    {
        this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        this.userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        this.idEncoder = idEncoder ?? throw new ArgumentNullException(nameof(idEncoder));
        this.assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
    }

    #region Contributions

    public async Task<FluentResults.Result<List<UserContribution>>> GetUserContributions(string userId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var contributions = await dbContext.UserContributions
                .AsNoTracking()
                .Where(c => c.UserId == userId)
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .OrderByDescending(c => c.Created)
                .ToListAsync(cancellationToken);
            return contributions;
        }
    }

    public async Task<FluentResults.Result<CreateContributionResponse>> CreateContribution(string userId, CreateContributionRequest request, CancellationToken cancellationToken)
    {
        var contribution = new UserContribution
        {
            UserId = userId,
            Created = DateTimeOffset.UtcNow,
            Asin = request.Asin,
            ExternalId = request.ExternalId,
            ExternalProvider = request.ExternalProvider,
            MediaType = request.MediaType,
            ReleaseDate = request.ReleaseDate,
            Status = UserContributionStatus.Pending,
            FrontImageUrl = request.FrontImageUrl,
            BackImageUrl = request.BackImageUrl,
            Upc = request.Upc,
            ReleaseTitle = request.ReleaseTitle,
            ReleaseSlug = request.ReleaseSlug,
            Locale = request.Locale,
            RegionCode = request.RegionCode
        };

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            dbContext.UserContributions.Add(contribution);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Save images to blob storage? (or just store the url and do that later)

        return new CreateContributionResponse { ContributionId = this.idEncoder.Encode(contribution.Id) };
    }

    public async Task<FluentResults.Result<UserContribution>> GetContribution(string contributionId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            int id = idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            return contribution;
        }
    }

    public async Task<Result> DeleteContribution(string contributionId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            dbContext.UserContributions.Remove(contribution);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateContribution(string contributionId, CreateContributionRequest request, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);
            if (contribution == null)
            {
                return Result.Fail(contributionId + " not found");
            }

            contribution.Asin = request.Asin;
            contribution.ExternalId = request.ExternalId;
            contribution.ExternalProvider = request.ExternalProvider;
            contribution.MediaType = request.MediaType;
            contribution.ReleaseDate = request.ReleaseDate;
            contribution.FrontImageUrl = request.FrontImageUrl;
            contribution.BackImageUrl = request.BackImageUrl;
            contribution.Upc = request.Upc;
            contribution.ReleaseTitle = request.ReleaseTitle;
            contribution.ReleaseSlug = request.ReleaseSlug;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    #endregion

    #region Discs

    public async Task<FluentResults.Result<List<UserContributionDisc>>> GetDiscs(string contributionId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            int id = idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (contribution == null)
            {
                throw new Exception("Contribution not found");
            }

            return contribution.Discs.OrderBy(d => d.Index).ToList();
        }
    }

    public async Task<Result> SaveDiscLogs(string contributionId, string discId, string logs, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            int id = idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            // TODO: Parse the logs and clean them of PII (like drive serial numbers etc)
            // Also validate the logs are from makemkv and not something else

            //Save the logs in blob storage
            byte[] byteArray = Encoding.UTF8.GetBytes(logs);
            using (MemoryStream memoryStream = new MemoryStream(byteArray))
            {
                memoryStream.Position = 0; // Reset position to the beginning
                await this.assetStore.Save(memoryStream, $"{contributionId}/{this.idEncoder.Encode(disc.Id)}-logs.txt", ContentTypes.TextContentType, cancellationToken);
            }

            disc.LogsUploaded = true;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();

        //TODO: Notify the client a disc has been added? (to prevent the client having to poll)
    }

    public async Task<FluentResults.Result<DiscLogResponse>> GetDiscLogs(string contributionId, string discId, CancellationToken cancellationToken)
    {
        // TODO: Check the user owns the contribution

        try
        {
            var blob = await this.assetStore.Download($"{contributionId}/{discId}-logs.txt", cancellationToken);
            if (blob == null)
            {
                // TODO: Try getting the status code in a middleware and changing the response code
                return Result.Fail(new FluentResults.Error("Logs not found").WithMetadata("StatusCode", StatusCodes.Status404NotFound));
            }

            string text = blob.ToString();
            var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var parsed = LogParser.Parse(lines);
            var orgainized = LogParser.Organize(parsed);

            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var decodedDiscId = this.idEncoder.Decode(discId).Single();
            UserContributionDisc? disc = null;
            await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
            {
                disc = await dbContext.UserContributionDiscs
                    .Include(c => c.Items)
                        .ThenInclude(d => d.Chapters)
                    .Include(c => c.Items)
                        .ThenInclude(d => d.AudioTracks)
                    .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);
            }

            return Result.Ok(new DiscLogResponse
            {
                Info = orgainized,
                Disc = disc
            });
        }
        catch (Exception e)
        {
            return Result.Fail(new FluentResults.Error("Error getting disc logs").CausedBy(e));
        }
    }

    public async Task<FluentResults.Result<SaveDiscResponse>> CreateDisc(string contributionId, SaveDiscRequest request, CancellationToken cancellationToken)
    {
        var disc = new UserContributionDisc
        {
            ContentHash = request.ContentHash,
            Index = request.Index,
            Format = request.Format,
            Name = request.Name,
            Slug = request.Slug
        };

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            var existingDisc = contribution?.Discs.FirstOrDefault(d => d.ContentHash == disc.ContentHash);
            if (existingDisc != null)
            {
                existingDisc.Index = request.Index;
                existingDisc.Format = request.Format;
                existingDisc.Name = request.Name;
                existingDisc.Slug = request.Slug;
            }
            else
            {
                contribution!.Discs.Add(disc);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new SaveDiscResponse { DiscId = this.idEncoder.Encode(disc.Id) };
    }

    public async Task<Result> UpdateDisc(string contributionId, string discId, SaveDiscRequest request, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);
            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);
            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            disc.ContentHash = request.ContentHash;
            disc.Index = request.Index;
            disc.Format = request.Format;
            disc.Name = request.Name;
            disc.Slug = request.Slug;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<Result> DeleteDisc(string contributionId, string discId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            contribution.Discs.Remove(disc);
            dbContext.UserContributionDiscs.Remove(disc);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<FluentResults.Result<DiscStatusResponse>> CheckDiskUploadStatus(string discId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = await dbContext.UserContributionDiscs
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == realDiscId, cancellationToken);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            return new DiscStatusResponse
            {
                LogsUploaded = disc.LogsUploaded
            };
        }
    }

    #endregion

    #region Disc Items

    public async Task<FluentResults.Result<AddItemResponse>> AddItemToDisc(string contributionId, string discId, AddItemRequest request, CancellationToken cancellationToken)
    {
        var item = new UserContributionDiscItem
        {
            ChapterCount = request.ChapterCount,
            Description = request.Description,
            Duration = request.Duration,
            Size = request.Size,
            Name = request.Name,
            SegmentCount = request.SegmentCount,
            SegmentMap = request.SegmentMap,
            Source = request.Source,
            Type = request.Type
        };

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            disc.Items.Add(item);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AddItemResponse { ItemId = this.idEncoder.Encode(item.Id) };
    }

    public async Task<Result> DeleteItemFromDisc(string contributionId, string discId, string itemId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = idEncoder.Decode(itemId).Single();
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            disc.Items.Remove(item);
            dbContext.UserContributionDiscItems.Remove(item);

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return Result.Ok();
    }

    #endregion

    #region Chapters

    public async Task<FluentResults.Result<AddChapterResponse>> AddChapterToItem(string contributionId, string discId, string itemId, AddChapterRequest request, CancellationToken cancellationToken)
    {
        var chapter = new UserContributionChapter
        {
            Index = request.Index,
            Title = request.Title
        };
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);
            
            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = idEncoder.Decode(itemId).Single();
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);
            
            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            var existingChapter = item.Chapters.FirstOrDefault(c => c.Index == chapter.Index);
            if (existingChapter != null)
            {
                existingChapter.Title = chapter.Title;
            }
            else
            {
                item.Chapters.Add(chapter);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AddChapterResponse { ChapterId = this.idEncoder.Encode(chapter.Id) };
    }

    public async Task<Result> DeleteChapterFromItem(string contributionId, string discId, string itemId, string chapterId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);
            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }
            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = idEncoder.Decode(itemId).Single();
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            int realChapterId = idEncoder.Decode(chapterId).Single();
            var chapter = item.Chapters.FirstOrDefault(c => c.Id == realChapterId);

            if (chapter == null)
            {
                return Result.Fail($"Chapter {chapterId} not found");
            }

            item.Chapters.Remove(chapter);
            dbContext.UserContributionChapters.Remove(chapter);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateChapterInItem(string contributionId, string discId, string itemId, string chapterId, AddChapterRequest request, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = idEncoder.Decode(itemId).Single();
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            int realChapterId = idEncoder.Decode(chapterId).Single();
            var chapter = item.Chapters.FirstOrDefault(c => c.Id == realChapterId);

            if (chapter == null)
            {
                return Result.Fail($"Chapter {chapterId} not found");
            }

            chapter.Index = request.Index;
            chapter.Title = request.Title;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    #endregion

    #region Audio Tracks

    public async Task<FluentResults.Result<AddAudioTrackResponse>> AddAudioTrackToItem(string contributionId, string discId, string itemId, AddAudioTrackRequest request, CancellationToken cancellationToken)
    {
        var audioTrack = new UserContributionAudioTrack
        {
            Index = request.Index,
            Title = request.Title
        };

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = idEncoder.Decode(itemId).Single();
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            var existingTrack = item.AudioTracks.FirstOrDefault(c => c.Index == audioTrack.Index);
            if (existingTrack != null)
            {
                existingTrack.Title = audioTrack.Title;
            }
            else
            {
                item.AudioTracks.Add(audioTrack);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AddAudioTrackResponse { AudioTrackId = this.idEncoder.Encode(audioTrack.Id) };
    }

    public async Task<Result> DeleteAudioTrackFromItem(string contributionId, string discId, string itemId, string audioTrackId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = idEncoder.Decode(itemId).Single();
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            int realAudioTrackId = idEncoder.Decode(audioTrackId).Single();
            var audioTrack = item.AudioTracks.FirstOrDefault(a => a.Id == realAudioTrackId);

            if (audioTrack == null)
            {
                return Result.Fail($"Audio track {audioTrackId} not found");
            }

            item.AudioTracks.Remove(audioTrack);
            dbContext.UserContributionAudioTracks.Remove(audioTrack);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateAudioTrackInItem(string contributionId, string discId, string itemId, string audioTrackId, AddAudioTrackRequest request, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = idEncoder.Decode(itemId).Single();
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            int realAudioTrackId = idEncoder.Decode(audioTrackId).Single();
            var audioTrack = item.AudioTracks.FirstOrDefault(a => a.Id == realAudioTrackId);

            if (audioTrack == null)
            {
                return Result.Fail($"Audio track {audioTrackId} not found");
            }

            audioTrack.Index = request.Index;
            audioTrack.Title = request.Title;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    #endregion
}
