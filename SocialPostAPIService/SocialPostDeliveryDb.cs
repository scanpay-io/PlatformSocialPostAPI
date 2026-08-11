using ScanPay.Utility.Model;
using ServiceStack.DataAnnotations;
using System;

namespace ScanPay.SocialPostService
{
    [Alias("SocialPostDelivery")]
    public class SocialPostDeliveryDb
    {
        [PrimaryKey]
        public string DeliveryID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string SocialPostID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string OrganizationID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string SocialConnectionID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string Platform { get; set; } =
            DefaultValue.EMPTY_STRING;

        public string ExternalPostID { get; set; } =
            DefaultValue.EMPTY_STRING;

        public string ExternalPostUrl { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string Status { get; set; } =
            SocialPostDeliveryStatus.Queued;

        public string ErrorCode { get; set; } =
            DefaultValue.EMPTY_STRING;

        public string ErrorMessage { get; set; } =
            DefaultValue.EMPTY_STRING;

        public DateTime? PublishedDateUtc { get; set; }

        public DateTime CreateDate { get; set; } =
            DefaultValue.UtcNow();

        public DateTime LastUpdate { get; set; } =
            DefaultValue.UtcNow();
    }
}
