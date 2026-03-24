using System;
using System.Net;

namespace TheDiscDb.Web.Email;

/// <summary>
/// Thrown when the Mailgun API returns a non-success status code.
/// </summary>
public class MailgunException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }

    public MailgunException(HttpStatusCode statusCode, string responseBody)
        : base($"Mailgun API returned {(int)statusCode} ({statusCode}): {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public MailgunException(HttpStatusCode statusCode, string responseBody, Exception innerException)
        : base($"Mailgun API returned {(int)statusCode} ({statusCode}): {responseBody}", innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
