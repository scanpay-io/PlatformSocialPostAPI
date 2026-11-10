using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScanPay.DataModel.Model;
using ScanPay.SocialPostService;
using ScanPay.Utility.Model;

static class CohortChecks
{
    public static async Task RunAsync()
    {
        int count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; }
        async Task Reject(Func<Task> action, string message) { try { await action(); } catch (ResponseStatusException) { count++; return; } throw new Exception(message); }
        var now = DateTime.UtcNow;
        var sent = now.AddDays(-2);
        const string id = "11111111-2222-4333-8444-555555555555", scope = "gany:dev:org";
        var post = new SocialPostDb { SocialPostID = id, OrganizationID = "org", AppName = "gany", Runway = "dev", AccountScopeKey = scope, ResourceID = "campaign" };
        SocialPostDeliveryDb Delivery(string platform) => new() { DeliveryID = platform + "-" + id, SocialPostID = id, OrganizationID = "org",
            AppName = "gany", Runway = "dev", AccountScopeKey = scope, Platform = platform, PublishedDateUtc = sent, TrackingUrl = "https://example.org/give" };
        var facebook = Delivery("facebook"); var threads = Delivery("threads");
        SocialPostClickDb Click(string click, string delivery) => new() { ClickID = click, SocialPostID = id, DeliveryID = delivery,
            OrganizationID = "org", AppName = "gany", Runway = "dev", AccountScopeKey = scope, VisitorID = "visitor", CreateDate = sent.AddHours(1), Platform = "facebook" };
        TransactionAmountDb Gift(string transaction, string status = "success") => new() { TransactionID = transaction, SocialPostID = id,
            PayeeAccountID = "org", Runway = "dev", SocialAccountScopeKey = scope, StatusState = status, Currency = "usd", SocialDonationAmount = 10, CreateDate = sent.AddHours(2), LastUpdate = sent.AddHours(2) };
        var requestBody = new SocialPostRequest { OrganizationID = "victim", OwnerAccountID = "victim", AccountID = "victim" };
        SocialAuthorization.ApplyOrganization(requestBody, "org");
        Check(requestBody.EffectiveOrganizationID == "org" && requestBody.OwnerAccountID == "org", "Untrusted organization selection overwritten");
        Check(SocialAuthorization.Actor(JObject.Parse("{ 'body': '{\"sub\":\"attacker\"}', 'sub':'attacker' }")) == "", "Body identity ignored");
        await Reject(() => SocialAuthorization.HydrateAsync(new JObject(), requestBody, null!), "Unauthenticated action rejected before database access");
        Check(!SocialTracking.Owns(post, "org", "gany:prod:org"), "Environment isolation");
        var forged = JsonConvert.DeserializeObject<SocialPostDb>(JsonConvert.SerializeObject(post))!; forged.Runway = "prod";
        Check(!SocialTracking.Owns(forged, "org", scope), "Scope key alone is not authorization");
        Check(!SocialTracking.Owns(new SocialPostDb { OrganizationID = "org" }, "org", scope), "Legacy unscoped post fails closed");
        var url = SocialTracking.Destination("https://example.org/give?keep=yes&smsid=old&socialvisitorid=private&socialpostid=old#pay", id, facebook.DeliveryID);
        Check(url.Contains("socialdeliveryid=facebook-") && url.Contains("keep=yes") && url.EndsWith("#pay") && !url.Contains("private") && !url.Contains("smsid"), "Tracking URL replaces stale identity and preserves destination");
        await Reject(() => { SocialTracking.Destination("http://example.org", id, "delivery"); return Task.CompletedTask; }, "HTTPS required");
        Check(facebook.PublishedAtKey == sent.ToString("o") && new SocialPostDeliveryDb().PublishedAtKey == "unpublished", "Index key never null");
        var storedDelivery = DbCRUD<SocialPostDeliveryDb>.ToStorageAttributes(facebook);
        Check(storedDelivery["PublishedAtKey"].S == sent.ToString("o"), "Actual DynamoDB converter writes publication index key");
        Check(DbCRUD<SocialPostDeliveryDb>.ToStorageAttributes(new SocialPostDeliveryDb())["PublishedAtKey"].S == "unpublished", "Actual DynamoDB converter avoids NULL index key");
        var paymentPost = new SocialPostAttributionRecord { SocialPostID = id, OrganizationID = "org", ResourceID = "campaign", AppName = "gany", Runway = "dev", AccountScopeKey = scope };
        Check(SocialAttribution.MatchesEnvironment(paymentPost, "org", "gany", "dev") &&
            !SocialAttribution.MatchesEnvironment(paymentPost, "org", "otherapp", "dev") &&
            !SocialAttribution.MatchesEnvironment(paymentPost, "org", "gany", "prod"), "Payment validation isolates app and environment");
        Check(SocialAttribution.AppendToUrl("https://example.org/give?smsid=stale#pay", id, facebook.DeliveryID) ==
            "https://example.org/give?socialpostid=" + id + "&socialdeliveryid=" + facebook.DeliveryID + "#pay", "Resolvers preserve delivery and replace competing channel");
        await Reject(() => { SocialAttribution.AppendToUrl("https://example.org", id, "facebook-00000000-0000-0000-0000-000000000000"); return Task.CompletedTask; }, "Resolver rejects delivery belonging to another post");
        Check(SocialEngagementService.SelectDelivery(post, new[] { facebook, threads }, facebook.DeliveryID, "facebook") == facebook, "Published delivery resolves");
        Check(SocialEngagementService.SelectDelivery(post, new[] { facebook, threads }, "", "facebook") == null, "Legacy platform hint cannot select multi-platform delivery");
        await Reject(() => { SocialEngagementService.SelectDelivery(post, new[] { facebook }, "unknown", ""); return Task.CompletedTask; }, "Unknown delivery rejected");
        var draft = Delivery("draft"); draft.PublishedDateUtc = null;
        await Reject(() => { SocialEngagementService.SelectDelivery(post, new[] { draft }, "", ""); return Task.CompletedTask; }, "Draft activity rejected");
        var click = Click("click", facebook.DeliveryID);
        var foreign = Click("foreign", facebook.DeliveryID); foreign.Runway = "prod";
        var refund = Gift("refund", "refunded"); refund.LastUpdate = now;
        var prodGift = Gift("prod"); prodGift.Runway = "prod";
        var bothChannels = Gift("both"); bothChannels.SmsMessageID = "sms";
        var otherApp = Gift("otherapp"); otherApp.SocialAccountScopeKey = "otherapp:dev:org";
        var unscoped = Gift("legacy"); unscoped.SocialAccountScopeKey = "";
        var gifts = new[] { Gift("one"), Gift("one"), Gift("two"), Gift("refund"), refund, prodGift, bothChannels, otherApp, unscoped, Gift("pending", "pending") };
        var report = JObject.FromObject(SocialInsights.Report("org", scope, new[] { post }, new[] { facebook, threads }, new[] { click, click, foreign }, gifts, now, true));
        Check((int)report["landing_events"]! == 1 && (int)report["donations"]! == 2, "Dedup, refund, channel and environment filtering");
        Check((decimal)report["amount_raised_by_currency"]!["usd"]! == 20, "Revenue principal totals");
        Check(report["deliveries"]![0]!["donations"]!.Type == JTokenType.Null, "No fabricated delivery donation attribution");
        Check((int)report["deliveries_with_landing"]! == 1 && (int)report["posts_with_donation"]! == 1, "Distinct post/delivery counts");
        Check(JObject.FromObject(SocialInsights.Rate(0, 0))["percentage"]!.Type == JTokenType.Null, "Undefined rate stays null");
        var after = Click("after", facebook.DeliveryID); after.CreateDate = now;
        Check((int)JObject.FromObject(SocialInsights.Report("org", scope, new[] { post }, new[] { facebook }, new[] { after }, Array.Empty<TransactionAmountDb>(), now, false))["landing_events"]! == 0, "Exclusive outcome cutoff");

