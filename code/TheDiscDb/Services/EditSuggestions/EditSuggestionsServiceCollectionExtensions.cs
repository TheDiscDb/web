namespace TheDiscDb.Services.EditSuggestions;

using Microsoft.Extensions.DependencyInjection;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.ReleaseFields;

/// <summary>
/// DI registration for the edit-suggestions / review-queue feature. Wires up the
/// <see cref="IChangeFactory"/> and every known <see cref="IChangeBuilder"/>.
/// New change types must be added here.
/// </summary>
public static class EditSuggestionsServiceCollectionExtensions
{
    public static IServiceCollection AddEditSuggestions(this IServiceCollection services)
    {
        services.AddSingleton<IChangeFactory, ChangeFactory>();

        // Registered change types. Add a line per new IChange implementation.
        services.AddSingleton<IChangeBuilder>(new ChangeBuilder<ReleaseFieldsDetails>(
            ReleaseFieldsUpdate.Key,
            d => new ReleaseFieldsUpdate(d)));

        return services;
    }
}
