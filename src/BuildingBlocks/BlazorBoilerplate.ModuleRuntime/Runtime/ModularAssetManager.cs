using System.Collections.Concurrent;

namespace BlazorBoilerplate.ModuleRuntime.Runtime;

/// <summary>
/// 模块化静态资源虚拟文件管理器（映射包内 assets/ 与 frontend/wwwroot/ 资源并防路径穿越）。
/// </summary>
public sealed class ModularAssetManager
{
    private readonly ConcurrentDictionary<string, string[]> _roots = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterModule(string moduleId, string packagePath)
    {
        if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(packagePath))
            return;

        var dirs = new[]
        {
            Path.Combine(packagePath, "assets"),
            Path.Combine(packagePath, "frontend", "wwwroot")
        }
        .Where(Directory.Exists)
        .Select(d => Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
        .ToArray();

        if (dirs.Length > 0)
            _roots[moduleId] = dirs;
    }

    public void UnregisterModule(string moduleId) =>
        _roots.TryRemove(moduleId, out _);

    public bool TryResolveFile(string moduleId, string relativePath, out string fullPath, out string contentType)
    {
        fullPath = string.Empty;
        contentType = "application/octet-stream";

        if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(relativePath))
            return false;
        if (!_roots.TryGetValue(moduleId, out var roots))
            return false;

        var cleanRel = relativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        foreach (var root in roots)
        {
            var candidate = Path.GetFullPath(Path.Combine(root, cleanRel));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.Exists(candidate))
            {
                fullPath = candidate;
                contentType = GetMimeType(candidate);
                return true;
            }
        }
        return false;
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".css" => "text/css; charset=utf-8",
        ".js" => "application/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        _ => "application/octet-stream"
    };
}

