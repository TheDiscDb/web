namespace TheDiscDb.Services.EditSuggestions;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using TheDiscDb.Web.Data;

/// <summary>Email/display-name for an edit-suggestion notification recipient.</summary>
public readonly record struct EditSuggestionRecipient(string? Email, string? DisplayName);

/// <summary>
/// Resolves a suggester's contact details from their user id. Edit-suggestion
/// notifications fire from the service layer (review happens server-side, the
/// user isn't on the page), so we look the address up rather than receiving it
/// from a Razor component. Abstracted behind an interface so the suggestion
/// services stay unit-testable without a real <see cref="UserManager{TUser}"/>.
/// </summary>
public interface IEditSuggestionRecipientResolver
{
    Task<EditSuggestionRecipient> ResolveAsync(string? userId, CancellationToken cancellationToken = default);
}

public sealed class EditSuggestionRecipientResolver(UserManager<TheDiscDbUser> userManager)
    : IEditSuggestionRecipientResolver
{
    public async Task<EditSuggestionRecipient> ResolveAsync(string? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return new EditSuggestionRecipient(null, null);
        }

        var user = await userManager.FindByIdAsync(userId);
        return new EditSuggestionRecipient(user?.Email, user?.UserName);
    }
}
