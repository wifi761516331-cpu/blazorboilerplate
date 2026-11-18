using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Attachments;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Modules.Attachments.Frontend.Pages;

public partial class Attachments
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    private const int MaxFilesPerBatch = 10;
    private int _maxUploadSizeMb = 11;
    private string _allowedExtensions = ".jpg,.jpeg,.png,.webp,.gif,.bmp,.pdf,.txt,.md,.json,.csv,.zip,.doc,.docx,.xls,.xlsx";
    private string _storageProvider = "Local";
    private long MaxUploadSizeBytes => (long)_maxUploadSizeMb * 1024 * 1024;
    private string _storageLabel => string.Equals(_storageProvider, "S3", StringComparison.OrdinalIgnoreCase)
        ? L["attachments.storage.s3"]
        : L["attachments.storage.local"];
    private string AcceptExtensions => _uploadKind is AttachmentKind.UserProfileImage or AttachmentKind.ProductPrimaryImage
        ? ".jpg,.jpeg,.png,.webp,.gif,.bmp"
        : _allowedExtensions;
    private string AllowedExtensionsDisplay => string.IsNullOrWhiteSpace(_allowedExtensions)
        ? L["attachments.dropzone.allowedFallback"]
        : string.Join(", ", AcceptExtensions.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private sealed record PendingUploadFile(string Name, string ContentType, long Size, byte[]? Bytes, bool TooLarge);

    private List<AttachmentDto> _items = [];
    private readonly List<PendingUploadFile> _pendingFiles = [];
    private AttachmentKind _uploadKind = AttachmentKind.General;
    private string? _ownerIdText;
    private string _filterKind = string.Empty;
    private string _searchText = string.Empty;
    private bool _loading;
    private bool _uploading;
    private int _uploadDone;
    private int _uploadTotal;
    private int _inputKey;
    private string? _loadError;

    private bool _uploadOpen;
    private bool _previewOpen;
    private AttachmentDto? _previewItem;
    private string? _previewUrl;

    private bool _deleteOpen;
    private AttachmentDto? _deleteTarget;

    private IReadOnlyList<AttachmentDto> FilteredItems => string.IsNullOrWhiteSpace(_searchText)
        ? _items
        : _items.Where(x => x.FileName.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        await RefreshUploadLimitAsync();
        await LoadAsync();
    }

    private async Task RefreshUploadLimitAsync()
    {
        try
        {
            var settings = await Api.GetPublicSystemSettingsAsync();
            if (settings.MaxUploadSizeMb > 0)
                _maxUploadSizeMb = settings.MaxUploadSizeMb;
            if (!string.IsNullOrWhiteSpace(settings.AllowedExtensions))
                _allowedExtensions = settings.AllowedExtensions;
            if (!string.IsNullOrWhiteSpace(settings.StorageProvider))
                _storageProvider = settings.StorageProvider;
        }
        catch
        {
            // 获取失败时保留当前或默认上限
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadError = null;
        try
        {
            AttachmentKind? kind = Enum.TryParse<AttachmentKind>(_filterKind, out var parsed) ? parsed : null;
            _items = [.. await Api.GetAttachmentsAsync(kind)];
        }
        catch (Exception ex)
        {
            _loadError = L.GetString("attachments.error.loadFailed", ex.Message);
            Toast.Error(_loadError);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task HandleFilesSelectedAsync(InputFileChangeEventArgs e)
    {
        foreach (var file in e.GetMultipleFiles(MaxFilesPerBatch))
        {
            if (_pendingFiles.Count >= MaxFilesPerBatch)
            {
                Toast.Warning(L.GetString("attachments.error.maxFiles", MaxFilesPerBatch));
                break;
            }
            if (_pendingFiles.Any(x => x.Name == file.Name && x.Size == file.Size))
                continue;

            if (file.Size > MaxUploadSizeBytes)
            {
                _pendingFiles.Add(new PendingUploadFile(file.Name, file.ContentType, file.Size, null, TooLarge: true));
                continue;
            }

            try
            {
                await using var stream = file.OpenReadStream(MaxUploadSizeBytes);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                _pendingFiles.Add(new PendingUploadFile(file.Name, file.ContentType, file.Size, ms.ToArray(), TooLarge: false));
            }
            catch (Exception ex)
            {
                Toast.Error(L.GetString("attachments.error.readFile", file.Name, ex.Message));
            }
        }
        _inputKey++;
    }

    private void RemovePendingFile(PendingUploadFile file) => _pendingFiles.Remove(file);

    private async Task StartUploadAsync()
    {
        if (_pendingFiles.Count == 0)
            return;

        Guid? ownerId = null;
        if (_uploadKind == AttachmentKind.ProductPrimaryImage && !string.IsNullOrWhiteSpace(_ownerIdText))
        {
            if (!Guid.TryParse(_ownerIdText.Trim(), out var parsedOwner))
            {
                Toast.Warning(L["attachments.error.invalidGuid"]);
                return;
            }
            ownerId = parsedOwner;
        }

        _uploading = true;
        _uploadDone = 0;
        _uploadTotal = _pendingFiles.Count;
        var successCount = 0;

        try
        {
            foreach (var file in _pendingFiles.ToArray())
            {
                if (file.TooLarge || file.Bytes is null)
                {
                    Toast.Warning(L.GetString("attachments.warning.skippedTooLarge", file.Name, _maxUploadSizeMb));
                    _uploadDone++;
                    continue;
                }

                try
                {
                    await using var stream = new MemoryStream(file.Bytes, writable: false);
                    await Api.UploadAttachmentAsync(stream, file.Name, file.ContentType, _uploadKind, ownerId);
                    successCount++;
                    _pendingFiles.Remove(file);
                }
                catch (Exception ex)
                {
                    Toast.Error(L.GetString("attachments.error.uploadFailed", file.Name, ex.Message));
                }
                finally
                {
                    _uploadDone++;
                    StateHasChanged();
                }
            }

            if (successCount > 0)
            {
                Toast.Success(L.GetString("attachments.success.uploaded", successCount));
                await LoadAsync();
                if (_pendingFiles.Count == 0)
                    _uploadOpen = false;
            }
        }
        finally
        {
            _uploading = false;
        }
    }

    private async Task OpenUploadModal()
    {
        await RefreshUploadLimitAsync();
        _pendingFiles.Clear();
        _inputKey++;
        _uploadOpen = true;
    }

    private void OpenPreviewIfImage(AttachmentDto item, string previewUrl)
    {
        if (IsImage(item))
            OpenPreview(item, previewUrl);
    }

    private void OpenPreview(AttachmentDto item, string previewUrl)
    {
        _previewItem = item;
        _previewUrl = previewUrl;
        _previewOpen = true;
    }

    private Task ClosePreviewAsync()
    {
        _previewOpen = false;
        return Task.CompletedTask;
    }

    private async Task CopyUrlAsync(string url)
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", url);
            Toast.Success(L["attachments.success.copied"]);
        }
        catch
        {
            Toast.Info(L.GetString("attachments.info.link", url));
        }
    }

    private void ConfirmDelete(AttachmentDto item)
    {
        _deleteTarget = item;
        _deleteOpen = true;
    }

    private async Task ExecuteDeleteAsync()
    {
        if (_deleteTarget is null)
            return;

        await Api.DeleteAttachmentAsync(_deleteTarget.Id, _deleteTarget.Kind);
        _items.RemoveAll(x => x.Id == _deleteTarget.Id && x.Kind == _deleteTarget.Kind);
        Toast.Success(L.GetString("attachments.success.deleted", _deleteTarget.FileName));
        _deleteOpen = false;
        _deleteTarget = null;
    }

    private static bool IsImage(AttachmentDto item) =>
        item.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private string GetKindLabel(AttachmentKind kind) => kind switch
    {
        AttachmentKind.UserProfileImage => L["attachments.kind.userAvatar"],
        AttachmentKind.ProductPrimaryImage => L["attachments.kind.productImage"],
        _ => L["attachments.kind.general"]
    };

    private static string GetExtensionLabel(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return string.IsNullOrWhiteSpace(ext) ? "FILE" : ext.TrimStart('.').ToUpperInvariant();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}
