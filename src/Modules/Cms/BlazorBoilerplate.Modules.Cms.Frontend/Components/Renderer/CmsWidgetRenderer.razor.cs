using System.Text;
using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using BlazorBoilerplate.Modules.Cms.Frontend.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Renderer;

public partial class CmsWidgetRenderer : IAsyncDisposable
{
    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Parameter, EditorRequired]
    public CmsPublishedWidget Widget { get; set; } = default!;

    [Parameter]
    public string SiteKey { get; set; } = "default";

    [Parameter]
    public Guid SectionId { get; set; }

    [Parameter]
    public bool IsDesignMode { get; set; }

    [Parameter]
    public bool IsSelected { get; set; }

    [Parameter]
    public EventCallback<string> OnNavigateAnchor { get; set; }

    [Parameter]
    public EventCallback<Guid> OnSelectWidget { get; set; }

    [Parameter]
    public EventCallback<(Guid WidgetId, int Delta)> OnMoveWidgetOrder { get; set; }

    [Parameter]
    public EventCallback<Guid> OnDeleteWidget { get; set; }

    [Parameter]
    public EventCallback<Guid> OnDuplicateWidget { get; set; }

    [Parameter]
    public EventCallback<Guid> OnDragStartWidget { get; set; }

    [Parameter]
    public EventCallback<(Guid SectionId, string ZoneId, Guid TargetWidgetId)> OnDropOnWidget { get; set; }

    [Parameter]
    public EventCallback<(Guid WidgetId, string? Width, string? Height)> OnResizeWidget { get; set; }

    [Parameter]
    public EventCallback<(Guid WidgetId, string OldText, string NewText)> OnInlineEditText { get; set; }

    [Parameter]
    public EventCallback<(Guid WidgetId, string OldImageUrl)> OnInlineEditImage { get; set; }

    private ElementReference _boxRef;
    private IJSObjectReference? _resizeJs;
    private IJSObjectReference? _inlineEditJs;
    private IJSObjectReference? _animJs;
    private DotNetObjectReference<CmsWidgetRenderer>? _selfRef;
    private (Guid WidgetId, bool IsDesignMode, string? Animation)? _lastAnimState;

    private Type ComponentType => CmsWidgetCatalog.Resolve(Widget.WidgetType);

    private CmsWidgetStyle ParsedStyle => CmsJson.DeserializeOrDefault(Widget.StyleJson, new CmsWidgetStyle());

    /// <summary>当前 widget 是否配置了入场动画（用于启用滚动触发观测）。</summary>
    private bool HasAnimation =>
        !string.IsNullOrWhiteSpace(ParsedStyle.Animation) &&
        !string.Equals(ParsedStyle.Animation, "none", StringComparison.OrdinalIgnoreCase);

    private string StyleClass
    {
        get
        {
            var s = ParsedStyle;
            // 设计器内不应用入场动画，避免 widget 因 opacity:0 不可见而无法编辑。
            var anim = IsDesignMode || string.IsNullOrWhiteSpace(s.Animation) ||
                       string.Equals(s.Animation, "none", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $"cms-anim-{s.Animation.Trim().ToLowerInvariant()}";
            return $"{s.CustomCssClass ?? string.Empty} {anim}".Trim();
        }
    }

    private string InlineStyle
    {
        get
        {
            var s = ParsedStyle;
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(s.BackgroundColor)) sb.Append($"background-color:{s.BackgroundColor};");
            if (!string.IsNullOrWhiteSpace(s.BackgroundImageUrl)) sb.Append($"background-image:url('{s.BackgroundImageUrl}');background-size:cover;background-position:center;");
            if (!string.IsNullOrWhiteSpace(s.TextColor)) sb.Append($"color:{s.TextColor};");
            if (!string.IsNullOrWhiteSpace(s.Padding)) sb.Append($"padding:{s.Padding};");
            if (!string.IsNullOrWhiteSpace(s.Margin)) sb.Append($"margin:{s.Margin};");
            if (!string.IsNullOrWhiteSpace(s.TextAlign)) sb.Append($"text-align:{s.TextAlign};");
            if (!string.IsNullOrWhiteSpace(s.BorderRadius)) sb.Append($"border-radius:{s.BorderRadius};");
            if (!string.IsNullOrWhiteSpace(s.Width)) sb.Append($"width:{s.Width};max-width:100%;box-sizing:border-box;");
            if (!string.IsNullOrWhiteSpace(s.Height)) sb.Append($"height:{s.Height};overflow:auto;box-sizing:border-box;");
            return sb.ToString();
        }
    }

    private Dictionary<string, object> WidgetParameters
    {
        get
        {
            var dict = new Dictionary<string, object>
            {
                ["PropsJson"] = Widget.PropsJson
            };
            if (string.Equals(Widget.WidgetType, "cms.hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Widget.WidgetType, "cms.button", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Widget.WidgetType, "cms.carousel", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Widget.WidgetType, "cms.pricing", StringComparison.OrdinalIgnoreCase))
                dict["OnNavigateAnchor"] = OnNavigateAnchor;
            if (string.Equals(Widget.WidgetType, "cms.contact", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Widget.WidgetType, "cms.carousel", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(Widget.WidgetType, "cms.contact", StringComparison.OrdinalIgnoreCase))
                {
                    dict["SiteKey"] = SiteKey;
                    dict["SectionId"] = SectionId;
                }
                dict["IsDesignMode"] = IsDesignMode;
            }
            return dict;
        }
    }

    private Task HandleSelectAsync() =>
        IsDesignMode ? OnSelectWidget.InvokeAsync(Widget.Id) : Task.CompletedTask;

    /// <summary>
    /// 非设计态下，若该 widget 配了入场动画，则用 IntersectionObserver 在它滚动进入视口时
    /// 加 .is-visible 触发 CSS 动画，并按区块内序号错开，让读者能明显看到逐个浮现的效果。
    /// 支持设计态与实时预览切换、以及无刷新切换语言时的状态同步。
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var animKey = HasAnimation ? ParsedStyle.Animation?.Trim().ToLowerInvariant() : null;
        var currentState = (Widget.Id, IsDesignMode, animKey);
        if (_lastAnimState == currentState)
            return;

        _lastAnimState = currentState;

        const string js = """
            export function disarm(el) {
                if (!el) return;
                try {
                    if (el._cmsAnimRaf) { cancelAnimationFrame(el._cmsAnimRaf); el._cmsAnimRaf = 0; }
                    if (el._cmsAnimIo) { el._cmsAnimIo.disconnect(); el._cmsAnimIo = null; }
                    el.classList.remove('cms-anim-armed', 'is-visible');
                    el.style.animationDelay = '';
                } catch {}
            }
            export function reveal(el, delayMs) {
                if (!el) return;
                disarm(el);
                try {
                    const show = () => {
                        if (delayMs > 0) el.style.animationDelay = delayMs + 'ms';
                        el.classList.add('is-visible');
                        if (el._cmsAnimIo) { el._cmsAnimIo.disconnect(); el._cmsAnimIo = null; }
                    };
                    if (!('IntersectionObserver' in window)) { show(); return; }
                    // 先 armed 隐藏，等下一帧布局稳定后再由 IntersectionObserver 监听：
                    // 只有当用户真正浏览/滚动到该模块进入视口时才触发入场动画，
                    // 使用 rootMargin: '0px' 避免页面最底部较矮的页脚/按钮被负边距裁掉而无法触发。
                    el.classList.add('cms-anim-armed');
                    el._cmsAnimRaf = requestAnimationFrame(() => {
                        el._cmsAnimRaf = 0;
                        if (!el.isConnected) return;
                        const io = new IntersectionObserver((entries) => {
                            for (const e of entries) {
                                if (e.isIntersecting) { show(); break; }
                            }
                        }, { threshold: 0.01, rootMargin: '0px' });
                        el._cmsAnimIo = io;
                        io.observe(el);
                    });
                } catch (err) {
                    disarm(el);
                }
            }
            """;

        if (IsDesignMode || !HasAnimation)
        {
            if (_animJs is not null)
            {
                try { await _animJs.InvokeVoidAsync("disarm", _boxRef); }
                catch (JSDisconnectedException) { }
                catch (JSException) { }
                catch (InvalidOperationException) { }
            }
            return;
        }

        try
        {
            _animJs ??= await JS.InvokeAsync<IJSObjectReference>(
                "import", $"data:text/javascript;charset=utf-8,{Uri.EscapeDataString(js)}");
            await _animJs.InvokeVoidAsync("reveal", _boxRef, DelayMs);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>按 widget 在区块中的序号错开动画，形成逐个浮现的节奏。</summary>
    private int DelayMs => Math.Min(Math.Max(Widget.SortOrder, 0) % 8 * 90, 630);

    private Task HandleMoveOrderAsync(int delta) =>
        IsDesignMode ? OnMoveWidgetOrder.InvokeAsync((Widget.Id, delta)) : Task.CompletedTask;

    private Task HandleDuplicateAsync() =>
        IsDesignMode ? OnDuplicateWidget.InvokeAsync(Widget.Id) : Task.CompletedTask;

    private Task HandleDeleteAsync() =>
        IsDesignMode ? OnDeleteWidget.InvokeAsync(Widget.Id) : Task.CompletedTask;

    private Task HandleDragStart() =>
        IsDesignMode ? OnDragStartWidget.InvokeAsync(Widget.Id) : Task.CompletedTask;

    private Task HandleDropOnSelfAsync() =>
        IsDesignMode ? OnDropOnWidget.InvokeAsync((SectionId, Widget.ZoneId, Widget.Id)) : Task.CompletedTask;

    private async Task StartResizeAsync(string axis)
    {
        if (!IsDesignMode) return;
        const string js = """
            export function beginResize(el, axis, dotNetRef) {
                if (!el) return;
                const rect = el.getBoundingClientRect();
                const startW = rect.width;
                const startH = rect.height;
                let startX = null;
                let startY = null;
                let curW = Math.round(startW);
                let curH = Math.round(startH);
                el.style.boxSizing = 'border-box';
                el.style.maxWidth = '100%';

                const onMove = (e) => {
                    if (startX === null) { startX = e.clientX; startY = e.clientY; }
                    if (axis === 'e' || axis === 'se') {
                        curW = Math.max(120, Math.round(startW + (e.clientX - startX)));
                        el.style.width = curW + 'px';
                    }
                    if (axis === 's' || axis === 'se') {
                        curH = Math.max(48, Math.round(startH + (e.clientY - startY)));
                        el.style.height = curH + 'px';
                        el.style.overflow = 'auto';
                    }
                };
                const onUp = () => {
                    window.removeEventListener('pointermove', onMove);
                    window.removeEventListener('pointerup', onUp);
                    const w = (axis === 'e' || axis === 'se') ? (curW + 'px') : null;
                    const h = (axis === 's' || axis === 'se') ? (curH + 'px') : null;
                    dotNetRef.invokeMethodAsync('CompleteResizeAsync', w, h);
                };
                window.addEventListener('pointermove', onMove);
                window.addEventListener('pointerup', onUp);
            }
            """;

        try
        {
            _resizeJs ??= await JS.InvokeAsync<IJSObjectReference>("import", $"data:text/javascript;charset=utf-8,{Uri.EscapeDataString(js)}");
            _selfRef ??= DotNetObjectReference.Create(this);
            await _resizeJs.InvokeVoidAsync("beginResize", _boxRef, axis, _selfRef);
        }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { }
    }

    [JSInvokable]
    public Task CompleteResizeAsync(string? width, string? height) =>
        OnResizeWidget.InvokeAsync((Widget.Id, width, height));

    private async Task HandleDoubleClickAsync(MouseEventArgs e)
    {
        if (!IsDesignMode) return;
        if (!IsSelected)
            await OnSelectWidget.InvokeAsync(Widget.Id);

        const string js = """
            export function beginInlineEdit(boxEl, clientX, clientY, dotNetRef) {
                if (!boxEl) return;
                let target = document.elementFromPoint(clientX, clientY);
                if (!target || !boxEl.contains(target)) return;
                if (target.closest('.cms-widget-design-bar, .cms-carousel-arrow, .cms-carousel-dots, input, textarea, select')) return;

                if (target.tagName === 'IMG') {
                    const src = target.getAttribute('src') || '';
                    dotNetRef.invokeMethodAsync('CompleteInlineImagePickAsync', src);
                    return;
                }

                const slideEl = target.closest('.cms-carousel-slide');
                if (slideEl && !target.closest('h1, h2, h3, h4, p, span, strong, small, button, a')) {
                    const bgUrl = slideEl.getAttribute('data-bg-url') || ('__carousel_slide__:' + (slideEl.getAttribute('data-slide-index') || '0'));
                    dotNetRef.invokeMethodAsync('CompleteInlineImagePickAsync', bgUrl);
                    return;
                }

                if (target.closest('.cms-media-placeholder') && target.closest('.cms-w-carousel')) {
                    dotNetRef.invokeMethodAsync('CompleteInlineImagePickAsync', '__carousel_slide__:0');
                    return;
                }

                const allowed = ['H1','H2','H3','H4','P','SPAN','STRONG','SMALL','FIGCAPTION','LI','BUTTON','A','DIV'];
                while (target && target !== boxEl && !allowed.includes(target.tagName)) {
                    target = target.parentElement;
                }
                if (!target || target === boxEl) return;
                if (target.children.length > 0 && target.tagName !== 'BUTTON') {
                    const textChild = Array.from(target.childNodes).find(n => n.nodeType === 3 && n.textContent.trim().length > 0);
                    if (!textChild) return;
                }

                const oldText = (target.innerText || '').trim();
                if (!oldText) return;

                const prevDraggable = boxEl.getAttribute('draggable');
                boxEl.setAttribute('draggable', 'false');
                target.setAttribute('contenteditable', 'true');
                target.classList.add('cms-inline-editing');
                target.focus();

                const cleanup = (commit) => {
                    target.removeEventListener('blur', onBlur);
                    target.removeEventListener('keydown', onKey);
                    target.removeAttribute('contenteditable');
                    target.classList.remove('cms-inline-editing');
                    if (prevDraggable !== null) boxEl.setAttribute('draggable', prevDraggable);
                    const newText = (target.innerText || '').trim();
                    if (commit && newText && newText !== oldText) {
                        dotNetRef.invokeMethodAsync('CompleteInlineTextEditAsync', oldText, newText);
                    } else if (!commit) {
                        target.innerText = oldText;
                    }
                };
                const onBlur = () => cleanup(true);
                const onKey = (ev) => {
                    if (ev.key === 'Escape') { ev.preventDefault(); cleanup(false); }
                    else if (ev.key === 'Enter' && target.tagName !== 'P') { ev.preventDefault(); target.blur(); }
                };
                target.addEventListener('blur', onBlur);
                target.addEventListener('keydown', onKey);
            }
            """;

        try
        {
            _inlineEditJs ??= await JS.InvokeAsync<IJSObjectReference>("import", $"data:text/javascript;charset=utf-8,{Uri.EscapeDataString(js)}");
            _selfRef ??= DotNetObjectReference.Create(this);
            await _inlineEditJs.InvokeVoidAsync("beginInlineEdit", _boxRef, e.ClientX, e.ClientY, _selfRef);
        }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { }
    }

    [JSInvokable]
    public Task CompleteInlineTextEditAsync(string oldText, string newText) =>
        OnInlineEditText.InvokeAsync((Widget.Id, oldText, newText));

    [JSInvokable]
    public Task CompleteInlineImagePickAsync(string oldImageUrl) =>
        OnInlineEditImage.InvokeAsync((Widget.Id, oldImageUrl));

    public async ValueTask DisposeAsync()
    {
        _selfRef?.Dispose();
        if (_inlineEditJs is not null)
        {
            try { await _inlineEditJs.DisposeAsync(); }
            catch (JSDisconnectedException) { }
            catch (InvalidOperationException) { }
        }
        if (_resizeJs is not null)
        {
            try { await _resizeJs.DisposeAsync(); }
            catch (JSDisconnectedException) { }
            catch (InvalidOperationException) { }
        }
        if (_animJs is not null)
        {
            try
            {
                await _animJs.InvokeVoidAsync("disarm", _boxRef);
                await _animJs.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            catch (InvalidOperationException) { }
        }
    }
}
