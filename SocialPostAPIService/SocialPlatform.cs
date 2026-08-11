using System;
using System.Collections.Generic;

namespace ScanPay.SocialPostService
{
    public static class SocialPlatform
    {
        public const string Facebook =
            "facebook";

        public const string Instagram =
            "instagram";

        public const string LinkedIn =
            "linkedin";

        private static readonly HashSet<string> ValidPlatforms =
            new(StringComparer.OrdinalIgnoreCase)
            {
                Facebook,
                Instagram,
                LinkedIn
            };

        public static bool IsValid(
            string platform)
        {
            return !string.IsNullOrWhiteSpace(platform) &&
                   ValidPlatforms.Contains(platform);
        }
    }
}
