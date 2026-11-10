using ServiceStack.DataAnnotations;
using System;

namespace ScanPay.SocialPostService
{
    [Alias("SocialPostClick")]
    public class SocialPostClickDb
    {
        public string DeliveryID { get; set; } = "";
        public string AppName { get; set; } = "";
        public string Runway { get; set; } = "";
        public string AccountScopeKey { get; set; } = "";
        [PrimaryKey]
        public string ClickID { get; set; } = "";
        public string SocialPostID { get; set; } = "";
        public string OrganizationID { get; set; } = "";
        public string CampaignID { get; set; } = "";
        public string VisitorID { get; set; } = "";
        public string Platform { get; set; } = "";
        public DateTime CreateDate { get; set; } = DateTime.UtcNow;
    }
}
