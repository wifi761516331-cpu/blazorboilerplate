namespace BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;

/// <summary>
/// 内置布局骨架名称。种子服务按这些名称补种布局，内置模板以同名 LayoutPreset 精确命中。
/// 两边必须共享同一份常量，避免改名后模板静默落回单栏。
/// </summary>
public static class CmsBuiltInLayoutNames
{
    public const string NestedExample = "嵌套布局示例 (侧栏+内嵌两栏)";

    public const string MosaicCollage = "异型拼贴布局 (7/5 + 通栏)";
    public const string MosaicNested = "异型嵌套布局 (封面 + 内嵌三列)";
    public const string MosaicStairs = "异型阶梯布局 (4/8 转 8/4)";

    public const string SignalStage = "异型中心舞台布局 (3/6/3)";
    public const string SignalNested = "异型嵌套布局 (舞台 + 内嵌双轨)";

    public const string AtlasSpread = "异型跨页布局 (5/7 转 7/5)";
    public const string AtlasNested = "异型嵌套布局 (地图 + 内嵌四列)";

    /// <summary>全部内置布局名，供种子服务一次性批量判重。</summary>
    public static readonly string[] All =
    [
        NestedExample,
        MosaicCollage, MosaicNested, MosaicStairs,
        SignalStage, SignalNested,
        AtlasSpread, AtlasNested
    ];
}
