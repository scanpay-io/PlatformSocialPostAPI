using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using ScanPay.DataModel.Model;
using ScanPay.Utility.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public class SocialClickRequest : LambdaRequest
    {
        [JsonProperty("social_post_id")]
        public string SocialPostID { get; set; } = "";
        [JsonProperty("click_id")]
        public string ClickID { get; set; } = "";
        [JsonProperty("visitor_id")]
        public string VisitorID { get; set; } = "";
        [JsonProperty("social_delivery_id")]
        public string DeliveryID { get; set; } = "";
        [JsonProperty("social_platform")]
        public string SocialPlatformName { get; set; } = "";
    }

    public class SocialEngagementMetrics
    {
        [JsonProperty("clicks")]
        public int Clicks { get; set; }
        [JsonProperty("unique_visitors")]
        public int UniqueVisitors { get; set; }
        [JsonProperty("donations")]
        public int Donations { get; set; }
        [JsonProperty("donors")]
        public int Donors { get; set; }
        [JsonProperty("total_amount_raised")]
        public decimal? TotalAmountRaised { get; set; }
        [JsonProperty("amount_raised_by_currency")]
        public Dictionary<string, decimal> AmountRaisedByCurrency { get; set; } = new();
        [JsonProperty("conversion_rate")]
        public decimal ConversionRate { get; set; }
        [JsonProperty("clicks_by_platform")]
        public Dictionary<string, int> ClicksByPlatform { get; set; } = new();
    }

    public class SocialEngagementService
    {
        public async Task RecordClickAsync(SocialClickRequest request, ILambdaContext context)
        {
            if (!Guid.TryParseExact(request.SocialPostID, "D", out var postID) ||
                !Guid.TryParseExact(request.ClickID, "D", out var clickID) ||
                !Guid.TryParseExact(request.VisitorID, "D", out var visitorID))
                throw ResponseStatusFactory.BadRequest("social_post_id, click_id and visitor_id must be GUIDs.");
            var post = await DbCRUD<SocialPostDb>.ReadAsync(postID.ToString(), context);
            if (post == null) throw ResponseStatusFactory.NotFound("Social post was not found.");
            SocialTracking.Ensure(post, context);
            using var client = DbCRUD<SocialPostDb>.DBClient(context);
            var deliveries = await new SocialInsightsStore(client)
                .QueryAsync<SocialPostDeliveryDb>(SocialInsightsStore.Query("SocialPostDelivery", "SocialPostID", post.SocialPostID), 2000);
            var selected = SelectDelivery(post, deliveries, request.DeliveryID, request.SocialPlatformName);
            var platform = selected?.Platform ?? "unknown";
            // Conditional insert makes retries with the same click ID idempotent.
            await DbCRUD<SocialPostClickDb>.TryCreateAsync(new SocialPostClickDb
            {
                ClickID = postID + ":" + clickID,
                DeliveryID = selected?.DeliveryID ?? "",
                AppName = post.AppName, Runway = post.Runway, AccountScopeKey = post.AccountScopeKey,
                SocialPostID = postID.ToString(),
                OrganizationID = post.OrganizationID,
                CampaignID = post.ResourceID,
                VisitorID = visitorID.ToString(),
                Platform = platform,
                CreateDate = DateTime.UtcNow
            }, context);
        }

        public static SocialPostDeliveryDb? SelectDelivery(SocialPostDb post, IEnumerable<SocialPostDeliveryDb> deliveries, string deliveryID, string platform)
        {
            var eligible = deliveries.Where(d => SocialTracking.Owns(d, post) && d.PublishedDateUtc.HasValue &&
                d.PublishedDateUtc <= DateTime.UtcNow).ToList();
            if (!string.IsNullOrEmpty(deliveryID))
            {
                var selected = eligible.SingleOrDefault(d => d.DeliveryID == deliveryID && !string.IsNullOrEmpty(d.TrackingUrl));
                if (selected == null || !string.IsNullOrWhiteSpace(platform) && !string.Equals(selected.Platform, platform, StringComparison.OrdinalIgnoreCase))
                    throw ResponseStatusFactory.NotFound("Published social delivery was not found.");
                return selected;
            }
            if (eligible.Count == 0) throw ResponseStatusFactory.NotFound("Social post has no published delivery.");
            // Old clients omit delivery IDs. Preserve post counts, never trust a client
            // platform hint to manufacture delivery attribution on a multi-platform post.
            return eligible.Count == 1 && !string.IsNullOrEmpty(eligible[0].TrackingUrl) ? eligible[0] : null;
        }

        public async Task<SocialEngagementMetrics> ReadAsync(SocialPostDb post, ILambdaContext context)
        {
            using var client = DbCRUD<SocialPostDb>.DBClient(context);
            var records = await LoadAsync(post, new SocialInsightsStore(client), context);
            return Aggregate(post.OrganizationID, post.SocialPostID, records.Clicks, records.Transactions);
        }

        public async Task<object> ReadCampaignAsync(string organizationID, string campaignID, ILambdaContext context)
        {
            var posts = await new SocialPostService().GetByOrganizationAsync(organizationID, context);
            if (!string.IsNullOrWhiteSpace(campaignID))
                posts = posts.Where(p => p.ResourceID == campaignID).ToList();
            var clicks = new List<SocialPostClickDb>();
            var transactions = new List<TransactionAmountDb>();
            var byPost = new List<object>();
            using var client = DbCRUD<SocialPostDb>.DBClient(context);
            var store = new SocialInsightsStore(client);
            foreach (var post in posts)
            {
                var records = await LoadAsync(post, store, context);
                clicks.AddRange(records.Clicks);
                transactions.AddRange(records.Transactions);
                byPost.Add(new { social_post_id = post.SocialPostID, campaign_id = post.ResourceID,
                    platforms = post.Platforms, engagement = Aggregate(organizationID, post.SocialPostID, records.Clicks, records.Transactions) });
            }
            return new { status = "success", code = 200, campaign_id = campaignID,
                engagement = Aggregate(organizationID, "", clicks, transactions), analytics = byPost };
        }

        private async Task<(List<SocialPostClickDb> Clicks, List<TransactionAmountDb> Transactions)> LoadAsync(SocialPostDb post, SocialInsightsStore store, ILambdaContext context)
        {
            SocialTracking.Ensure(post, context);
            var clicks = await store.QueryAsync<SocialPostClickDb>(SocialInsightsStore.Query("SocialPostClick", "SocialPostID", post.SocialPostID));
            var transactions = await store.QueryAsync<TransactionAmountDb>(SocialInsightsStore.Query("TransactionAmount", "SocialPostID", post.SocialPostID));
            return (clicks.Where(c => c.AccountScopeKey == post.AccountScopeKey && c.AppName == post.AppName && c.Runway == post.Runway).ToList(),
                transactions.Where(t => t.Runway == post.Runway && t.SocialAccountScopeKey == post.AccountScopeKey && FormatValue.EmptyValue(t.SmsMessageID)).ToList());
        }

        public static SocialEngagementMetrics Aggregate(string organizationID, string postID,
            IEnumerable<SocialPostClickDb> clicks, IEnumerable<TransactionAmountDb> transactions)
        {
            var visits = clicks.Where(c => c.OrganizationID == organizationID && (postID.Length == 0 || c.SocialPostID == postID))
                .GroupBy(c => c.ClickID).Select(g => g.First()).ToList();
            // CompleteOneTimePay writes attribution to TransactionAmount. Later processor
            // status changes remain authoritative; never count payment initiation.
            var paid = transactions.Where(j => j.PayeeAccountID == organizationID && (postID.Length == 0 || j.SocialPostID == postID) &&
                    !string.IsNullOrEmpty(j.TransactionID))
                .GroupBy(j => j.TransactionID)
                .Select(g => g.OrderByDescending(j => j.LastUpdate).ThenByDescending(j => j.CreateDate).First())
                .Where(j => string.Equals(j.StatusState, "success", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var amounts = paid.GroupBy(j => string.IsNullOrWhiteSpace(j.Currency) ? "unknown" : j.Currency.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Sum(j => j.SocialDonationAmount));
            return new SocialEngagementMetrics
            {
                Clicks = visits.Count,
                UniqueVisitors = visits.Select(c => c.VisitorID).Distinct().Count(),
                Donations = paid.Count,
                Donors = paid.Select(j => string.IsNullOrWhiteSpace(j.PayerAccountID) ? j.PayerEmail?.Trim().ToLowerInvariant() : j.PayerAccountID).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Count(),
                AmountRaisedByCurrency = amounts,
                TotalAmountRaised = amounts.Count > 1 ? null : amounts.Values.Sum(),
                ConversionRate = visits.Count == 0 ? 0 : Math.Round(100m * paid.Count / visits.Count, 2),
                ClicksByPlatform = visits.GroupBy(c => c.Platform).ToDictionary(g => g.Key, g => g.Count())
            };
        }
    }
}
