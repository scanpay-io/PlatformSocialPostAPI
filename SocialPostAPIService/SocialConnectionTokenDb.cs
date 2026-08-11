using ScanPay.Utility.Model;
using ServiceStack.DataAnnotations;
using System;

namespace ScanPay.SocialPostService
{
    [Alias("SocialConnectionToken")]
    public class SocialConnectionTokenDb
    {
        [PrimaryKey]
        public string TokenSecretID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string SocialConnectionID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string OrganizationID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string Platform { get; set; } =
            DefaultValue.EMPTY_STRING;

        public string AccessToken { get; set; } =
            DefaultValue.EMPTY_STRING;

        public string RefreshToken { get; set; } =
            DefaultValue.EMPTY_STRING;

        public DateTime? TokenExpiresDateUtc { get; set; }

        public DateTime CreateDate { get; set; } =
            DefaultValue.UtcNow();

        public DateTime LastUpdate { get; set; } =
            DefaultValue.UtcNow();
    }
}
