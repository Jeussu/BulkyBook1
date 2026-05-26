using System.Text.RegularExpressions;

namespace BulkyBook.Utility
{
    public static class SafeHtml
    {
        private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "p",
            "br",
            "strong",
            "b",
            "em",
            "i",
            "ul",
            "ol",
            "li"
        };

        public static string Sanitize(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var sanitized = Regex.Replace(
                html,
                @"<\s*(script|style|iframe|object|embed|svg|math|meta|link|base|form|input|button)[^>]*>.*?<\s*/\s*\1\s*>",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromSeconds(1));

            sanitized = Regex.Replace(
                sanitized,
                @"<\s*(script|style|iframe|object|embed|svg|math|meta|link|base|form|input|button)[^>]*/?\s*>",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromSeconds(1));

            sanitized = Regex.Replace(
                sanitized,
                @"<!--.*?-->",
                string.Empty,
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(1));

            return Regex.Replace(
                sanitized,
                @"<\s*(/?)\s*([a-zA-Z0-9]+)(?:\s+[^>]*)?>",
                match =>
                {
                    var closingSlash = match.Groups[1].Value;
                    var tagName = match.Groups[2].Value.ToLowerInvariant();

                    if (!AllowedTags.Contains(tagName))
                    {
                        return string.Empty;
                    }

                    if (tagName == "br")
                    {
                        return "<br>";
                    }

                    return closingSlash == "/" ? $"</{tagName}>" : $"<{tagName}>";
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromSeconds(1));
        }
    }
}
