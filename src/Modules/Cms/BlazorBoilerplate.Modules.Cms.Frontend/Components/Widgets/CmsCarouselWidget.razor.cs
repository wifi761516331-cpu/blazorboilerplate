using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsCarouselWidget : IDisposable
{
    public sealed record CarouselSlide(
        string? ImageUrl = null,
        string? Title = null,
        string? Subtitle = null,
        string? ButtonText = null,
        string? ButtonHref = "#contact");

    public sealed record CarouselProps(
        int Height = 420,
        bool AutoPlay = true,
        int IntervalMs = 5000,
        List<CarouselSlide>? Slides = null);

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter] public string PropsJson { get; set; } = "{}";
    [Parameter] public bool IsDesignMode { get; set; }
    [Parameter] public EventCallback<string> OnNavigateAnchor { get; set; }

    private CarouselProps _props = new();
    private List<CarouselSlide> _slides = [];
    private int _activeIndex;
    private Timer? _timer;

    protected override void OnParametersSet()
    {
        _props = CmsJson.DeserializeOrDefault(PropsJson, new CarouselProps());
        _slides = _props.Slides ?? [];
        if (_activeIndex >= _slides.Count)
            _activeIndex = 0;

        _timer?.Dispose();
        _timer = null;
        if (!IsDesignMode && _props.AutoPlay && _slides.Count > 1)
        {
            var interval = Math.Clamp(_props.IntervalMs, 2000, 15000);
            _timer = new Timer(_ =>
            {
                _activeIndex = (_activeIndex + 1) % _slides.Count;
                _ = InvokeAsync(StateHasChanged);
            }, null, interval, interval);
        }
    }

    private void Prev()
    {
        if (_slides.Count == 0) return;
        _activeIndex = (_activeIndex - 1 + _slides.Count) % _slides.Count;
    }

    private void Next()
    {
        if (_slides.Count == 0) return;
        _activeIndex = (_activeIndex + 1) % _slides.Count;
    }

    private static string SlideBgStyle(CarouselSlide slide) =>
        string.IsNullOrWhiteSpace(slide.ImageUrl)
            ? "background: linear-gradient(135deg, var(--cms-pri, #0f6cbd), color-mix(in srgb, var(--cms-acc, #115ea3) 80%, #0b192c));"
            : $"background-image: linear-gradient(rgba(15,23,42,0.45), rgba(15,23,42,0.45)), url('{slide.ImageUrl}'); background-size: cover; background-position: center;";

    private async Task NavigateSlideAsync(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return;
        if (href.StartsWith('#'))
        {
            await OnNavigateAnchor.InvokeAsync(href[1..]);
            return;
        }
        Navigation.NavigateTo(href);
    }

    public void Dispose() => _timer?.Dispose();
}
