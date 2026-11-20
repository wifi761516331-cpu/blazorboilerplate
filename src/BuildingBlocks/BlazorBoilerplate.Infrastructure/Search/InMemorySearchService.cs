using System.Collections.Concurrent;
using BlazorBoilerplate.Application.Search;

namespace BlazorBoilerplate.Infrastructure.Search;

/// <summary>基于内存索引的轻量全局全文检索服务（支持多索引分组与相关性评分）。</summary>
public sealed class InMemorySearchService : ISearchService
{
    private readonly ConcurrentDictionary<string, SearchDocument> _docs = new(StringComparer.OrdinalIgnoreCase);

    public Task IndexAsync(SearchDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        _docs[$"{document.IndexName}:{document.Id}"] = document;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string indexName, string id, CancellationToken cancellationToken = default)
    {
        _docs.TryRemove($"{indexName}:{id}", out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        var kw = (query.Keyword ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(kw))
            return Task.FromResult<IReadOnlyList<SearchHit>>([]);

        var tokens = kw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hits = new List<SearchHit>();

        foreach (var doc in _docs.Values)
        {
            if (!string.IsNullOrWhiteSpace(query.IndexName) &&
                !string.Equals(doc.IndexName, query.IndexName, StringComparison.OrdinalIgnoreCase))
                continue;

            double score = 0;
            foreach (var token in tokens)
            {
                if (doc.Title.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += doc.Title.Equals(token, StringComparison.OrdinalIgnoreCase) ? 10 : 5;
                if (doc.Content.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 2;
                if (doc.Fields is not null && doc.Fields.Values.Any(v => v.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    score += 1;
            }

            if (score <= 0)
                continue;

            var snippet = BuildSnippet(doc.Content, tokens[0]);
            hits.Add(new SearchHit(doc.Id, doc.IndexName, doc.Title, snippet, doc.Url, score));
        }

        IReadOnlyList<SearchHit> ordered = hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(query.Top, 1, 100))
            .ToArray();

        return Task.FromResult(ordered);
    }

    private static string BuildSnippet(string content, string token)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;
        var idx = content.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return content.Length <= 120 ? content : content[..120] + "…";

        var start = Math.Max(0, idx - 30);
        var len = Math.Min(120, content.Length - start);
        var slice = content.Substring(start, len);
        return (start > 0 ? "…" : "") + slice + (start + len < content.Length ? "…" : "");
    }
}

