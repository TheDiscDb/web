using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

public class CreateApiKeyModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Owner email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address.")]
    public string OwnerEmail { get; set; } = string.Empty;

    public string? Roles { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public bool LogUsage { get; set; } = true;
}

public class EditApiKeyModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Owner email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address.")]
    public string OwnerEmail { get; set; } = string.Empty;

    public string? Roles { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsActive { get; set; }

    public bool LogUsage { get; set; }
}

public class ApiKeyRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string? Roles { get; set; }
    public bool IsActive { get; set; }
    public bool LogUsage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class ApiKeys : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private IMemoryCache Cache { get; set; } = null!;

    private SqlServerDataContext database = default!;
    private IQueryable<ApiKeyRow>? ApiKeyList { get; set; }

    // Create dialog
    private bool showCreateDialog;
    private readonly CreateApiKeyModel createModel = new();

    // Key reveal dialog
    private bool showKeyRevealDialog;
    private string createdPlainTextKey = string.Empty;

    // Edit dialog
    private bool showEditDialog;
    private readonly EditApiKeyModel editModel = new();

    // Revoke dialog
    private bool showRevokeDialog;
    private ApiKeyRow? revokeTarget;

    protected override async Task OnInitializedAsync()
    {
        await RefreshList();
    }

    private async Task RefreshList()
    {
        if (database != null)
        {
            await database.DisposeAsync();
        }

        database = await DbFactory.CreateDbContextAsync();

        ApiKeyList = database.ApiKeys
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyRow
            {
                Id = k.Id,
                Name = k.Name,
                KeyPrefix = k.KeyPrefix,
                KeyHash = k.KeyHash,
                OwnerEmail = k.OwnerEmail,
                Roles = k.Roles,
                IsActive = k.IsActive,
                LogUsage = k.LogUsage,
                CreatedAt = k.CreatedAt,
                ExpiresAt = k.ExpiresAt,
                LastUsedAt = k.LastUsedAt
            });
    }

    // ── Create ──

    private void ShowCreateDialog()
    {
        createModel.Name = string.Empty;
        createModel.OwnerEmail = string.Empty;
        createModel.Roles = null;
        createModel.ExpiresAt = null;
        createModel.LogUsage = true;
        showCreateDialog = true;
    }

    private async Task HandleCreate()
    {
        var plainTextKey = ApiKey.GeneratePlainTextKey();
        var roles = string.IsNullOrWhiteSpace(createModel.Roles)
            ? null
            : createModel.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var apiKey = ApiKey.Create(
            plainTextKey,
            createModel.Name.Trim(),
            createModel.OwnerEmail.Trim(),
            roles,
            createModel.ExpiresAt,
            createModel.LogUsage);

        database.ApiKeys.Add(apiKey);
        await database.SaveChangesAsync();

        showCreateDialog = false;
        createdPlainTextKey = plainTextKey;
        showKeyRevealDialog = true;

        await RefreshList();
    }

    private void CloseKeyRevealDialog()
    {
        showKeyRevealDialog = false;
        createdPlainTextKey = string.Empty;
    }

    // ── Edit ──

    private void ShowEditDialog(ApiKeyRow row)
    {
        editModel.Id = row.Id;
        editModel.Name = row.Name;
        editModel.OwnerEmail = row.OwnerEmail;
        editModel.Roles = row.Roles;
        editModel.ExpiresAt = row.ExpiresAt;
        editModel.IsActive = row.IsActive;
        editModel.LogUsage = row.LogUsage;
        showEditDialog = true;
    }

    private async Task HandleEdit()
    {
        var key = await database.ApiKeys.FindAsync(editModel.Id);
        if (key == null)
        {
            showEditDialog = false;
            return;
        }

        key.Name = editModel.Name.Trim();
        key.OwnerEmail = editModel.OwnerEmail.Trim();
        key.Roles = string.IsNullOrWhiteSpace(editModel.Roles) ? null : editModel.Roles.Trim();
        key.ExpiresAt = editModel.ExpiresAt;
        key.IsActive = editModel.IsActive;
        key.LogUsage = editModel.LogUsage;

        await database.SaveChangesAsync();

        // Always invalidate cache so changes to roles, expiry, logging, etc. take effect immediately
        InvalidateCache(key);

        showEditDialog = false;
        await RefreshList();
    }

    // ── Revoke ──

    private void ShowRevokeDialog(ApiKeyRow row)
    {
        revokeTarget = row;
        showRevokeDialog = true;
    }

    private void CancelRevoke()
    {
        revokeTarget = null;
        showRevokeDialog = false;
    }

    private async Task ConfirmRevoke()
    {
        if (revokeTarget == null)
        {
            return;
        }

        var key = await database.ApiKeys.FindAsync(revokeTarget.Id);
        if (key != null)
        {
            key.IsActive = false;
            await database.SaveChangesAsync();
            InvalidateCache(key);
        }

        showRevokeDialog = false;
        revokeTarget = null;
        await RefreshList();
    }

    private void InvalidateCache(ApiKey key)
    {
        Cache.Remove($"apikey:{key.KeyHash}");
        Cache.Remove($"apikey-id:{key.Id}");
    }

    public async ValueTask DisposeAsync() => await database.DisposeAsync();
}
