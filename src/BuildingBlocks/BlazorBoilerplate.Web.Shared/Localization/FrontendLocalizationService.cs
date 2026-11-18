using System.Collections.Concurrent;
using System.Globalization;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Web.Shared.Services;

namespace BlazorBoilerplate.Web.Shared.Localization;

/// <summary>
/// 前端多语言服务（Scoped，随 Blazor 线路存活）：
/// 从 ApiHost 拉取平台与所有已加载模块的合并词典（后端已按 目标文化 -> 父文化 -> en-US 合并），
/// 并在本地按文化缓存，支持即时切换语言与通知组件重渲染。
/// </summary>
public sealed class FrontendLocalizationService : ILocalizationService, IDisposable
{
    private readonly BlazorBoilerplateApiClient _api;

    /// <summary>实例转发处理器，必须持有引用才能精确退订，避免 static 事件持有线路引用导致泄漏。</summary>
    private readonly Action _forwardGlobalChanged;

    public FrontendLocalizationService(BlazorBoilerplateApiClient api)
    {
        _api = api;
        _forwardGlobalChanged = () => Changed?.Invoke();
        GlobalChanged += _forwardGlobalChanged;
    }

    public void Dispose() => GlobalChanged -= _forwardGlobalChanged;

    /// <summary>
    /// 进程级词典版本：管理模块改词后由 InvalidateAsync 递增，布局 @key 绑定它强制重建页面子树。
    /// 必须 static：Scoped 实例每条线路一份，只有进程级版本才能让所有在线线路感知同一变更。
    /// </summary>
    private static int _version;

    /// <summary>进程级变更广播：每个 Scoped 实例订阅后转发到自己的 Changed，驱动本线路重渲染。</summary>
    private static event Action? GlobalChanged;

    /// <summary>供布局 @key 读取的当前词典版本。</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>
    /// 按文化的词典缓存。必须是进程级共享：SSR 预渲染与交互线路分属不同 DI 作用域，
    /// 若缓存放在 scoped 实例里，首屏渲染时新作用域的词典为空，同步索引器 L[key]
    /// 只能输出原始 key（预渲染阶段 await 后的 Changed 无法改写已生成的 HTML）。
    /// 由 WebHost 中间件在渲染前预热，各作用域（含每条线路）直接命中同一份词典。
    /// 词典内容按文化全局一致，不含租户/用户私有数据，共享是安全的。
    /// </summary>
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    // 按 culture 缓存「正在加载」的任务：多个组件（Routes/Layout/页面）在同一线路里
    // 并发调用 EnsureLoadedAsync 时共享同一次 API 请求，避免 N 次重复调用与各自 Complete
    // 造成的渲染时序错乱；加载结束后移除条目，失败后可重试。
    private readonly ConcurrentDictionary<string, Task> _inFlight =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 进程级：每个文化同时最多一个补刷任务在途。与永久去重锁不同，这里在补刷循环结束（成功或耗尽重试）后
    /// 移除条目，允许后续 EnsureLoadedAsync 再次调度——用于覆盖「ApiHost 从 Bootstrap 恢复 Normal」等
    /// 模块延迟注册的窗口可能晚于首次补刷的场景。多线路并发命中预热缓存仍共享同一补刷任务。
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> CatchUpInFlight =
        new(StringComparer.OrdinalIgnoreCase);

    private string _currentCulture = NormalizeInitialCulture();

    public event Action? Changed;

    public int Revision { get; private set; }
    public string CurrentCulture => _currentCulture;
    public string FallbackCulture => LocalizationDefaults.DefaultFallbackCulture;

    public string this[string key] => GetString(key);

    /// <summary>
    /// 由 WebHost 请求中间件在渲染前预热进程级词典。预渲染阶段用同步索引器 L[key] 渲染，
    /// 必须保证此刻词典已就绪，否则整页输出原始 key。返回预热后该文化的词条数。
    /// </summary>
    public static int Prewarm(string culture, IReadOnlyDictionary<string, string> dict)
    {
        if (string.IsNullOrWhiteSpace(culture) || dict is not { Count: > 0 })
            return 0;

        var target = culture.Trim();
        // 与 LoadAsync 同构：只接受词条更多或相等的快照。否则新开标签页的文档请求
        // 若预热到一份较旧的词典，会把线路已加载的更全词典倒退覆盖，页面大面积变回 key。
        var currentCount = _cache.TryGetValue(target, out var cur) ? cur.Count : 0;
        if (dict.Count < currentCount)
            return currentCount;

        _cache[target] = new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        return dict.Count;
    }