        SocialInsightsRequest Request()
        {
            var request = new SocialInsightsRequest { FromUtc = sent.AddDays(-1), ToUtc = sent.AddDays(1), View = "deliveries", Limit = 1 };
            request.ApplyPostFallback(new SocialPostRequest().SocialPostID);
            return request;
        }
        Check(Request().PostID == "", "Legacy blank post fallback does not turn a cohort into a post report");
        var postOnly = new SocialInsightsRequest(); postOnly.ApplyPostFallback(id);
        Check(postOnly.PostID == id, "Legacy post route still supplies the post ID");
        postOnly.ApplyPostFallback("another-post");
        Check(postOnly.PostID == id, "Explicit post query takes precedence over fallback");
        var key = Enumerable.Repeat((byte)42, 32).ToArray();
        using var db = new FakeSocialDb { Post = post, Deliveries = new() { facebook, threads }, Clicks = new() { click }, Gifts = gifts.ToList() };
        var page = JObject.FromObject(await new SocialInsightsStore(db, key).ReadAsync("org", scope, Request(), now));
        Check(page["analytics"]!.Type == JTokenType.Null && page["deliveries"]!.Count() == 1, "Page is not a partial-looking summary");
        var token = (string)page["next_token"]!;
        Check(token.Length > 0 && db.Queries.Any(q => q.IndexName == "AccountPublishedAt-index"), "Publication cohort index and cursor used");
        var next = Request(); next.NextToken = token;
        var page2 = JObject.FromObject(await new SocialInsightsStore(db, key).ReadAsync("org", scope, next, now.AddMinutes(1)));
        Check((string)page2["deliveries"]![0]!["delivery_id"]! == threads.DeliveryID && (DateTime)page2["outcome_before_utc"]! == now, "Pagination freezes cutoff and continues deterministically");
        await Reject(() => { SocialInsightsCursor.Decode(token, "gany:prod:org", Request(), key); return Task.CompletedTask; }, "Cursor scope binding");
        var changed = Request(); changed.Limit = 2;
        await Reject(() => { SocialInsightsCursor.Decode(token, scope, changed, key); return Task.CompletedTask; }, "Cursor filter binding");
        await Reject(() => { SocialInsightsCursor.Decode(token + "x", scope, Request(), key); return Task.CompletedTask; }, "Cursor tampering");
        await Reject(() => { new SocialInsightsRequest().Validate(now); return Task.CompletedTask; }, "Missing cohort range rejected");
        var longRange = Request(); longRange.FromUtc = now.AddDays(-100);
        await Reject(() => { longRange.Validate(now); return Task.CompletedTask; }, "90-day limit");
        db.Deliveries = Enumerable.Range(0, 2001).Select(i => Delivery("d" + i)).ToList();
        await Reject(async () => await new SocialInsightsStore(db, key).ReadAsync("org", scope, Request(), now), "Oversized cohort fails explicitly");
        db.Deliveries = new() { facebook }; db.ScannedCount = 50001;
        await Reject(async () => await new SocialInsightsStore(db, key).ReadAsync("org", scope, Request(), now), "Evaluated-record budget enforced");
        db.ScannedCount = null; db.MissingIndex = true;
        await Reject(async () => await new SocialInsightsStore(db, key).ReadAsync("org", scope, Request(), now), "Missing index fails visibly");
        Console.WriteLine($"Passed {count} social cohort checks.");
    }
}

