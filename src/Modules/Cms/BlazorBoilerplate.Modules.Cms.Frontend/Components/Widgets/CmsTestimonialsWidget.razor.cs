using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsTestimonialsWidget
{
    public sealed record TestimonialItem(
        string Quote = "上线效率非常高，模块化架构让我们的官网和业务系统保持了高度统一。",
        string Author = "张总",
        string? Role = "某科技集团 CTO",
        string? AvatarUrl = null);

    public sealed record TestimonialsProps(
        string? Heading = "客户声音",
        List<TestimonialItem>? Items = null);

    [Parameter] public string PropsJson { get; set; } = "{}";
    private TestimonialsProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new TestimonialsProps());
}
