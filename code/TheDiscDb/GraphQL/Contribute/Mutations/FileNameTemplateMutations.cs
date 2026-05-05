using System.Security.Claims;
using HotChocolate;
using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Naming;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    /// <summary>
    /// Upserts the current user's file-name template override for the given
    /// item type. Validates the template syntax via
    /// <see cref="NamingTemplate.Parse"/> and rejects unknown item types.
    /// </summary>
    [Error(typeof(AuthenticationException))]
    [Authorize]
    public async Task<UserFileNameTemplate> SetFileNameTemplate(
        string itemType,
        string template,
        SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken)
    {
        var user = principal.Principal ?? throw new AuthenticationException("No user principal available.");
        var userId = userManager.GetUserId(user);

        if (string.IsNullOrEmpty(userId))
        {
            throw new AuthenticationException("UserId not found");
        }

        if (!DefaultFileNameTemplates.IsKnownItemType(itemType))
        {
            throw new GraphQLException($"Unknown item type '{itemType}'.");
        }

        if (string.IsNullOrWhiteSpace(template))
        {
            throw new GraphQLException("Template cannot be empty. Delete the override to revert to the default.");
        }

        if (template.Length > 512)
        {
            throw new GraphQLException("Template cannot exceed 512 characters.");
        }

        var parseResult = NamingTemplate.Parse(template);
        if (!parseResult.IsSuccess)
        {
            var message = parseResult.Errors is { Count: > 0 }
                ? string.Join("; ", parseResult.Errors.Select(e => e.Message))
                : "Invalid template.";
            throw new GraphQLException($"Invalid template: {message}");
        }

        var existing = await database.UserFileNameTemplates
            .FirstOrDefaultAsync(t => t.UserId == userId && t.ItemType == itemType, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            existing = new UserFileNameTemplate
            {
                UserId = userId,
                ItemType = itemType,
                Template = template,
                UpdatedAt = now,
            };
            database.UserFileNameTemplates.Add(existing);
        }
        else
        {
            existing.Template = template;
            existing.UpdatedAt = now;
        }

        await database.SaveChangesAsync(cancellationToken);
        return existing;
    }

    /// <summary>
    /// Deletes the current user's override for <paramref name="itemType"/>,
    /// reverting them to the built-in default. Returns <c>true</c> when a row
    /// was deleted, <c>false</c> when no override existed.
    /// </summary>
    [Error(typeof(AuthenticationException))]
    [Authorize]
    public async Task<bool> DeleteFileNameTemplate(
        string itemType,
        SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken)
    {
        var user = principal.Principal ?? throw new AuthenticationException("No user principal available.");
        var userId = userManager.GetUserId(user);

        if (string.IsNullOrEmpty(userId))
        {
            throw new AuthenticationException("UserId not found");
        }

        if (!DefaultFileNameTemplates.IsKnownItemType(itemType))
        {
            throw new GraphQLException($"Unknown item type '{itemType}'.");
        }

        var existing = await database.UserFileNameTemplates
            .FirstOrDefaultAsync(t => t.UserId == userId && t.ItemType == itemType, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        database.UserFileNameTemplates.Remove(existing);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
}
