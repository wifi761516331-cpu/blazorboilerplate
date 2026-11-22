using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Attachments;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Media;

public partial class CmsMediaPickerDialog
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    [Parameter]
    public bool IsOpen { get; set; }

    [Parameter]
    public EventCallback<bool> IsOpenChanged { get; set; }

    [Parameter]
    public EventCallback<string> OnSelected { get; set; }

    private List<AttachmentDto> _items = [];
    private string _search = string.Empty;
    private bool _imagesOnly = true;
    private bool _loading;
    private bool _uploading;
    private bool _loadedOnce;

    private IReadOnlyList<AttachmentDto> FilteredItems => _items
        .Where(x => (!_imagesOnly || IsImage(x))
                    && (string.IsNullOrWhiteSpace(_search) || x.FileName.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(x => x.CreatedAtUtc)
        .ToList();

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !_loadedOnce)
        {
            _loadedOnce = true;
            await FrontendLoc.EnsureLoadedAsync();
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _items = [.. await Api.GetAttachmentsAsync()];
        }
        catch (Exception ex)
        {
            Toast.Error($"读取附件库失败：{ex.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task HandleUploadAsync(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file is null) return;
        _uploading = true;
        try
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024);
            var uploaded = await Api.UploadAttachmentAsync(stream, file.Name, file.ContentType, AttachmentKind.General);
            _items.Insert(0, uploaded);
            Toast.Success($"已上传 {uploaded.FileName}");
        }
        catch (Exception ex)
        {
            Toast.Error($"上传失败：{ex.Message}");
        }
        finally
        {
            _uploading = false;
        }
    }

    private async Task SelectAsync(AttachmentDto item)
    {
        await OnSelected.InvokeAsync(GetContentUrl(item));
        await CloseAsync();
    }

    private async Task HandleOpenChanged(bool open)
    {
        IsOpen = open;
        await IsOpenChanged.InvokeAsync(open);
    }

    private Task CloseAsync() => HandleOpenChanged(false);

    private static bool IsImage(AttachmentDto item) =>
        item.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        || item.FileName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase);

    private string GetContentUrl(AttachmentDto item) =>
        Api.GetAttachmentUrl(item.Id, item.Kind, download: false);
}