    /// <summary>进程级缓存是否已含该文化（中间件据此决定是否跳过重复预热）。</summary>
    public static bool HasCulture(string culture) =>
        !string.IsNullOrWhiteSpace(culture) && _cache.ContainsKey(culture.Trim());

    /// <summary>
    /// 词条变更后的失效入口（管理模块保存成功后调用）。
    /// 关键：不能只清缓存就广播——布局 @key 绑定版本，广播后所有线路立即重建子树并重新求值 L[key]，
    /// 若此刻缓存为空，整页会变成原始 key。因此这里先从 ApiHost 拉回最新词典覆盖缓存，再递增版本广播；
    /// 拉取失败则保留旧缓存（显示旧词好过整页 key）。culture 为空表示当前线路文化。
    /// 前提：ApiHost 侧管理模块须先失效其 store 快照，本端才能拉到新词。
    /// </summary>
    public async Task InvalidateAsync(string? culture = null, CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(culture) ? _currentCulture : culture.Trim();

        try
        {
            var dict = await _api.GetLocalizationResourcesAsync(target, cancellationToken);
            if (dict is { Count: > 0 })
                _cache[target] = new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // ApiHost 暂不可达：保留旧缓存，仅广播触发重建（内容不变），避免整页变原始 key。
        }

        Interlocked.Increment(ref _version);
        GlobalChanged?.Invoke();
    }

    public async Task EnsureLoadedAsync(string? culture = null, CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(culture) ? _currentCulture : culture.Trim();
        _currentCulture = target;

        if (_cache.TryGetValue(target, out var cached))
        {
            // 命中预热缓存时 LoadAsync 不会执行，catch-up 也就无从触发。
            // 首次安装时中间件预热拿到的可能只有平台资源，模块延迟注册的词条会永久缺失。
            // 故命中缓存也调度补刷（进程级在途去重，见 CatchUpInFlight）。
            ScheduleCatchUpRefresh(target, cached.Count);
            return;
        }

        // 并发去重：同一 culture 的加载只发一次请求；后来者 await 同一个 Task，
        // 保证所有组件的 await 在同一时刻返回，避免各自 Complete 导致的首帧时序错乱。
        var load = _inFlight.GetOrAdd(target, _ => LoadAsync(target, cancellationToken));
        try
        {
            await load;
        }
        finally
        {
            // 仅移除自己触发的那次；失败后再次调用可重新加载。
            _inFlight.TryRemove(new KeyValuePair<string, Task>(target, load));
        }
    }

    private async Task LoadAsync(string target, CancellationToken cancellationToken)
    {
        try
        {
            var dict = await _api.GetLocalizationResourcesAsync(target, cancellationToken);
            if (dict is { Count: > 0 })
            {
                var currentCount = _cache.TryGetValue(target, out var cur) ? cur.Count : 0;
                if (dict.Count >= currentCount)
                    _cache[target] = new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
            }
            Revision++;
            Changed?.Invoke();

            // 首次安装 / 模块延迟加载时，ApiHost 此时可能还没把模块 localization/*.json
            // 合并进 registry，首屏只能拿到平台资源（如 identity.signin.* 全是原始 key）。
            // 延迟补刷一次，资源变多就更新缓存并通知组件重渲染，用户无需手动 F5。
            ScheduleCatchUpRefresh(target, dict?.Count ?? 0);
        }
        catch
        {
            // ApiHost 暂不可达时不阻断页面渲染（回退 Key 或已有缓存）；不写缓存，允许重试。
        }
    }

