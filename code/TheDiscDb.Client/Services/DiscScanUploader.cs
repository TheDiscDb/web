namespace TheDiscDb.Client.Services;

using System.Net.Http.Headers;
using System.Text.Json;
using TheDiscDb.Client.Interop;
using TheDiscDb.OpticalDiscManifest.Models;

public sealed record DiscScanUploadResult(bool Cancelled, bool Uploaded, string? Error, IReadOnlyList<string> Warnings)
{
    public static DiscScanUploadResult Cancel() => new(true, false, null, []);

    public static DiscScanUploadResult Failure(string error, IReadOnlyList<string>? warnings = null)
        => new(false, false, error, warnings ?? []);
}

/// <summary>
/// Lets the user pick a disc folder, generates an Optical Disc Manifest in the browser and uploads it.
/// </summary>
public sealed class DiscScanUploader
{
    private readonly DiscDirectoryPicker picker;
    private readonly BrowserOpticalDiscManifestScanner scanner;
    private readonly HttpClient httpClient;

    public DiscScanUploader(DiscDirectoryPicker picker, BrowserOpticalDiscManifestScanner scanner, HttpClient httpClient)
    {
        this.picker = picker;
        this.scanner = scanner;
        this.httpClient = httpClient;
    }

    // Warm the interop module up front. An awaited import inside the click handler yields the event
    // loop and drops the browser's transient user activation, which makes the picker refuse to open.
    public ValueTask PreloadAsync(CancellationToken cancellationToken)
        => this.picker.PreloadAsync(cancellationToken);

    public async Task<DiscScanUploadResult> ScanAndUploadAsync(
        string manifestUri,
        Action onScanning,
        Action<string> onProgress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> warnings = [];
        try
        {
            await using var selection = await this.picker.PickAsync(cancellationToken, onSelectionCommitted: onScanning);
            if (selection is null)
            {
                return DiscScanUploadResult.Cancel();
            }

            // Let the busy state paint before the enumeration blocks the renderer.
            await Task.Yield();

            ManifestGenerationResult result = await this.scanner.ScanAsync(selection.Files, onProgress, cancellationToken);
            if (!result.Validation.IsValid)
            {
                // The generator validates against the same schema the upload endpoint enforces,
                // so posting this would only fail again server-side.
                return DiscScanUploadResult.Failure(
                    "The scan produced a manifest that does not satisfy the disc manifest schema: "
                    + string.Join(" ", result.Validation.Errors.Take(3)));
            }

            warnings = result.Diagnostics
                .Where(diagnostic => diagnostic.Severity is "error" or "warning")
                .Select(diagnostic => diagnostic.Message)
                .Distinct(StringComparer.Ordinal)
                .Take(10)
                .ToArray();

            onProgress("Uploading disc manifest");

            using var content = new ByteArrayContent(result.Json);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var response = await this.httpClient.PostAsync(manifestUri, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                return DiscScanUploadResult.Failure(ExtractErrorDetail(body, "disc manifest"), warnings);
            }

            return new DiscScanUploadResult(false, true, null, warnings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DiscScanUploadResult.Cancel();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return DiscScanUploadResult.Failure($"Could not scan the selected disc folder: {ex.Message}", warnings);
        }
    }

    public static string ExtractErrorDetail(string responseBody, string description)
    {
        string fallback = $"An error occurred uploading the {description}.";
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
            {
                var text = detail.GetString();
                if (string.IsNullOrEmpty(text))
                {
                    return fallback;
                }

                // The ProblemDetails detail contains a wrapper like:
                // "Unable to save disc logs...\r\nErrors:\r\n- Actual error message"
                // Extract just the error line(s) after "Errors:" for a cleaner message.
                var errorsIndex = text.IndexOf("Errors:", StringComparison.OrdinalIgnoreCase);
                if (errorsIndex >= 0)
                {
                    var errorLines = text[(errorsIndex + "Errors:".Length)..]
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(l => l.TrimStart('-', ' '))
                        .Where(l => !string.IsNullOrEmpty(l));
                    var joined = string.Join(" ", errorLines);
                    return string.IsNullOrEmpty(joined) ? fallback : joined;
                }

                return text;
            }
        }
        catch
        {
            // Not valid JSON, fall through
        }

        return fallback;
    }
}
