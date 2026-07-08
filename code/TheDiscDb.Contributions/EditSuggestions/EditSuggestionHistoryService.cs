namespace TheDiscDb.Services.EditSuggestions;

using System;
using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Web.Data;

public interface IEditSuggestionHistoryService
{
    Task RecordCreatedAsync(int suggestionId, string userId, CancellationToken cancellationToken = default);

    Task RecordStatusChangedAsync(
        int suggestionId,
        string userId,
        EditSuggestionStatus oldStatus,
        EditSuggestionStatus newStatus,
        CancellationToken cancellationToken = default);

    Task RecordChangeStatusChangedAsync(
        int suggestionId,
        int changeId,
        string userId,
        EditSuggestionChangeStatus oldStatus,
        EditSuggestionChangeStatus newStatus,
        string? adminNote = null,
        CancellationToken cancellationToken = default);

    Task RecordWithdrawnAsync(int suggestionId, string userId, CancellationToken cancellationToken = default);

    Task RecordMessageAsync(
        int suggestionId,
        string userId,
        EditSuggestionHistoryType messageType,
        CancellationToken cancellationToken = default);
}

public sealed class EditSuggestionHistoryService(SqlServerDataContext database) : IEditSuggestionHistoryService
{
    public async Task RecordCreatedAsync(int suggestionId, string userId, CancellationToken cancellationToken)
    {
        database.EditSuggestionHistory.Add(new EditSuggestionHistory
        {
            SuggestionId = suggestionId,
            TimeStamp = DateTimeOffset.UtcNow,
            Description = "Suggestion created",
            UserId = userId,
            Type = EditSuggestionHistoryType.Created,
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordStatusChangedAsync(
        int suggestionId,
        string userId,
        EditSuggestionStatus oldStatus,
        EditSuggestionStatus newStatus,
        CancellationToken cancellationToken)
    {
        database.EditSuggestionHistory.Add(new EditSuggestionHistory
        {
            SuggestionId = suggestionId,
            TimeStamp = DateTimeOffset.UtcNow,
            Description = $"Status changed from **{oldStatus}** to **{newStatus}**",
            UserId = userId,
            Type = EditSuggestionHistoryType.StatusChanged,
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordChangeStatusChangedAsync(
        int suggestionId,
        int changeId,
        string userId,
        EditSuggestionChangeStatus oldStatus,
        EditSuggestionChangeStatus newStatus,
        string? adminNote,
        CancellationToken cancellationToken)
    {
        var description = $"Change status: **{oldStatus}** → **{newStatus}**";
        if (!string.IsNullOrWhiteSpace(adminNote))
        {
            description += $" — {adminNote}";
        }

        database.EditSuggestionHistory.Add(new EditSuggestionHistory
        {
            SuggestionId = suggestionId,
            ChangeId = changeId,
            TimeStamp = DateTimeOffset.UtcNow,
            Description = description,
            UserId = userId,
            Type = EditSuggestionHistoryType.ChangeStatusChanged,
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordWithdrawnAsync(int suggestionId, string userId, CancellationToken cancellationToken)
    {
        database.EditSuggestionHistory.Add(new EditSuggestionHistory
        {
            SuggestionId = suggestionId,
            TimeStamp = DateTimeOffset.UtcNow,
            Description = "Suggestion withdrawn by user",
            UserId = userId,
            Type = EditSuggestionHistoryType.Withdrawn,
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordMessageAsync(
        int suggestionId,
        string userId,
        EditSuggestionHistoryType messageType,
        CancellationToken cancellationToken)
    {
        database.EditSuggestionHistory.Add(new EditSuggestionHistory
        {
            SuggestionId = suggestionId,
            TimeStamp = DateTimeOffset.UtcNow,
            Description = messageType == EditSuggestionHistoryType.AdminMessage
                ? "Admin message added"
                : "User message added",
            UserId = userId,
            Type = messageType,
        });
        await database.SaveChangesAsync(cancellationToken);
    }
}
