namespace TheDiscDb.Services.EditSuggestions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Web.Data;

public sealed class EditSuggestionService(
    SqlServerDataContext database,
    IChangeFactory changeFactory,
    IEditSuggestionHistoryService historyService,
    IEditSuggestionNotificationService? notifications = null,
    IEditSuggestionRecipientResolver? recipients = null) : IEditSuggestionService
{
    public async Task<EditSuggestion> SubmitAsync(
        string userId,
        EditSuggestionSource source,
        string? summary,
        IReadOnlyList<SubmitChangeInput> changes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        if (changes is null || changes.Count == 0)
        {
            throw new ArgumentException("At least one change is required.", nameof(changes));
        }

        // Validate all type keys are registered and all JSON payloads are valid
        // before persisting anything.
        IChange? firstChange = null;
        foreach (var input in changes)
        {
            if (!changeFactory.RegisteredTypeKeys.Contains(input.TypeKey))
            {
                throw new UnknownChangeTypeException(input.TypeKey);
            }

            // Materialize each change to validate the JSON is well-formed.
            // The first change also provides the bundle's TargetEntityKey.
            var instance = changeFactory.Create(input.TypeKey, input.ProposedJson);
            firstChange ??= instance;
        }

        var suggestion = new EditSuggestion
        {
            UserId = userId,
            Created = DateTimeOffset.UtcNow,
            Status = EditSuggestionStatus.Pending,
            Summary = summary,
            TargetEntityType = DeriveEntityType(changes[0].TypeKey),
            TargetEntityKey = firstChange!.TargetEntityKey,
            Source = source,
        };

        for (var i = 0; i < changes.Count; i++)
        {
            suggestion.Changes.Add(new EditSuggestionChange
            {
                Ordinal = i,
                Type = changes[i].TypeKey,
                ProposedJson = changes[i].ProposedJson,
                OriginalSnapshotJson = changes[i].OriginalSnapshotJson,
                Status = EditSuggestionChangeStatus.Pending,
            });
        }

        database.EditSuggestions.Add(suggestion);
        await database.SaveChangesAsync(cancellationToken);

        await historyService.RecordCreatedAsync(suggestion.Id, userId, cancellationToken);

        if (notifications is not null)
        {
            var recipient = recipients is null
                ? default
                : await recipients.ResolveAsync(userId, cancellationToken);
            await notifications.NotifySuggestionSubmittedAsync(
                suggestion, recipient.Email, recipient.DisplayName, cancellationToken);
        }

        return suggestion;
    }

    public async Task<EditSuggestion?> GetByIdAsync(int suggestionId, CancellationToken cancellationToken)
    {
        return await database.EditSuggestions
            .Include(s => s.Changes.OrderBy(c => c.Ordinal))
            .FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken);
    }

    public async Task<IReadOnlyList<EditSuggestion>> ListAsync(
        EditSuggestionListFilter filter,
        CancellationToken cancellationToken)
    {
        var query = database.EditSuggestions
            .Include(s => s.Changes.OrderBy(c => c.Ordinal))
            .AsQueryable();

        if (filter.MineOnly && filter.UserId is not null)
        {
            query = query.Where(s => s.UserId == filter.UserId);
        }

        if (filter.Statuses is { Length: > 0 })
        {
            query = query.Where(s => filter.Statuses.Contains(s.Status));
        }

        if (!string.IsNullOrEmpty(filter.TargetEntityType))
        {
            query = query.Where(s => s.TargetEntityType == filter.TargetEntityType);
        }

        if (!string.IsNullOrEmpty(filter.TargetEntityKey))
        {
            query = query.Where(s => s.TargetEntityKey == filter.TargetEntityKey);
        }

        return await query
            .OrderByDescending(s => s.Created)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(cancellationToken);
    }

    public async Task<EditSuggestion?> WithdrawAsync(
        int suggestionId,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var suggestion = await database.EditSuggestions
            .Include(s => s.Changes)
            .FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken);

        if (suggestion is null)
        {
            return null;
        }

        // Only the owner or an admin can withdraw.
        if (!isAdmin && suggestion.UserId != userId)
        {
            return null;
        }

        // Only pending/in-review suggestions can be withdrawn.
        if (suggestion.Status is not (EditSuggestionStatus.Pending or EditSuggestionStatus.InReview))
        {
            return null;
        }

        var oldStatus = suggestion.Status;
        suggestion.Status = EditSuggestionStatus.Withdrawn;
        await database.SaveChangesAsync(cancellationToken);

        await historyService.RecordWithdrawnAsync(suggestion.Id, userId, cancellationToken);
        await historyService.RecordStatusChangedAsync(
            suggestion.Id, userId, oldStatus, EditSuggestionStatus.Withdrawn, cancellationToken);

        return suggestion;
    }

    public async Task<EditSuggestionMessage> AddMessageAsync(
        int suggestionId,
        string fromUserId,
        string toUserId,
        string body,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var suggestion = await database.EditSuggestions
            .FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken)
            ?? throw new InvalidOperationException($"EditSuggestion {suggestionId} not found.");

        // Only the suggestion owner or an admin may post messages.
        if (!isAdmin && suggestion.UserId != fromUserId)
        {
            throw new UnauthorizedAccessException(
                $"User '{fromUserId}' is not the owner of suggestion {suggestionId} and is not an admin.");
        }

        var message = new EditSuggestionMessage
        {
            SuggestionId = suggestionId,
            FromUserId = fromUserId,
            ToUserId = toUserId,
            Message = body,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        database.EditSuggestionMessages.Add(message);
        await database.SaveChangesAsync(cancellationToken);

        var historyType = isAdmin
            ? EditSuggestionHistoryType.AdminMessage
            : EditSuggestionHistoryType.UserMessage;
        await historyService.RecordMessageAsync(suggestion.Id, fromUserId, historyType, cancellationToken);

        if (notifications is not null)
        {
            if (isAdmin)
            {
                var recipient = recipients is null
                    ? default
                    : await recipients.ResolveAsync(toUserId, cancellationToken);
                await notifications.NotifyMessageFromAdminAsync(
                    suggestion, body, recipient.Email, cancellationToken);
            }
            else
            {
                var sender = recipients is null
                    ? default
                    : await recipients.ResolveAsync(fromUserId, cancellationToken);
                await notifications.NotifyMessageFromUserAsync(
                    suggestion, body, sender.DisplayName, sender.Email, cancellationToken);
            }
        }

        return message;
    }

    private static string DeriveEntityType(string typeKey)
    {
        // Convention: type keys are dotted, e.g. "release.fields.update", "disc.fields.update",
        // "disc-item.fields.update", "chapter.update", "track.fields.update".
        if (typeKey.StartsWith("release.", StringComparison.Ordinal))
        {
            return "Release";
        }

        if (typeKey.StartsWith("disc-item.", StringComparison.Ordinal)
            || typeKey.StartsWith("chapter.", StringComparison.Ordinal)
            || typeKey.StartsWith("track.", StringComparison.Ordinal))
        {
            return "DiscItem";
        }

        if (typeKey.StartsWith("disc.", StringComparison.Ordinal))
        {
            return "Disc";
        }

        return "Unknown";
    }
}
