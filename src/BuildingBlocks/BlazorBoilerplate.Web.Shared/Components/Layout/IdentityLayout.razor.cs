using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Web.Shared.Localization;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Web.Shared.Components.Layout;

// 基类 LayoutComponentBase / 接口 IDisposable 由 .razor 的 @inherits / @implements 声明，
// 此处只放字段与方法——避免两个分部声明指定不同基类导致 CS0263。
public partial class IdentityLayout
{
    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    private bool _dark;

    protected override void OnInitialized()
    {
        FrontendLoc.Changed += HandleLocalizationChanged;
    }

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
    }

    private void HandleLocalizationChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        FrontendLoc.Changed -= HandleLocalizationChanged;
    }
}
