using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RemoteInspector.Internal
{
    internal static class RemoteInspectorWebAssets
    {
        private sealed class AssetManifestEntry
        {
            public string ResourcePath;
            public string ContentType;
        }

        private static readonly Dictionary<string, AssetManifestEntry> Manifest = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["/"] = new AssetManifestEntry { ResourcePath = "RemoteInspectorWeb/index", ContentType = "text/html; charset=utf-8" },
            ["/index.html"] = new AssetManifestEntry { ResourcePath = "RemoteInspectorWeb/index", ContentType = "text/html; charset=utf-8" },
            ["/styles.css"] = new AssetManifestEntry { ResourcePath = "RemoteInspectorWeb/styles", ContentType = "text/css; charset=utf-8" },
            ["/app.js"] = new AssetManifestEntry { ResourcePath = "RemoteInspectorWeb/app", ContentType = "application/javascript; charset=utf-8" }
        };

        public static bool TryGetAsset(string rawPath, out string contentType, out byte[] data)
        {
            var path = NormalizePath(rawPath);
            if (!Manifest.TryGetValue(path, out var entry))
            {
                contentType = "text/plain; charset=utf-8";
                data = null;
                return false;
            }

            var asset = Resources.Load<TextAsset>(entry.ResourcePath);
            if (asset == null)
            {
                contentType = "text/html; charset=utf-8";
                data = Encoding.UTF8.GetBytes("<h1>Missing embedded web asset</h1>");
                return true;
            }

            contentType = entry.ContentType;
            data = asset.bytes;
            return true;
        }

        private static string NormalizePath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return "/";
            }

            var queryStart = rawPath.IndexOf('?');
            var path = queryStart >= 0 ? rawPath.Substring(0, queryStart) : rawPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/";
            }

            return path.StartsWith("/") ? path : "/" + path;
        }
    }
}
