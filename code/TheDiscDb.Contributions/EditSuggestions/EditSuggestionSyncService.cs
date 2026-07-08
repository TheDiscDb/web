namespace TheDiscDb.Services.EditSuggestions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

/// <summary>
/// Exposes applied-but-unsynced changes for the batch file-sync tool, and provides
/// a way for the tool to mark changes as synced once the resulting PR merges.
/// </summary>
public interface IEditSuggestionSyncService
{
    /// <summary>Returns changes that have been applied to the DB but not yet synced to the data repo.</summary>
    Task<IReadOnlyList<EditSuggestionChange>> GetUnsyncedChangesAsync(int take = 100, CancellationToken cancellationToken = default);

    /// <summary>Marks the specified changes as synced (e.g. after the ContributionBuddy PR merges).</summary>
    Task MarkSyncedAsync(IReadOnlyList<int> changeIds, CancellationToken cancellationToken = default);
}

public sealed class EditSuggestionSyncService(SqlServerDataContext database) : IEditSuggestionSyncService
{
    public async Task<IReadOnlyList<EditSuggestionChange>> GetUnsyncedChangesAsync(
        int take,
        CancellationToken cancellationToken)
    {
        return await database.EditSuggestionChanges
            .Where(c => c.Status == EditSuggestionChangeStatus.Applied && c.SyncedToFilesAt == null)
            .OrderBy(c => c.AppliedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkSyncedAsync(IReadOnlyList<int> changeIds, CancellationToken cancellationToken)
    {
        if (changeIds is null || changeIds.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await database.EditSuggestionChanges
            .Where(c => changeIds.Contains(c.Id) && c.Status == EditSuggestionChangeStatus.Applied)
            .ExecuteUpdateAsync(
                setter => setter.SetProperty(c => c.SyncedToFilesAt, now),
                cancellationToken);
    }
}
