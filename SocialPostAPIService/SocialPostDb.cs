using ScanPay.Utility.Model;
using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;

namespace ScanPay.SocialPostService
{
    [Alias("SocialPost")]
    public class SocialPostDb
    {
        public string AppName { get; set; } = "";
        public string Runway { get; set; } = "";
        public string AccountScopeKey { get; set; } = "";
        // Zero identifies legacy deliveries; each restored draft starts a new attempt.
        public int PublishingAttempt { get; set; }
        public TikTokPostOptions? TikTok { get; set; }
        public TextSocialPostOptions? TextSocial { get; set; }
        public List<MetaSocialPostOptions> MetaSocial { get; set; } = new();

        [PrimaryKey]
        public string SocialPostID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string OrganizationID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string ResourceType { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string ResourceID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [Index]
        public string OutreachItemID { get; set; } =
            DefaultValue.EMPTY_STRING;

        public string Content { get; set; } =
            DefaultValue.EMPTY_STRING;

        public string DestinationUrl { get; set; } =
            DefaultValue.EMPTY_STRING;

        public List<string> Platforms { get; set; } =
            new();

        [Index]
        public string Status { get; set; } =
            SocialPostStatus.Draft;

        public DateTime? ScheduledDateUtc { get; set; }

        public DateTime? PublishedDateUtc { get; set; }

        public DateTime CreateDate { get; set; } =
            DefaultValue.UtcNow();

        public DateTime LastUpdate { get; set; } =
            DefaultValue.UtcNow();
    }
}