    /// <summary>
    /// 有限次退避补刷：模块词条注册可能晚于首屏预热很多（首次安装含迁移/种子/模块加载可达十余秒）。
    /// 旧实现固定延迟 3 秒且永久去重，一旦扑空就再也不补，页面永久停在原始 key。
    /// 现在按递增间隔重试若干次，直到拉到的词典比当前缓存更全；每次重试都重新读取 currentCount，
    /// 保证并发线路里较旧的快照不会覆盖较新的。成功或耗尽重试后移除在途标记，允许后续再次调度。
    /// </summary>
    private void ScheduleCatchUpRefresh(string target, int initialCount)
    {
        // 进程级在途去重：同一文化同时只跑一个补刷循环；结束后移除，不永久封锁。
        if (!CatchUpInFlight.TryAdd(target, 0))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                // 递增间隔：给 ApiHost 完成建库/种子/模块装载留出窗口，总窗口约 12 秒。
                int[] delaysMs = [1500, 2000, 2500, 3000, 3000];
                foreach (var delayMs in delaysMs)
                {
                    await Task.Delay(delayMs);
                    var latest = await _api.GetLocalizationResourcesAsync(target);
                    var currentCount = _cache.TryGetValue(target, out var cur) ? cur.Count : 0;
                    if (latest.Count > currentCount && latest.Count >= initialCount)
                    {
                        _cache[target] = new Dictionary<string, string>(latest, StringComparer.OrdinalIgnoreCase);
                        // 缓存是进程级共享，必须递增版本 + 全局广播，否则其他线路仍停在旧词。
                        Interlocked.Increment(ref _version);
                        GlobalChanged?.Invoke();
                        return; // 词条已到齐，停止重试。
                    }
                }
                // 重试耗尽仍未增长：保持现状，不阻断渲染；下次 EnsureLoadedAsync 可再调度。
            }
            catch
            {
                // 单次补刷失败不影响已渲染内容；在途标记在 finally 移除，后续可重试。
            }
            finally
            {
                CatchUpInFlight.TryRemove(target, out _);
            }
        });
    }

    public async Task SetCultureAsync(string culture, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(culture))
            return;

        var normalized = culture.Trim();
        _currentCulture = normalized;

        try
        {
            var info = CultureInfo.GetCultureInfo(normalized);
            CultureInfo.CurrentCulture = info;
            CultureInfo.CurrentUICulture = info;
        }
        catch (CultureNotFoundException)
        {
            // 保留字符串文化名，不因未知区域码中断
        }

        await EnsureLoadedAsync(normalized, cancellationToken);
        Revision++;
        Changed?.Invoke();
    }

    public string GetString(string key, params object[] args) =>
        GetStringForCulture(key, _currentCulture, args);

    public string GetStringForCulture(string key, string culture, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var target = string.IsNullOrWhiteSpace(culture) ? _currentCulture : culture.Trim();
        string? raw = null;

        if (_cache.TryGetValue(target, out var exact) && exact.TryGetValue(key, out var exactVal))
            raw = exactVal;
        else
        {
            // 与后端 PlatformLocalizationService 同构的父文化回退（如 zh-TW -> zh）。
            var dash = target.IndexOf('-');
            if (dash > 0 && _cache.TryGetValue(target[..dash], out var parent) && parent.TryGetValue(key, out var parentVal))
                raw = parentVal;
            else if (_cache.TryGetValue(FallbackCulture, out var fallback) && fallback.TryGetValue(key, out var fallbackVal))
                raw = fallbackVal;
        }

        return FormatValue(raw ?? key, args);
    }

    public async Task<string> GetStringAsync(string key, CancellationToken cancellationToken = default, params object[] args)
    {
        await EnsureLoadedAsync(_currentCulture, cancellationToken);
        return GetString(key, args);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllStringsAsync(
        string? culture = null,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(culture) ? _currentCulture : culture.Trim();
        await EnsureLoadedAsync(target, cancellationToken);
        return _cache.TryGetValue(target, out var dict)
            ? dict
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<LanguageInfo>> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var list = await _api.GetLanguagesAsync(cancellationToken);
            if (list.Count > 0)
                return list;
        }
        catch
        {
            // 回退内置语言清单
        }

        return LocalizationDefaults.BuiltInLanguages;
    }

    private static string NormalizeInitialCulture()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        return string.IsNullOrWhiteSpace(name) ? LocalizationDefaults.DefaultChineseCulture : name;
    }

    private static string FormatValue(string template, object[] args)
    {
        if (args.Length == 0)
            return template;

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}

