namespace TheDiscDb.Services.EditSuggestions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for edit-suggestion email notifications. The real Mailgun
/// implementation (<see cref="EditSuggestionNotificationService"/>) lives in the web app;
/// the interface and no-op fallback live in the shared TheDiscDb.Contributions library.
/// </summary>
public static class EditSuggestionNotificationsServiceCollectionExtensions
{
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
