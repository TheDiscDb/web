using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TheDiscDb.Web.Email;

public static class MailgunClientExtensions
{
    public static IServiceCollection AddMailgunClient(this IServiceCollection services)
    {
        services.Configure<MailgunOptions>(options =>
        {
            var config = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
            config.GetSection("Mailgun").Bind(options);
        });

        services.AddHttpClient(nameof(MailgunClient), (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<MailgunOptions>>().CurrentValue;

            var baseUri = new Uri(options.BaseUrl);
            if (!string.Equals(baseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Mailgun BaseUrl must use HTTPS to protect the API key in transit. Got: {options.BaseUrl}");
            }

            client.BaseAddress = baseUri;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{options.ApiKey}")));
        });

        services.AddTransient<MailgunClient>();

        return services;
    }
}