sealed class FakeSocialDb : AmazonDynamoDBClient
{
    public SocialPostDb Post = new();
    public List<SocialPostDeliveryDb> Deliveries = new();
    public List<SocialPostClickDb> Clicks = new();
    public List<TransactionAmountDb> Gifts = new();
    public List<QueryRequest> Queries = new();
    public int? ScannedCount;
    public bool MissingIndex;
    public FakeSocialDb() : base(new BasicAWSCredentials("fake", "fake"), new AmazonDynamoDBConfig { ServiceURL = "http://localhost:1" }) { }
    static Dictionary<string, AttributeValue> Row(object value) => Document.FromJson(JsonConvert.SerializeObject(value)).ToAttributeMap();
    public override Task<GetItemResponse> GetItemAsync(GetItemRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GetItemResponse { Item = Row(Post) });
    public override Task<QueryResponse> QueryAsync(QueryRequest request, CancellationToken cancellationToken = default)
    {
        if (MissingIndex) throw new ResourceNotFoundException("missing");
        lock (Queries) Queries.Add(request);
        var rows = request.TableName == "SocialPostDelivery" ? Deliveries.Select(Row).ToList() :
            request.TableName == "SocialPostClick" ? Clicks.Select(Row).ToList() : Gifts.Select(Row).ToList();
        return Task.FromResult(new QueryResponse { Items = rows, ScannedCount = ScannedCount ?? rows.Count, ConsumedCapacity = new() { CapacityUnits = 1 } });
    }
}
