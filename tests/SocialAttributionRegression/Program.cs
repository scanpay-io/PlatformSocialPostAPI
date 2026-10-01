using Newtonsoft.Json;
using ScanPay.DataModel.Model;
using ScanPay.Payment.Model;
using ScanPay.SocialPostService;

var postID = "11111111-2222-4333-8444-555555555555";
var post = new SocialPostAttributionRecord { SocialPostID = postID, OrganizationID = "org", ResourceID = "campaign" };
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
Check(SocialAttribution.Matches(post, "org", "campaign"), "Valid campaign");
Check(!SocialAttribution.Matches(post, "other", "campaign"), "Reject cross-organization attribution");
Check(!SocialAttribution.Matches(post, "org", "other"), "Reject cross-campaign attribution");
var json = "{\"social_post_id\":\"" + postID + "\"}";
Check(JsonConvert.DeserializeObject<SocialClickRequest>("{\"social_platform\":\"facebook\"}").SocialPlatformName == "facebook", "Social platform does not collide with Lambda client platform");
var redirect = SocialAttribution.AppendToUrl("https://example.org/give?keep=yes&socialpostid=old#payment", postID);
Check(redirect == "https://example.org/give?keep=yes&socialpostid=" + postID + "#payment", "Short-link attribution preserves query and fragment and replaces old ID");
Check(JsonConvert.DeserializeObject<OneTimePayRequest>(json).SocialPostID == postID, "One-time request contract");
Check(JsonConvert.DeserializeObject<SchedulePayment>(json).SocialPostID == postID, "Schedule request contract");
Check(JsonConvert.DeserializeObject<DonorPledge>(json).SocialPostID == postID, "Pledge request contract");
var clicks = new[] {
    new SocialPostClickDb { ClickID = "click1", VisitorID = "visitor1", OrganizationID = "org", SocialPostID = postID, Platform = "facebook" },
    new SocialPostClickDb { ClickID = "click1", VisitorID = "visitor1", OrganizationID = "org", SocialPostID = postID, Platform = "facebook" },
    new SocialPostClickDb { ClickID = "click2", VisitorID = "visitor1", OrganizationID = "org", SocialPostID = postID, Platform = "facebook" },
    new SocialPostClickDb { ClickID = "other", VisitorID = "other", OrganizationID = "other", SocialPostID = postID }
};
TransactionAmountDb Payment(string id, string status, decimal amount, int minute = 0) => new() {
    TransactionID = id, PayeeAccountID = "org", SocialPostID = postID,
    PayerAccountID = "donor", SocialDonationAmount = amount, StatusState = status, Currency = "usd",
    LastUpdate = new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc)
};
var records = new[] { Payment("paid", "success", 25), Payment("paid", "success", 25),
    Payment("pending", "pending", 100), Payment("failed", "failed", 50),
    Payment("refunded", "success", 70), Payment("refunded", "refunded", 70, 1) };
var metrics = SocialEngagementService.Aggregate("org", postID, clicks, records);
Check(metrics.Clicks == 2 && metrics.UniqueVisitors == 1, "Deduplicate clicks, distinguish visitors");
Check(metrics.Donations == 1 && metrics.Donors == 1 && metrics.TotalAmountRaised == 25, "Only latest successful, deduplicated payments count");
Check(metrics.ConversionRate == 50, "Conversion is successful donations / clicks as percent");
Check(metrics.ClicksByPlatform["facebook"] == 2, "Platform clicks");
Check(SocialEngagementService.Aggregate("org", postID, Array.Empty<SocialPostClickDb>(), records).ConversionRate == 0, "Zero denominator");
var foreign = Payment("foreign", "success", 30); foreign.Currency = "cad";
Check(SocialEngagementService.Aggregate("org", postID, clicks, records.Append(foreign)).TotalAmountRaised == null, "Do not sum different country currencies");
Console.WriteLine("Social attribution regression checks passed.");
