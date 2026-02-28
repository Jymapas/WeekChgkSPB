using System;
using System.Collections.Generic;
using System.Text;

namespace WeekChgkSPB;

public static class LinkNormalizer
{
    private const string LiveJournalBaseUrl = "https://chgk-spb.livejournal.com";
    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid",
        "gclid",
        "yclid",
        "mc_cid",
        "mc_eid",
        "igshid"
    };

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var trimmed = input.Trim();
        if (long.TryParse(trimmed, out var id) && id > 0)
        {
            trimmed = $"{LiveJournalBaseUrl}/{id}.html";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Fragment = string.Empty
        };

        if (string.Equals(builder.Host, "chgk-spb.livejournal.com", StringComparison.OrdinalIgnoreCase))
        {
            builder.Scheme = "https";
        }

        if ((builder.Scheme == "http" && builder.Port == 80) ||
            (builder.Scheme == "https" && (builder.Port == 443 || builder.Port == 80)))
        {
            builder.Port = -1;
        }

        var path = builder.Path;
        if (!string.IsNullOrEmpty(path) && path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
        {
            builder.Path = path.TrimEnd('/');
        }

        builder.Query = NormalizeQuery(builder.Query);
        return builder.Uri.ToString();
    }

    private static string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var items = new List<(string Key, string? Value)>();
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var eq = pair.IndexOf('=');
            var rawKey = eq >= 0 ? pair[..eq] : pair;
            var rawValue = eq >= 0 ? pair[(eq + 1)..] : null;
            var key = Uri.UnescapeDataString(rawKey);
            if (IsTrackingParam(key))
            {
                continue;
            }

            var value = rawValue is null ? null : Uri.UnescapeDataString(rawValue);
            items.Add((key, value));
        }

        if (items.Count == 0)
        {
            return string.Empty;
        }

        items.Sort((a, b) =>
        {
            var keyCompare = string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            if (keyCompare != 0)
            {
                return keyCompare;
            }

            return string.Compare(a.Value, b.Value, StringComparison.Ordinal);
        });

        var sb = new StringBuilder();
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('&');
            }

            sb.Append(Uri.EscapeDataString(items[i].Key));
            var value = items[i].Value;
            if (value is not null)
            {
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(value));
            }
        }

        return sb.ToString();
    }

    private static bool IsTrackingParam(string key)
    {
        if (key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TrackingParams.Contains(key);
    }
}
