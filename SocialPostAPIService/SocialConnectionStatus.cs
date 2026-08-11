namespace ScanPay.SocialPostService
{
    public static class SocialConnectionStatus
    {
        public const string Connected =
            "connected";

        public const string Expiring =
            "expiring";

        public const string Expired =
            "expired";

        public const string Revoked =
            "revoked";

        public const string ReauthorizationRequired =
            "reauthorization_required";

        public const string Disconnected =
            "disconnected";
    }
}
