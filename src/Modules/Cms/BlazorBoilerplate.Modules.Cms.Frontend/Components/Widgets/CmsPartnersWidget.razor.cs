using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsPartnersWidget
{
    public sealed record PartnerItem(string Name = "合作伙伴", string? LogoUrl = null, string? LinkUrl = null);
    public sealed record PartnersProps(string? Heading = "合作伙伴与生态客户", List<PartnerItem>? Items = null, string? Variant = null);

    [Parameter] public string PropsJson { get; set; } = "{}";
    private PartnersProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new PartnersProps());
}
