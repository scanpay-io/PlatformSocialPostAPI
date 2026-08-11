namespace ScanPay.SocialPostService
{
    using System;
    using System.Collections.Generic;

    public static class SocialPostStatus
    {
        public const string Draft =
            "draft";

        public const string Scheduled =
            "scheduled";

        public const string Queued =
            "queued";

        public const string Publishing =
            "publishing";

        public const string Published =
            "published";

        public const string PartiallyPublished =
            "partially_published";

        public const string Failed =
            "failed";

        public const string Cancelled =
            "cancelled";

        private static readonly HashSet<string> ValidStatuses =
            new(StringComparer.OrdinalIgnoreCase)
            {
                Draft,
                Scheduled,
                Queued,
                Publishing,
                Published,
                PartiallyPublished,
                Failed,
                Cancelled
            };

        public static bool IsValid(
            string status)
        {
            return !string.IsNullOrWhiteSpace(status) &&
                   ValidStatuses.Contains(
                       status);
        }
    }
}
