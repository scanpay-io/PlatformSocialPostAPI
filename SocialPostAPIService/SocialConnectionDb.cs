using ScanPay.Utility.Model;
using ServiceStack.DataAnnotations;
using System;

namespace ScanPay.SocialPostService
{
    [Alias("SocialConnection")]
    public class SocialConnectionDb
    {
        [PrimaryKey]
        public string SocialConnectionID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string OrganizationID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string Platform { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string ExternalAccountID { get; set; } =
            DefaultValue.EMPTY_STRING;

        public string DisplayName { get; set; } =
            DefaultValue.EMPTY_STRING;

        public string TokenSecretID { get; set; } =
            DefaultValue.EMPTY_STRING;

        public DateTime? TokenExpiresDateUtc { get; set; }

        public string GrantedScopes { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string Status { get; set; } =
            SocialConnectionStatus.Connected;

        public DateTime CreateDate { get; set; } =
            DefaultValue.UtcNow();

        public DateTime LastUpdate { get; set; } =
            DefaultValue.UtcNow();
    }
}
