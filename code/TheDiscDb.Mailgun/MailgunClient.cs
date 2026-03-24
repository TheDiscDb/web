using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace TheDiscDb.Web.Email;

public class MailgunClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IOptionsMonitor<MailgunOptions> options;

    public MailgunClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<MailgunOptions> options)
    {
        this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Sends an email via the Mailgun v3 Messages API.
    /// </summary>
    public async Task<MailgunSendResult> SendAsync(MailgunMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.To.Count == 0)
        {
            throw new ArgumentException("At least one recipient is required.", nameof(message));
        }

        if (string.IsNullOrWhiteSpace(message.Text) && string.IsNullOrWhiteSpace(message.Html))
        {
            throw new ArgumentException("At least one of Text or Html must be provided.", nameof(message));
        }

        var opts = options.CurrentValue;
        var client = httpClientFactory.CreateClient(nameof(MailgunClient));

        using var content = BuildFormContent(message, opts);

        using var response = await client.PostAsync(
            $"v3/{opts.Domain}/messages",
            content,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MailgunException(response.StatusCode, body);
        }

        return JsonSerializer.Deserialize<MailgunSendResult>(body, JsonOptions)
            ?? throw new MailgunException(response.StatusCode, body);
    }

    private static MultipartFormDataContent BuildFormContent(MailgunMessage message, MailgunOptions opts)
    {
        var form = new MultipartFormDataContent();

        form.Add(new StringContent(message.From ?? opts.FromEmail), "from");
        form.Add(new StringContent(string.Join(",", message.To)), "to");
        form.Add(new StringContent(message.Subject), "subject");

        if (message.Cc.Count > 0)
        {
            form.Add(new StringContent(string.Join(",", message.Cc)), "cc");
        }

        if (message.Bcc.Count > 0)
        {
            form.Add(new StringContent(string.Join(",", message.Bcc)), "bcc");
        }

        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            form.Add(new StringContent(message.Text), "text");
        }

        if (!string.IsNullOrWhiteSpace(message.Html))
        {
            form.Add(new StringContent(message.Html), "html");
        }

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            form.Add(new StringContent(message.ReplyTo), "h:Reply-To");
        }

        foreach (var tag in message.Tags)
        {
            form.Add(new StringContent(tag), "o:tag");
        }

        AddBoolOption(form, "o:tracking", message.Tracking);
        AddBoolOption(form, "o:tracking-clicks", message.TrackingClicks);
        AddBoolOption(form, "o:tracking-opens", message.TrackingOpens);
        AddBoolOption(form, "o:require-tls", message.RequireTls);

        if (message.TestMode)
        {
            form.Add(new StringContent("yes"), "o:testmode");
        }

        if (message.DeliveryTime.HasValue)
        {
            form.Add(new StringContent(message.DeliveryTime.Value.ToString("R", CultureInfo.InvariantCulture)), "o:deliverytime");
        }

        foreach (var (key, value) in message.CustomHeaders)
        {
            form.Add(new StringContent(value), $"h:{key}");
        }

        foreach (var (key, value) in message.CustomVariables)
        {
            form.Add(new StringContent(value), $"v:{key}");
        }

        return form;
    }

    private static void AddBoolOption(MultipartFormDataContent form, string name, bool? value)
    {
        if (value.HasValue)
        {
            form.Add(new StringContent(value.Value ? "yes" : "no"), name);
        }
    }
}
