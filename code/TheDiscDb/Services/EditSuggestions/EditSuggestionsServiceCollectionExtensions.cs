namespace TheDiscDb.Services.EditSuggestions;

using Microsoft.Extensions.DependencyInjection;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.Chapter;
using TheDiscDb.Data.Changes.DiscFields;
using TheDiscDb.Data.Changes.DiscItemFields;
using TheDiscDb.Data.Changes.ReleaseFields;
using TheDiscDb.Data.Changes.Track;

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
            (d, opts) => new ReleaseFieldsUpdate(d, opts)));
        services.AddSingleton<IChangeBuilder>(new ChangeBuilder<DiscFieldsDetails>(
            DiscFieldsUpdate.Key,
            (d, opts) => new DiscFieldsUpdate(d, opts)));
        services.AddSingleton<IChangeBuilder>(new ChangeBuilder<DiscItemFieldsDetails>(
            DiscItemFieldsUpdate.Key,
            (d, opts) => new DiscItemFieldsUpdate(d, opts)));
        services.AddSingleton<IChangeBuilder>(new ChangeBuilder<ChapterDetails>(
            ChapterUpdate.Key,
            (d, opts) => new ChapterUpdate(d, opts)));
        services.AddSingleton<IChangeBuilder>(new ChangeBuilder<TrackFieldsDetails>(
            TrackFieldsUpdate.Key,
            (d, opts) => new TrackFieldsUpdate(d, opts)));

        // Application services.
        services.AddScoped<IEditSuggestionHistoryService, EditSuggestionHistoryService>();
        services.AddScoped<IEditSuggestionService, EditSuggestionService>();
        services.AddScoped<IEditSuggestionReviewService, EditSuggestionReviewService>();
        services.AddScoped<IEditSuggestionSyncService, EditSuggestionSyncService>();

        return services;
    }
}
