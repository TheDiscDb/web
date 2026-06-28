namespace TheDiscDb.Services.EditSuggestions;

using Microsoft.Extensions.Configuration;
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

        // Add types
        services.AddSingleton<IChangeBuilder>(new ChangeBuilder<ChapterDetails>(
            ChapterAdd.Key,
            (d, opts) => new ChapterAdd(d, opts)));
        services.AddSingleton<IChangeBuilder>(new ChangeBuilder<DiscItemFieldsDetails>(
            DiscItemAdd.Key,
            (d, opts) => new DiscItemAdd(d, opts)));
        services.AddSingleton<IChangeBuilder>(new ChangeBuilder<TrackFieldsDetails>(
            TrackAdd.Key,
            (d, opts) => new TrackAdd(d, opts)));

        // Delete types
        services.AddSingleton<IChangeBuilder>(new ChangeBuilder<ChapterDeleteDetails>(
            ChapterDelete.Key,
            (d, opts) => new ChapterDelete(d, opts)));
        services.AddSingleton<IChangeBuilder>(new ChangeBuilder<DiscItemDeleteDetails>(
            DiscItemDelete.Key,
            (d, opts) => new DiscItemDelete(d, opts)));
        services.AddSingleton<IChangeBuilder>(new ChangeBuilder<TrackDeleteDetails>(
            TrackDelete.Key,
            (d, opts) => new TrackDelete(d, opts)));

        // Application services.
        services.AddScoped<IEditSuggestionHistoryService, EditSuggestionHistoryService>();
        services.AddScoped<IEditSuggestionRecipientResolver, EditSuggestionRecipientResolver>();
        services.AddScoped<IEditSuggestionService, EditSuggestionService>();
        services.AddScoped<IEditSuggestionReviewService, EditSuggestionReviewService>();
        services.AddScoped<IEditSuggestionSyncService, EditSuggestionSyncService>();

        return services;
    }

    /// <summary>
    /// Registers edit-suggestion email notifications. Every call site is wired, but
    /// the sender ships dormant: the real Mailgun implementation is used only when
    /// Mailgun is configured (<c>Mailgun:ApiKey</c> present) AND at least one
    /// audience switch under <c>EditSuggestions:Notifications</c>
    /// (<c>NotifyAdmins</c> / <c>NotifyUsers</c>) is true. Otherwise a no-op
    /// implementation is registered so the call sites are harmless. The real
    /// service additionally gates each send per audience, so enabling only
    /// <c>NotifyAdmins</c> emails the admin without ever mailing users.
    /// </summary>
    public static IServiceCollection AddEditSuggestionNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("EditSuggestions:Notifications");
        services.Configure<EditSuggestionNotificationOptions>(section);

        var mailgunConfigured = !string.IsNullOrEmpty(configuration.GetValue<string>("Mailgun:ApiKey"));
        var notificationOptions = section.Get<EditSuggestionNotificationOptions>() ?? new EditSuggestionNotificationOptions();

        if (mailgunConfigured && notificationOptions.AnyEnabled)
        {
            services.AddTransient<IEditSuggestionNotificationService, EditSuggestionNotificationService>();
        }
        else
        {
            services.AddTransient<IEditSuggestionNotificationService, NullEditSuggestionNotificationService>();
        }

        return services;
    }
}
