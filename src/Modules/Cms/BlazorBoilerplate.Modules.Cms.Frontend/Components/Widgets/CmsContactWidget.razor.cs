using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using BlazorBoilerplate.Modules.Cms.Frontend.Services;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsContactWidget
{
    public sealed record ContactProps(
        string? Heading = null,
        string? Subheading = null,
        string? SubmitText = "提交咨询",
        string? Email = null,
        string? Phone = null,
        string? Address = null,
        string? Hours = null,
        string? MapEmbedUrl = null,
        string? MapLabel = null,
        string? Coordinates = null,
        string? AmapKey = "adc139d56406f3844c8f1cf1c6b65c41",
        string? SecurityJsCode = null,
        double Longitude = 113.6401,
        double Latitude = 34.72468,
        int Zoom = 15);

    [Inject]
    private CmsApiClient CmsApi { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Parameter]
    public string PropsJson { get; set; } = "{}";

    [Parameter]
    public string SiteKey { get; set; } = "default";

    [Parameter]
    public Guid? SectionId { get; set; }

    [Parameter]
    public bool IsDesignMode { get; set; }

    private ContactProps _props = new();
    private string _name = string.Empty;
    private string _email = string.Empty;
    private string _phone = string.Empty;
    private string _company = string.Empty;
    private string _message = string.Empty;
    private bool _submitting;
    private bool _submitted;

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new ContactProps());

    private async Task SubmitAsync()
    {
        if (IsDesignMode) return;
        if (string.IsNullOrWhiteSpace(_name) || string.IsNullOrWhiteSpace(_message))
        {
            Toast.Warning("请填写姓名与咨询内容。");
            return;
        }
        _submitting = true;
        try
        {
            var ok = await CmsApi.SubmitLeadAsync(new(SiteKey, SectionId, _name, _email, _phone, _company, _message));
            if (ok) _submitted = true;
            else Toast.Error("提交失败，请稍后重试。");
        }
        catch (Exception ex)
        {
            Toast.Error($"提交失败：{ex.Message}");
        }
        finally
        {
            _submitting = false;
        }
    }
}
