namespace TheDiscDb.Services.EditSuggestions;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Web.Data;

/// <summary>
/// User-facing operations: submit a bundle of changes, list own suggestions,
/// get a specific suggestion, withdraw, and send messages.
/// </summary>
public interface IEditSuggestionService
{
    /// <summary>
    /// Creates a new <see cref="EditSuggestion"/> with the given changes.
    /// Validates each change's type key against the <see cref="Data.Changes.IChangeFactory"/>.
    /// </summary>
    Task<EditSuggestion> SubmitAsync(
        string userId,
        EditSuggestionSource source,
        string? summary,
        IReadOnlyList<SubmitChangeInput> changes,
        CancellationToken cancellationToken = default);

    Task<EditSuggestion?> GetByIdAsync(int suggestionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists suggestions visible to the specified user. Non-admins see only
    /// their own; admins can see all.
    /// </summary>
    Task<IReadOnlyList<EditSuggestion>> ListAsync(
        EditSuggestionListFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws a pending suggestion. Only the owning user or an admin may withdraw.
    /// </summary>
    Task<EditSuggestion?> WithdrawAsync(int suggestionId, string userId, bool isAdmin, CancellationToken cancellationToken = default);

    Task<EditSuggestionMessage> AddMessageAsync(
        int suggestionId,
        string fromUserId,
        string toUserId,
        string body,
        bool isAdmin,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Input for a single change within a submission. The service validates that
/// <see cref="TypeKey"/> is registered and stores the JSON payloads.
/// </summary>
public sealed record SubmitChangeInput(
    string TypeKey,
    string ProposedJson,
    string? OriginalSnapshotJson);

/// <summary>
/// Filter criteria for listing suggestions. All fields are optional; null means "no filter".
/// </summary>
public sealed record EditSuggestionListFilter
{
    public string? UserId { get; init; }
    public bool MineOnly { get; init; } = true;
    public EditSuggestionStatus[]? Statuses { get; init; }
    public string? TargetEntityType { get; init; }
    public string? TargetEntityKey { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 25;
}
