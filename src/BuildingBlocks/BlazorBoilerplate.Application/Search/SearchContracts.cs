namespace BlazorBoilerplate.Application.Search;

/// <summary>可索引的全文检索文档。</summary>
public sealed record SearchDocument(
    string Id,
    string IndexName,
    string Title,
    string Content,
    string? Url = null,
    IReadOnlyDictionary<string, string>? Fields = null);

/// <summary>全文检索查询条件。</summary>
public sealed record SearchQuery(
    string Keyword,
    string? IndexName = null,
    int Top = 20);

/// <summary>全文检索命中结果。</summary>
public sealed record SearchHit(
    string Id,
    string IndexName,
    string Title,
    string Snippet,
    string? Url,
    double Score);

/// <summary>全局统一搜索服务契约。</summary>
public interface ISearchService
{
    /// <summary>写入或更新索引文档。</summary>
    Task IndexAsync(SearchDocument document, CancellationToken cancellationToken = default);

    /// <summary>从指定索引移除文档。</summary>
    Task RemoveAsync(string indexName, string id, CancellationToken cancellationToken = default);

    /// <summary>执行关键词检索。</summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
}

