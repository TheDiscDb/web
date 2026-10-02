using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using TheDiscDb.OpticalDiscManifest.Generation;
using TheDiscDb.OpticalDiscManifest.Models;

namespace TheDiscDb.DiscScan.Cli;

public static class Program
{
    public const int Success = 0;
    public const int BadArguments = 2;
    public const int NoDiscFound = 3;
    public const int GenerationFailure = 4;
    public const int ValidationFailure = 5;

    private const string ProducerName = "thediscdb-scan";
    private const string ProducerUri = "https://thediscdb.com/";
    private const string UploadHint = "Upload this file on the Disc Manifest tab at https://thediscdb.com/contribute";

    public static async Task<int> Main(string[] args)
        => await RunAsync(args, Console.Out, Console.Error, CancellationToken.None);

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (!CommandLineOptions.TryParse(args, out CommandLineOptions? options, out string parseError))
        {
            error.WriteLine(parseError);
            error.WriteLine();
            WriteHelp(error);
            return BadArguments;
        }

        if (options.ShowHelp)
        {
            WriteHelp(output);
            return Success;
        }

        if (options.ShowVersion)
        {
            output.WriteLine(GetVersion());
            return Success;
        }

        if (options.List)
        {
            return ListDiscs(output, error);
        }

        if (options.Path is null)
        {
            error.WriteLine("A disc path is required unless --list, --help, or --version is specified.");
            return BadArguments;
        }

        if (!LocalDiscSource.TryOpen(options.Path, out LocalDiscSource? source, out string sourceError)
            || source is null)
        {
            error.WriteLine(sourceError);
            return NoDiscFound;
        }

        foreach (string warning in source.Warnings)
        {
            WriteWarning(error, warning, options.Quiet);
        }

        if (options.Verbose)
        {
            error.WriteLine($"Scanning {source.DisplayPath}");
            error.WriteLine($"Found {source.Files.Count} files");
        }

        ManifestGenerationResult result;
        try
        {
            var generator = new OpticalDiscManifestGenerator();
            result = await generator.GenerateAsync(
                new ManifestGenerationRequest
                {
                    Files = source.Files,
                    ProducerName = ProducerName,
                    ProducerVersion = GetVersion(),
                    ProducerUri = ProducerUri,
                    ReportProgress = options.Quiet
                        ? null
                        : message => error.WriteLine(message),
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error.WriteLine($"Generation failed: {ex.Message}");
            return GenerationFailure;
        }

        foreach (ManifestDiagnostic diagnostic in result.Diagnostics)
        {
            WriteDiagnostic(error, diagnostic, options.Quiet);
        }

        if (result.Diagnostics.Any(item => string.Equals(item.Severity, "error", StringComparison.OrdinalIgnoreCase)))
        {
            error.WriteLine("Generation failed because one or more disc structure files could not be read or parsed.");
            return GenerationFailure;
        }

        if (!result.Validation.IsValid)
        {
            error.WriteLine("Generated manifest failed schema validation:");
            foreach (string validationError in result.Validation.Errors)
            {
                error.WriteLine($"  {validationError}");
            }

            return ValidationFailure;
        }

        string destination = options.OutputPath ?? CreateDefaultOutputPath(source.Label);
        if (!options.Stdout)
        {
            if (File.Exists(destination) && !options.Force)
            {
                error.WriteLine($"Output file already exists: {destination}");
                error.WriteLine("Use --force to overwrite it.");
                return BadArguments;
            }

            await File.WriteAllBytesAsync(destination, result.Json, cancellationToken);
        }
        else
        {
            await output.WriteAsync(System.Text.Encoding.UTF8.GetString(result.Json));
            await output.WriteLineAsync();
        }

        if (!options.Quiet)
        {
            string location = options.Stdout ? "stdout" : Path.GetFullPath(destination);
            error.WriteLine($"Wrote {location}");
            error.WriteLine(CreateSummary(result.Manifest.Disc));
            error.WriteLine(UploadHint);
        }

        return Success;
    }

    public static string CreateDefaultOutputPath(string label)
        => Path.Combine(Environment.CurrentDirectory, $"{SanitizeFileName(label)}.odm.json");

    public static string GetVersion()
        => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Program).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

