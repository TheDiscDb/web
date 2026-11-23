namespace TheDiscDb.Client.Services;

using TheDiscDb.Client.Pages.Contribute;

public sealed record DiscSearchContribution(string Hash, DiscTitleMatch? Title, IReadOnlyList<string> Warnings);

public sealed class DiscSearchSession(DiscSearchService search, DiscScanUploader uploader) : IAsyncDisposable
{
    private DiscFileSelection? selection;
    private DiscScanResult? identifiers;
    private string? hash;
    private int revision;
    private bool disposed;

    public DiscSearchResult? Result { get; private set; }
    public bool CanRetry => this.hash is not null;
    public bool CanPrepareContribution => this.selection is not null && this.identifiers is not null && this.Result is not null;

    public async Task SearchAsync(DiscFileSelection selection, CancellationToken cancellationToken)
    {
        int current = await this.ResetAsync(selection);

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        var identifiers = await DiscScanner.ScanAsync(selection.Files, cancellationToken);
        if (!string.IsNullOrEmpty(identifiers.Error))
        {
            throw new InvalidOperationException(identifiers.Error);
        }
        if (identifiers.HashFiles.Count == 0 || identifiers.FingerprintFiles.Count == 0)
        {
            throw new InvalidOperationException("No hashable disc files were found. Select the disc root folder again.");
        }
        if (current != this.revision)
        {
            return;
        }

        this.identifiers = identifiers;
        this.hash = identifiers.CalculateContentHash();
        await this.LookupAsync(this.hash, current, cancellationToken);
    }

    public async Task SearchHashAsync(string hash, CancellationToken cancellationToken)
    {
        int current = await this.ResetAsync(null);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new InvalidOperationException("Provide a disc hash before searching.");
        }
        if (current != this.revision)
        {
            return;
        }

        this.hash = hash.Trim().ToUpperInvariant();
        await this.LookupAsync(this.hash, current, cancellationToken);
    }

    public async Task ClearAsync()
    {
        await this.ResetAsync(null);
    }

    public Task RetryAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        string hash = this.hash
            ?? throw new InvalidOperationException("Provide a disc hash or select the disc folder before searching.");
        this.Result = null;
        return this.LookupAsync(hash, ++this.revision, cancellationToken);
    }

    public async Task<DiscSearchContribution> PrepareContributionAsync(
        DiscTitleMatch? title, string baseUri, Action<string> reportProgress, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        var files = this.selection?.Files
            ?? throw new InvalidOperationException("Select the disc folder again before continuing.");
        var identifiers = this.identifiers
            ?? throw new InvalidOperationException("Select the disc folder again before continuing.");
        var result = this.Result
            ?? throw new InvalidOperationException("Retry the database search before continuing.");
        int current = this.revision;
        if (title is not null)
        {
            reportProgress("Checking the existing disc");
            var latest = await search.SearchAsync(result.Hash, cancellationToken);
            if (!title.CanContribute || !latest.Titles.Any(match =>
                ContributionContinuation.MatchesTitle(match.MediaType, match.ExternalId, title.MediaType!, title.ExternalId!)))
            {
                throw new InvalidOperationException("This release match is no longer available. Search again before adding a release.");
            }
        }
        this.EnsureCurrent(current, cancellationToken);

        var saved = await uploader.SaveDraftAsync(
            files, baseUri, reportProgress, cancellationToken, identifierScan: identifiers,
            isKnownDisc: title is null ? null : (_, _) => Task.FromResult(true), saveLocally: true);
        this.EnsureCurrent(current, cancellationToken);
        if (!saved.Result.Uploaded || saved.DiscHash is null)
        {
            throw new InvalidOperationException(saved.Result.Error ?? "The scan could not be saved. Select the disc folder again and retry.");
        }
        return new DiscSearchContribution(saved.DiscHash, title, saved.Result.Warnings);
    }

    private async Task LookupAsync(string hash, int current, CancellationToken cancellationToken)
    {
        var result = await search.SearchAsync(hash, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (current == this.revision)
        {
            this.Result = result;
        }
    }

    private async Task<int> ResetAsync(DiscFileSelection? selection)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        int current = ++this.revision;
        var previous = this.selection;
        this.selection = selection;
        this.identifiers = null;
        this.hash = null;
        this.Result = null;
        if (previous is not null)
        {
            await previous.DisposeAsync();
        }
        return current;
    }

    private void EnsureCurrent(int current, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (current != this.revision)
        {
            throw new OperationCanceledException("The selected disc has changed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        this.revision++;
        var selection = this.selection;
        this.selection = null;
        this.identifiers = null;
        this.hash = null;
        this.Result = null;
        if (selection is not null)
        {
            await selection.DisposeAsync();
        }
    }
}
