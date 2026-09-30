using System;
using System.Collections.Generic;

namespace ScanPay.SocialPostService
{
    public static class SocialPlatform
    {
        public const string TikTok = "tiktok";
        public const string X = "x";

        public static bool UsesSignedState(string platform) =>
            string.Equals(platform, TikTok, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(platform, LinkedIn, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(platform, X, StringComparison.OrdinalIgnoreCase);
        public const string Facebook =
            "facebook";

        public const string Instagram =
            "instagram";

        public const string Threads =
            "threads";

        public const string LinkedIn =
            "linkedin";

        private static readonly HashSet<string> ValidPlatforms =
            new(StringComparer.OrdinalIgnoreCase)
            {
                Facebook,
                Instagram,
                Threads,
                LinkedIn,
                TikTok,
                X
            };

        public static bool IsValid(
            string platform)
        {
            return !string.IsNullOrWhiteSpace(platform) &&
                   ValidPlatforms.Contains(platform);
        }
    }
}
