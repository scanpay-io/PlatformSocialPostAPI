using Newtonsoft.Json;
using ScanPay.DataModel.Model;
using ScanPay.Utility.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService;

public sealed class SocialInsightsRequest
{
    [JsonProperty("post_id")] public string PostID { get; set; } = "";
    [JsonProperty("campaign_id")] public string CampaignID { get; set; } = "";
    [JsonProperty("from_utc")] public DateTime? FromUtc { get; set; }
    [JsonProperty("to_utc")] public DateTime? ToUtc { get; set; }
    [JsonProperty("outcome_before_utc")] public DateTime? OutcomeBeforeUtc { get; set; }
    [JsonProperty("view")] public string View { get; set; } = "summary";
    [JsonProperty("limit")] public int Limit { get; set; } = 50;
    [JsonProperty("next_token")] public string NextToken { get; set; } = "";

    public void ApplyPostFallback(string? postID)
    {
        // Legacy Lambda requests use a single space as their empty value.
        if (string.IsNullOrWhiteSpace(PostID))
            PostID = string.IsNullOrWhiteSpace(postID) ? "" : postID;
    }

    public void Validate(DateTime now)
    {
        if (View is not ("summary" or "deliveries") || Limit < 1 || Limit > 100)
            throw ResponseStatusFactory.BadRequest("Use view=summary or deliveries and a limit between 1 and 100.");
        if (!string.IsNullOrEmpty(PostID))
        {
            if (!Guid.TryParseExact(PostID, "D", out _) || FromUtc.HasValue || ToUtc.HasValue || !string.IsNullOrEmpty(CampaignID))
                throw ResponseStatusFactory.BadRequest("post_id must be a GUID and cannot be combined with cohort filters.");
            PostID = Guid.Parse(PostID).ToString();
        }
        else if (!FromUtc.HasValue || !ToUtc.HasValue || FromUtc >= ToUtc || ToUtc - FromUtc > TimeSpan.FromDays(90))
            throw ResponseStatusFactory.BadRequest("Cohorts require from_utc < to_utc, at most 90 days apart.");
        if (FromUtc > now || ToUtc > now) throw ResponseStatusFactory.BadRequest("Publication dates cannot be in the future.");
        OutcomeBeforeUtc ??= now;
        if (OutcomeBeforeUtc > now || OutcomeBeforeUtc < DateTime.UnixEpoch || OutcomeBeforeUtc < ToUtc)
            throw ResponseStatusFactory.BadRequest("outcome_before_utc must be at or after to_utc and no later than now.");
        if (View == "summary" && !string.IsNullOrEmpty(NextToken))
            throw ResponseStatusFactory.BadRequest("Pagination requires view=deliveries.");
    }
}

public static class SocialInsights
{
    public static object Rate(int numerator, int denominator) => new
    { numerator, denominator, percentage = denominator == 0 ? (decimal?)null : Math.Round(100m * numerator / denominator, 2) };

    public static object Report(string organization, string scope, IEnumerable<SocialPostDb> posts,
        IEnumerable<SocialPostDeliveryDb> deliveries, IEnumerable<SocialPostClickDb> clicks,
        IEnumerable<TransactionAmountDb> transactions, DateTime cutoff, bool includeDeliveries)
    {
        var owned = posts.Where(p => SocialTracking.Owns(p, organization, scope)).ToDictionary(p => p.SocialPostID);
        var selected = deliveries.Where(d => owned.TryGetValue(d.SocialPostID, out var p) && SocialTracking.Owns(d, p) &&
            d.PublishedDateUtc.HasValue && d.PublishedDateUtc < cutoff).GroupBy(d => d.DeliveryID).Select(g => g.First()).ToList();
        var byDelivery = selected.ToDictionary(d => d.DeliveryID);
        var byPost = selected.GroupBy(d => d.SocialPostID).ToDictionary(g => g.Key, g => g.Min(d => d.PublishedDateUtc!.Value));
        var visits = clicks.Where(c => c.OrganizationID == organization && c.AccountScopeKey == scope &&
            c.AppName + ":" + c.Runway + ":" + organization == scope && byPost.TryGetValue(c.SocialPostID, out var start) &&
            c.CreateDate >= start && c.CreateDate < cutoff && (string.IsNullOrEmpty(c.DeliveryID) ||
                byDelivery.TryGetValue(c.DeliveryID, out var d) && d.SocialPostID == c.SocialPostID &&
                !string.IsNullOrEmpty(d.TrackingUrl) && c.CreateDate >= d.PublishedDateUtc))
            .GroupBy(c => c.ClickID).Select(g => g.First()).ToList();
        // Payment records currently prove post attribution only. Never infer a donation's
        // delivery/platform from the first click or from the post's platform list.
        var paid = transactions.Where(t => t.PayeeAccountID == organization && byPost.ContainsKey(t.SocialPostID) &&
            t.Runway == owned[t.SocialPostID].Runway && t.SocialAccountScopeKey == scope && FormatValue.EmptyValue(t.SmsMessageID) && !string.IsNullOrWhiteSpace(t.TransactionID))
            .GroupBy(t => t.TransactionID).Select(g => g.OrderByDescending(t => t.LastUpdate).ThenByDescending(t => t.CreateDate).First())
            .Where(t => t.StatusState == "success" && t.CreateDate >= byPost[t.SocialPostID] && t.CreateDate < cutoff).ToList();
        var tracked = selected.Where(d => !string.IsNullOrEmpty(d.TrackingUrl)).ToList();
        var linkedVisits = visits.Where(c => !string.IsNullOrEmpty(c.DeliveryID)).ToList();
        return new
        {
            published_deliveries = selected.Count, tracked_published_deliveries = tracked.Count,
            tracking_coverage = Rate(tracked.Count, selected.Count),
            landing_events = visits.Count, unique_browser_visitors = visits.Select(c => c.VisitorID).Distinct().Count(),
            unattributed_delivery_landings = visits.Count - linkedVisits.Count,
            deliveries_with_landing = linkedVisits.Select(c => c.DeliveryID).Distinct().Count(),
            posts_with_donation = paid.Select(t => t.SocialPostID).Distinct().Count(), donations = paid.Count,
            amount_raised_by_currency = paid.GroupBy(t => string.IsNullOrWhiteSpace(t.Currency) ? "unknown" : t.Currency.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Sum(t => t.SocialDonationAmount)),
            donations_per_landing = Rate(paid.Count, visits.Count),
            delivery_donation_attribution = "unavailable_post_only_payment_contract",
            deliveries = includeDeliveries ? selected.Select(d => new
            {
                delivery_id = d.DeliveryID, social_post_id = d.SocialPostID, platform = d.Platform,
                published_at_utc = d.PublishedDateUtc, tracking_status = string.IsNullOrEmpty(d.TrackingUrl) ? "untracked" : "tracked",
                landing_events = string.IsNullOrEmpty(d.TrackingUrl) ? (int?)null : linkedVisits.Count(c => c.DeliveryID == d.DeliveryID),
                donations = (int?)null
            }).ToArray() : null
        };
    }
}
