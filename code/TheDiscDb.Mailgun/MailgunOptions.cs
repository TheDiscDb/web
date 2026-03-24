namespace TheDiscDb.Web.Email;

public class MailgunOptions
{
    public string ApiKey { get; set; } = null!;
    public string Domain { get; set; } = null!;
    public string FromEmail { get; set; } = null!;
    public string BaseUrl { get; set; } = "https://api.mailgun.net/";
    public string AdminEmail { get; set; } = string.Empty;
}