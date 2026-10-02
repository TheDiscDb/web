using System.CommandLine;
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
        var path = new Argument<string?>("path")
        {
            Description = "Disc root containing VIDEO_TS or BDMV, a drive letter, or a mounted volume.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var outputPath = new Option<string?>("--output", "-o")
        {
            Description = "Output .odm.json path. Defaults to <volume-label-or-folder-name>.odm.json in the current directory.",
            HelpName = "file"
        };
        var stdout = new Option<bool>("--stdout") { Description = "Write JSON to stdout instead of a file." };
        var force = new Option<bool>("--force") { Description = "Overwrite an existing output file." };
        var list = new Option<bool>("--list") { Description = "Detect mounted DVD/Blu-ray discs and print path, label, and format." };
        var quiet = new Option<bool>("--quiet", "-q") { Description = "Suppress progress and warning output." };
        var verbose = new Option<bool>("--verbose", "-v") { Description = "Print extra scan details." };

        var command = new RootCommand(
            $"TheDiscDb optical disc manifest scanner. Exit codes: {Success} success, {BadArguments} bad arguments, " +
            $"{NoDiscFound} no disc found, {GenerationFailure} generation failure, {ValidationFailure} validation failure.");
        command.Arguments.Add(path);
        command.Options.Add(list);
        var listCommand = new Command("list", list.Description!);
        command.Subcommands.Add(listCommand);

        foreach (var target in new Command[] { command, listCommand })
        {
            target.Options.Add(outputPath);
            target.Options.Add(stdout);
            target.Options.Add(force);
            target.Options.Add(quiet);
            target.Options.Add(verbose);
            target.Validators.Add(result =>
            {
                if (result.GetValue(stdout) && result.GetValue(outputPath) is not null)
                {
                    result.AddError("--stdout cannot be combined with --output.");
                }

                if (result.GetValue(quiet) && result.GetValue(verbose))
                {
                    result.AddError("--quiet cannot be combined with --verbose.");
                }
            });
        }

        command.Validators.Add(result =>
        {
            if (result.GetValue(path) is { } value && value.StartsWith('-'))
            {
                result.AddError($"Unknown option: {value}");
            }

            if (result.GetValue(path) is null && !result.GetValue(list))
            {
                result.AddError("A disc path is required unless --list, --help, or --version is specified.");
            }
        });
        command.SetAction((result, token) => result.GetValue(list)
            ? Task.FromResult(ListDiscs(output, error))
            : ScanAsync(new CommandLineOptions(
                result.GetValue(path)!,
                result.GetValue(outputPath),
                result.GetValue(stdout),
                result.GetValue(force),
                result.GetValue(quiet),
                result.GetValue(verbose)), output, error, token));
        listCommand.SetAction(_ => ListDiscs(output, error));

        var parsed = command.Parse(args);
        int exitCode = await parsed.InvokeAsync(new InvocationConfiguration
        {
            Output = output,
            Error = error,
            EnableDefaultExceptionHandler = false
        }, cancellationToken);
        return parsed.Errors.Count > 0 ? BadArguments : exitCode;
    }

    private static async Task<int> ScanAsync(
        CommandLineOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
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

    private sealed record CommandLineOptions(
        string Path,
        string? OutputPath,
        bool Stdout,
        bool Force,
        bool Quiet,
        bool Verbose);
}