    private static int ListDiscs(TextWriter output, TextWriter error)
    {
        var candidates = LocalDiscSource.ListCandidates();
        if (candidates.Count == 0)
        {
            error.WriteLine("No ready DVD or Blu-ray disc roots were found.");
            return NoDiscFound;
        }

        foreach (LocalDiscCandidate candidate in candidates)
        {
            output.WriteLine($"{candidate.Path}\t{candidate.Label}\t{candidate.Format}");
        }

        return Success;
    }

    private static string CreateSummary(ManifestDisc disc)
    {
        int titleCount = disc.Titles?.Count ?? 0;
        string label = titleCount == 1 ? "title" : "titles";
        return $"{disc.Format}, {titleCount} {label}";
    }

    private static void WriteDiagnostic(TextWriter error, ManifestDiagnostic diagnostic, bool quiet)
    {
        if (quiet && !string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string path = string.IsNullOrWhiteSpace(diagnostic.Path) ? string.Empty : $"{diagnostic.Path}: ";
        error.WriteLine($"{diagnostic.Severity}: {path}{diagnostic.Code}: {diagnostic.Message}");
    }

    private static void WriteWarning(TextWriter error, string message, bool quiet)
    {
        if (!quiet)
        {
            error.WriteLine($"warning: {message}");
        }
    }

    private static string SanitizeFileName(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(label.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        sanitized = sanitized.Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "disc" : sanitized;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine($"""
            TheDiscDb optical disc manifest scanner

            Usage:
              thediscdb-scan <path> [-o|--output <file>] [--stdout] [--force] [-q|--quiet] [-v|--verbose]
              thediscdb-scan --list
              thediscdb-scan --help
              thediscdb-scan --version

            Arguments:
              <path>                Disc root containing VIDEO_TS or BDMV, a drive letter, or a mounted volume.

            Options:
              -o, --output <file>   Output .odm.json path. Defaults to <volume-label-or-folder-name>.odm.json in the current directory.
              --stdout              Write JSON to stdout instead of a file.
              --force               Overwrite an existing output file.
              --list, list          Detect mounted DVD/Blu-ray discs and print path, label, and format.
              -q, --quiet           Suppress progress and warning output.
              -v, --verbose         Print extra scan details.
              --help, -h, -?        Show help.
              --version             Show version.

            Exit codes:
              {Success} success, {BadArguments} bad arguments, {NoDiscFound} no disc found, {GenerationFailure} generation failure, {ValidationFailure} validation failure.
            """);
    }

    private sealed record CommandLineOptions(
        string? Path,
        string? OutputPath,
        bool Stdout,
        bool Force,
        bool List,
        bool Quiet,
        bool Verbose,
        bool ShowHelp,
        bool ShowVersion)
    {
        public static bool TryParse(
            string[] args,
            [NotNullWhen(true)] out CommandLineOptions? options,
            out string error)
        {
            string? path = null;
            string? outputPath = null;
            bool stdout = false;
            bool force = false;
            bool list = false;
            bool quiet = false;
            bool verbose = false;
            bool help = false;
            bool version = false;
            error = string.Empty;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--help" or "-h" or "-?":
                        help = true;
                        break;
                    case "--version":
                        version = true;
                        break;
                    case "--list" or "list":
                        list = true;
                        break;
                    case "--stdout":
                        stdout = true;
                        break;
                    case "--force":
                        force = true;
                        break;
                    case "--quiet" or "-q":
                        quiet = true;
                        break;
                    case "--verbose" or "-v":
                        verbose = true;
                        break;
                    case "--output" or "-o":
                        if (i + 1 >= args.Length)
                        {
                            options = null;
                            error = $"{arg} requires a file path.";
                            return false;
                        }

                        outputPath = args[++i];
                        break;
                    default:
                        if (arg.StartsWith('-'))
                        {
                            options = null;
                            error = $"Unknown option: {arg}";
                            return false;
                        }

                        if (path is not null)
                        {
                            options = null;
                            error = $"Unexpected argument: {arg}";
                            return false;
                        }

                        path = arg;
                        break;
                }
            }

            if (stdout && outputPath is not null)
            {
                options = null;
                error = "--stdout cannot be combined with --output.";
                return false;
            }

            if (quiet && verbose)
            {
                options = null;
                error = "--quiet cannot be combined with --verbose.";
                return false;
            }

            options = new CommandLineOptions(path, outputPath, stdout, force, list, quiet, verbose, help, version);
            return true;
        }
    }
}
