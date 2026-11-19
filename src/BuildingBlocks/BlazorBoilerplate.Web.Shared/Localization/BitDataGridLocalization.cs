using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;

namespace BlazorBoilerplate.Web.Shared.Localization;

/// <summary>
/// BitDataGrid 的内置分页/工具栏文案硬编码英文（Bit fork QuickGrid 时未接本地化）。
/// 通过 <see cref="BitDataGrid.Strings"/> 参数注入按当前文化翻译的 <see cref="BitDataGridStrings"/>。
/// 词条存于平台内置词典（platform.grid.*），由多语言管理模块统一维护。
/// </summary>
public static class BitDataGridLocalizationExtensions
{
    public static BitDataGridStrings CreateGridStrings(this ILocalizationService localization) => new()
    {
        PagerRangeFormat = localization.GetString("platform.grid.pagerRange"),
        PagerPageFormat = localization.GetString("platform.grid.pagerPage"),
        PerPageFormat = localization.GetString("platform.grid.perPage"),
        RowsPerPageLabel = localization.GetString("platform.grid.rowsPerPage"),
        EmptyText = localization.GetString("platform.grid.empty"),
        LoadingText = localization.GetString("platform.grid.loading"),
        NoMatchesText = localization.GetString("platform.grid.noMatches"),
    };
}
