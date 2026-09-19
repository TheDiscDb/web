namespace TheDiscDb.Services.EditSuggestions;

using System.Text;
using TheDiscDb.Web.Data;

public static class EditSuggestionResolutionSummary
{
    public static string BuildRejectedChangesMarkdown(EditSuggestion suggestion)
    {
        var rejected = suggestion.Changes
            .Where(change => change.Status == EditSuggestionChangeStatus.Rejected)
            .OrderBy(change => change.Ordinal)
            .ToList();

        if (rejected.Count == 0)
        {
            return string.Empty;
        }

        var summary = new StringBuilder("### Changes not accepted");
        foreach (var change in rejected)
        {
            var reason = !string.IsNullOrWhiteSpace(change.AdminNote)
                ? change.AdminNote
                : !string.IsNullOrWhiteSpace(change.ConflictReason)
                    ? change.ConflictReason
                    : "No reason provided.";
            summary.AppendLine();
            summary.Append("- **");
            summary.Append(change.Type);
            summary.Append("**: ");
            summary.Append(reason);
        }

        return summary.ToString();
    }
}
