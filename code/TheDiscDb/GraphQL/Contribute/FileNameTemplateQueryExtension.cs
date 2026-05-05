using System.Security.Claims;
using HotChocolate.Authorization;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Naming;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute;

[ExtendObjectType(typeof(ContributionQuery))]
public class FileNameTemplateQueryExtension
{
    /// <summary>
    /// Returns the current user's file-name template overrides. Item types
    /// that are not present in the result fall back to the built-in defaults
    /// in <see cref="DefaultFileNameTemplates"/> on the client.
    /// </summary>
    [Authorize]
    public async Task<List<UserFileNameTemplate>> GetMyFileNameTemplates(
        SqlServerDataContext database,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return [];
        }

        return await database.UserFileNameTemplates
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.ItemType)
            .ToListAsync(cancellationToken);
    }
}
