using System.Net;
using Newtonsoft.Json.Linq;
using ScanPay.SocialPostService;
using ScanPay.Utility.Model;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Interlocked.Increment(ref checks); }
async Task Reject(Func<Task> action, string message)
{
    try { await action(); } catch (ResponseStatusException) { checks++; return; }
    throw new Exception(message);
}
static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
static SocialPostDb Post(params string[] platforms) => new()
{
    SocialPostID = "post", OrganizationID = "org", Content = "Support our campaign", DestinationUrl = "https://example.org/give",
    Platforms = platforms.ToList(), MetaSocial = platforms.Select(p => new MetaSocialPostOptions
    { Platform = p, ImageUrl = p == "instagram" ? "https://example.org/photo.jpg" : "" }).ToList()
};
static async Task<Dictionary<string, string>> Form(HttpRequestMessage request) => (await request.Content!.ReadAsStringAsync())
    .Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

// Full multi-platform workflow: Page discovery, direct Facebook send, containers and status polling.
var store = MemoryStore.For("facebook", "instagram", "threads");
int sends = 0;
using var http = new HttpClient(new ProviderHttpHandler(async request =>
{
    string path = request.RequestUri!.AbsolutePath;
    string host = request.RequestUri.Host;
    Check(request.RequestUri.Scheme == "https" && !request.RequestUri.Query.Contains("access_token="), "Credentials must not be in URLs");
    if (path.EndsWith("/accounts"))
    {
        Check(request.Headers.Authorization?.Parameter == "user-token", "Page lookup uses user token");
        return Json("{'data':[{'id':'456','name':'Nonprofit','access_token':'page-token','tasks':['CREATE_CONTENT']}]} ");
    }
    if (request.Method == HttpMethod.Get)
        return Json(host == "graph.instagram.com" ? "{'status_code':'FINISHED'}" : "{'status':'FINISHED'}");
    Interlocked.Increment(ref sends);
    var form = await Form(request);
    if (path.EndsWith("/feed"))
    {
        Check(path == "/v25.0/456/feed" && request.Headers.Authorization?.Parameter == "page-token", "Facebook posts as the selected Page");
        Check(form["message"] == "Support our campaign" && form["link"] == "https://example.org/give", "Facebook link contract");
        return Json("{'id':'456_789'}");
    }
    Check(request.Headers.Authorization?.Parameter == "user-token", "Instagram/Threads use their own user token");
    if (path.EndsWith("/media"))
    {
        Check(host == "graph.instagram.com" && form["image_url"].EndsWith("photo.jpg") && form["caption"].Contains("/give"), "Instagram Login image payload");
        return Json("{'id':'111'}");
    }
    if (path.EndsWith("/threads"))
    {
        Check(host == "graph.threads.net" && form["media_type"] == "TEXT" && form["text"].Contains("/give"), "Threads text payload");
        return Json("{'id':'222'}");
    }
    Check(form["creation_id"] == (host == "graph.instagram.com" ? "111" : "222"), "Publish uses saved container ID");
    return Json("{'id':'999'}");
}));
var service = new MetaSocialPublishingService(new(http), store);
var post = Post("facebook", "instagram", "threads");
await service.PublishAsync(post, null!);
Check(store.Deliveries["facebook-post"].Status == "published", "Facebook must confirm publication");
Check(store.Deliveries["instagram-post"].ContainerID == "111" && store.Deliveries["threads-post"].Status == "published", "Ready containers are published during Publish now");
Check(post.Status == "published" && sends == 5, "Publish now completes ready containers");
await service.PublishAsync(post, null!);
Check(sends == 5, "Repeated publish must not duplicate remote sends");
await Task.WhenAll(service.RefreshStatusAsync(post, null!), service.RefreshStatusAsync(post, null!));
Check(sends == 5 && store.Deliveries.Values.All(d => d.Status == "published"), "Concurrent polls publish each container once");
await service.RefreshStatusAsync(post, null!);
Check(sends == 5 && post.Status == "published" && post.PublishedDateUtc.HasValue, "Published containers are never sent again");
var safePages = await service.FacebookPagesAsync(store.Connections.First(), null!);
Check(!Newtonsoft.Json.JsonConvert.SerializeObject(safePages).Contains("page-token"), "Page tokens never enter frontend responses");

foreach (string platform in new[] { "facebook", "instagram", "threads" })
{
    var badStore = MemoryStore.For(platform);
    badStore.Providers[platform].PostingEnabled = false;
    await Reject(() => new MetaSocialPublishingService(new(http), badStore).PublishAsync(Post(platform), null!), "Disabled provider must reject");
    badStore.Providers[platform].PostingEnabled = true;
    badStore.Tokens[platform].OrganizationID = "other";
    await Reject(() => new MetaSocialPublishingService(new(http), badStore).PublishAsync(Post(platform), null!), "Cross-tenant token must reject");
    badStore.Tokens[platform].OrganizationID = "org";
    badStore.Tokens[platform].TokenExpiresDateUtc = DateTime.UtcNow.AddMinutes(-1);
    await Reject(() => new MetaSocialPublishingService(new(http), badStore).PublishAsync(Post(platform), null!), "Expired token must reject");
    Check(badStore.Deliveries.Count == 0, "Preflight failure must not create deliveries");
}
var invalid = Post("instagram");
invalid.MetaSocial.Clear();
await Reject(() => Task.FromResult(MetaSocialPublishingService.Validate(invalid, "instagram")), "Instagram text-only post must reject");
invalid = Post("threads"); invalid.Content = new string('x', 501);
await Reject(() => Task.FromResult(MetaSocialPublishingService.Validate(invalid, "threads")), "Threads text length must reject");
invalid = Post("instagram"); invalid.MetaSocial[0].ImageUrl = "http://localhost/photo.jpg";
await Reject(() => Task.FromResult(MetaSocialPublishingService.Validate(invalid, "instagram")), "Invalid image URL must reject");
invalid = Post("threads", "x");
await Reject(() => Task.FromResult(MetaSocialPublishingService.Validate(invalid, "threads")), "Mixed dedicated platforms must reject");
invalid = Post("threads"); invalid.MetaSocial.Add(new() { Platform = "threads" });
await Reject(() => Task.FromResult(MetaSocialPublishingService.Validate(invalid, "threads")), "Duplicate platform settings must reject");

// Provider timeout and storage failure must never cause an automatic resend.
foreach (bool databaseFailure in new[] { false, true })
{
    var failureStore = MemoryStore.For("threads");
    failureStore.FailDeliverySaves = databaseFailure ? 1 : 0;
    int attempts = 0;
    using var failureHttp = new HttpClient(new ProviderHttpHandler(_ =>
    {
        attempts++;
        if (!databaseFailure) throw new TaskCanceledException("timeout");
        return Task.FromResult(Json("{'id':'333'}"));
    }));
    var failureService = new MetaSocialPublishingService(new(failureHttp), failureStore);
    var failurePost = Post("threads");
    await failureService.PublishAsync(failurePost, null!);
    await failureService.PublishAsync(failurePost, null!);
    Check(attempts == 1 && failurePost.Status == "submission_unknown", "Ambiguous initialization must not retry");
}
foreach (string status in new[] { "IN_PROGRESS", "ERROR", "EXPIRED" })
{
    var statusStore = MemoryStore.For("instagram");
    statusStore.Deliveries["instagram-post"] = new() { DeliveryID = "instagram-post", SocialPostID = "post", OrganizationID = "org",
        Platform = "instagram", SocialConnectionID = "instagram", TargetAccountID = "123", ContainerID = "333", Status = "processing" };
    using var statusHttp = new HttpClient(new ProviderHttpHandler(request =>
    {
        Check(request.Method == HttpMethod.Get, "Unready/failed containers must not publish");
        return Task.FromResult(Json("{'status_code':'" + status + "'}"));
    }));
    var statusPost = Post("instagram");
    await new MetaSocialPublishingService(new(statusHttp), statusStore).RefreshStatusAsync(statusPost, null!);
    Check(statusStore.Deliveries.Values.Single().Status == (status == "IN_PROGRESS" ? "processing" : "failed"), "Container state must be reflected accurately");
}
using var rejectedHttp = new HttpClient(new ProviderHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
{ Content = new StringContent("secret-token-diagnostic") })));
try { await new MetaSocialProviderClient(rejectedHttp).CreateContainerAsync("threads", "123", "token", "text", ""); }
catch (ResponseStatusException ex) { Check(!ex.Message.Contains("secret-token"), "Provider diagnostics must not leak"); }
Check(MetaSocialPublishingService.AggregateStatus(new[] { new SocialPostDeliveryDb { Status = "published" }, new SocialPostDeliveryDb { Status = "failed" } }, 2) == "partially_published", "Partial failure aggregation");

using var imageHttp = new HttpClient(new ProviderHttpHandler(async request =>
{
    var form = await Form(request);
    if (request.RequestUri!.Host == "graph.facebook.com")
        Check(request.RequestUri.AbsolutePath.EndsWith("/photos") && form["url"] == "https://example.org/photo.jpg" &&
            form["caption"].Contains("https://example.org/give") && form["published"] == "true", "Facebook image contract includes campaign link");
    else Check(form["media_type"] == "IMAGE" && form["image_url"] == "https://example.org/photo.jpg", "Threads image contract");
    return Json("{'id':'555','post_id':'123_555'}");
}));
Check(await new MetaSocialProviderClient(imageHttp).PublishFacebookAsync("123", "token", "Caption", "https://example.org/give", "https://example.org/photo.jpg") == "123_555", "Facebook photo result uses post ID");
await new MetaSocialProviderClient(imageHttp).CreateContainerAsync("threads", "123", "token", "Caption", "https://example.org/photo.jpg");

int pageRequests = 0;
using var pagesHttp = new HttpClient(new ProviderHttpHandler(request =>
{
    pageRequests++;
    Check(request.RequestUri!.Host == "graph.facebook.com", "Pagination cannot exfiltrate token to next URL");
    if (pageRequests == 1) return Task.FromResult(Json("{'data':[{'id':'1','access_token':'secret','tasks':['ANALYZE']}], 'paging':{'next':'https://evil.example/steal','cursors':{'after':'cursor&x=1'}}}"));
    Check(request.RequestUri.Query.Contains("after=cursor%26x%3D1"), "Pagination cursor must be escaped");
    return Task.FromResult(Json("{'data':[{'id':'2','name':'Page','access_token':'secret','tasks':['CREATE_CONTENT']}]}"));
}));
Check((await new MetaSocialProviderClient(pagesHttp).FacebookPagesAsync("token")).Single().ID == "2", "Analytics-only Page role excluded");

var ambiguousStore = MemoryStore.For("facebook");
using var ambiguousHttp = new HttpClient(new ProviderHttpHandler(request =>
{
    Check(request.Method == HttpMethod.Get, "Ambiguous Page selection must never send a post");
    return Task.FromResult(Json("{'data':[{'id':'1','access_token':'a','tasks':['CREATE_CONTENT']},{'id':'2','access_token':'b','tasks':['CREATE_CONTENT']}]}"));
}));
await Reject(() => new MetaSocialPublishingService(new(ambiguousHttp), ambiguousStore).PublishAsync(Post("facebook"), null!), "Multiple Pages require explicit selection");
var wrongPage = Post("facebook"); wrongPage.MetaSocial[0].FacebookPageID = "999";
await Reject(() => new MetaSocialPublishingService(new(ambiguousHttp), ambiguousStore).PublishAsync(wrongPage, null!), "Unmanaged Page must reject");

using var noIdHttp = new HttpClient(new ProviderHttpHandler(_ => Task.FromResult(Json("{}"))));
await Reject(() => new MetaSocialProviderClient(noIdHttp).CreateContainerAsync("threads", "123", "token", "Caption", ""), "Missing container ID must reject");
await Reject(() => new MetaSocialProviderClient(noIdHttp).PublishContainerAsync("instagram", "123", "token", "456"), "Missing publication ID must reject");

var timeoutStore = MemoryStore.For("threads");
timeoutStore.Deliveries["threads-post"] = new() { DeliveryID = "threads-post", SocialPostID = "post", OrganizationID = "org", Platform = "threads",
    SocialConnectionID = "threads", TargetAccountID = "123", ContainerID = "444", Status = "processing" };
int publishAttempts = 0;
using var timeoutHttp = new HttpClient(new ProviderHttpHandler(request =>
{
    if (request.Method == HttpMethod.Get) return Task.FromResult(Json("{'status':'FINISHED'}"));
    publishAttempts++;
    throw new TaskCanceledException();
}));
var timeoutService = new MetaSocialPublishingService(new(timeoutHttp), timeoutStore);
var timeoutPost = Post("threads");
await timeoutService.RefreshStatusAsync(timeoutPost, null!);
await timeoutService.RefreshStatusAsync(timeoutPost, null!);
Check(publishAttempts == 1 && timeoutPost.Status == "submission_unknown", "Ambiguous final publish cannot be retried by polling");

var legacyStore = MemoryStore.For("threads");
legacyStore.Deliveries["old"] = new() { DeliveryID = "old", SocialPostID = "post", OrganizationID = "org", Platform = "threads", Status = "queued" };
await Reject(() => new MetaSocialPublishingService(new(http), legacyStore).PublishAsync(Post("threads"), null!), "Retired queue records need explicit recreation");
var cancelled = Post("threads"); cancelled.Status = "cancelled";
await Reject(() => service.PublishAsync(cancelled, null!), "Cancelled post must reject");
// Explicit rejections are failed deliveries, while ambiguous responses retain duplicate protection.
foreach (bool finalPublish in new[] { false, true })
foreach (var responseCode in new[] { HttpStatusCode.BadRequest, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests,
    HttpStatusCode.OK, HttpStatusCode.InternalServerError, HttpStatusCode.RequestTimeout })
{
    var rejectionStore = MemoryStore.For("instagram");
    var rejectionPost = Post("instagram");
    if (finalPublish)
        rejectionStore.Deliveries["instagram-post"] = new() { DeliveryID = "instagram-post", SocialPostID = "post", OrganizationID = "org",
            Platform = "instagram", SocialConnectionID = "instagram", TargetAccountID = "123", ContainerID = "333", Status = "processing" };
    int attempts = 0;
    using var rejectionHttp = new HttpClient(new ProviderHttpHandler(request =>
    {
        if (request.Method == HttpMethod.Get) return Task.FromResult(Json("{'status_code':'FINISHED'}"));
        attempts++;
        return Task.FromResult(new HttpResponseMessage(responseCode)
        { Content = new StringContent("{\"error\":{\"code\":190,\"message\":\"secret-token-diagnostic\"}}") });
    }));
    var rejectionService = new MetaSocialPublishingService(new(rejectionHttp), rejectionStore);
    if (finalPublish) await rejectionService.RefreshStatusAsync(rejectionPost, null!);
    else await rejectionService.PublishAsync(rejectionPost, null!);
    bool uncertain = responseCode is HttpStatusCode.InternalServerError or HttpStatusCode.RequestTimeout;
    var delivery = rejectionStore.Deliveries.Values.Single();
    Check(delivery.Status == (uncertain ? "submission_unknown" : "failed") && rejectionPost.Status == delivery.Status,
        "Explicit rejection and ambiguous remote outcomes must be distinguished at both write stages");
    Check(!delivery.ErrorMessage.Contains("secret-token"), "Stored delivery errors must not leak provider diagnostics");
    if (!uncertain) Check(delivery.ErrorCode == "190" && delivery.ErrorMessage.Contains("instagram"), "Safe rejection code and platform must be retained");
    await rejectionService.PublishAsync(rejectionPost, null!);
    await rejectionService.RefreshStatusAsync(rejectionPost, null!);
    Check(attempts == 1, "Neither failed nor ambiguous deliveries may be resubmitted automatically");
}
var draftStore = MemoryStore.For("instagram");
var draftPost = Post("instagram");
await new MetaSocialPublishingService(new(http), draftStore).RefreshStatusAsync(draftPost, null!);
Check(draftPost.Status == "draft" && draftStore.SavedPost == null, "Status checks must preserve unsubmitted drafts");
foreach (var rawTimestamp in new[] { "2026-10-29T14:58:52.5183319Z", "2026-10-29T14:58:52.5183311Z", "2026-10-29T14:58:52.5183310Z" })
{
var timestampAttributes = new Dictionary<string, Amazon.DynamoDBv2.Model.AttributeValue>
{
    ["SocialPostID"] = new("post"), ["OrganizationID"] = new("org"), ["Status"] = new("failed"),
    ["LastUpdate"] = new(rawTimestamp), ["PublishedDateUtc"] = new(rawTimestamp)
};
var timestampPost = DbCRUD<SocialPostDb>.FromStorageAttributes(timestampAttributes);
var roundTrip = DbCRUD<SocialPostDb>.ToStorageAttributes(timestampPost);
Check(roundTrip["LastUpdate"].S == rawTimestamp && roundTrip["PublishedDateUtc"].S == rawTimestamp,
    "Dynamo timestamp reads must preserve exact precision for conditional publishing/cancellation updates");
}
Check(SocialPostStatus.IsValid("submission_unknown"), "Unknown submissions are valid post statuses");
foreach (bool failFirstRead in new[] { false, true })
{
    var deferredStore = MemoryStore.For("threads");
    int reads = 0, writes = 0;
    using var deferredHttp = new HttpClient(new ProviderHttpHandler(request =>
    {
        if (request.Method == HttpMethod.Get)
        {
            reads++;
            if (reads == 1 && failFirstRead) throw new TaskCanceledException();
            return Task.FromResult(Json(reads == 1 ? "{'status':'IN_PROGRESS'}" : "{'status':'FINISHED'}"));
        }
        writes++;
        return Task.FromResult(Json("{'id':'777'}"));
    }));
    var deferredService = new MetaSocialPublishingService(new(deferredHttp), deferredStore);
    var deferredPost = Post("threads");
    await deferredService.PublishAsync(deferredPost, null!);
    Check(deferredPost.Status == "publishing" && writes == 1, "Unready containers and transient status failures stay pending");
    await Task.WhenAll(deferredService.RefreshStatusAsync(deferredPost, null!), deferredService.RefreshStatusAsync(deferredPost, null!));
    Check(deferredPost.Status == "published" && writes == 2, "Concurrent later polls publish a pending Threads container only once");
}
foreach (string platform in new[] { "instagram", "threads" })
{
    var recoveryPost = Post(platform);
    recoveryPost.Status = "failed";
    Check(SocialPostService.CanRestoreDraft(recoveryPost, ""), "Legacy save requests without status can recover failed Meta posts");
    recoveryPost.Status = "submission_unknown";
    Check(SocialPostService.CanCancel(recoveryPost, new[] { new SocialPostDeliveryDb { Platform = platform, Status = "submission_unknown" } }),
        "Both Meta container platforms allow local cancellation of uncertain submissions");
}
var unknownPost = Post("threads"); unknownPost.Status = "submission_unknown";
Check(SocialPostService.CanCancel(unknownPost, new[] { new SocialPostDeliveryDb { Platform = "threads", Status = "submission_unknown" } }),
    "Uncertain Threads submissions can be cancelled locally");
foreach (string state in new[] { "initializing", "processing", "publishing", "published" })
    Check(!SocialPostService.CanCancel(unknownPost, new[] { new SocialPostDeliveryDb { Platform = "threads", Status = state } }),
        "Cancellation must not race an active or confirmed publication");
foreach (string state in new[] { "failed", "cancelled", "published", "publishing", "submission_unknown" })
{
    var recoveryPost = Post("threads"); recoveryPost.Status = state;
    Check(SocialPostService.CanRestoreDraft(recoveryPost, "draft") == (state is "failed" or "cancelled"),
        "Draft recovery is limited to failed and cancelled attempts");
}
var summary = SocialPostService.SummarizeDeliveries(new[] { "initializing", "processing", "publishing", "submission_unknown", "failed", "published", "cancelled" }
    .Select(s => new SocialPostDeliveryDb { Status = s }).ToList());
Check(summary.Publishing == 3 && summary.SubmissionUnknown == 1 && summary.Failed == 1 && summary.Published == 1 && summary.Cancelled == 1 && summary.TotalDeliveries == 7,
    "Analytics accounts for processing and uncertain deliveries without labelling them failed");

// Restoring a rejected attempt keeps the post and starts a distinct delivery attempt.
var restored = Post("instagram");
restored.Status = "failed";
var createdAt = restored.CreateDate;
var originalLink = restored.DestinationUrl;
var failedDelivery = new SocialPostDeliveryDb {
    DeliveryID = "instagram-post", SocialPostID = "post", OrganizationID = "org",
    Platform = "instagram", Status = "failed", ErrorCode = "36001"
};
SocialPostService.RestoreDraft(restored, new[] { failedDelivery });
Check(restored.SocialPostID == "post" && restored.CreateDate == createdAt && restored.DestinationUrl == originalLink,
    "Restore preserves the original post identity, creation date and tracking link");
Check(restored.Status == "draft" && restored.PublishingAttempt == 1 && failedDelivery.Status == "failed",
    "Restore retains failed delivery history and starts a new attempt");
var retryStore = MemoryStore.For("instagram");
retryStore.Deliveries[failedDelivery.DeliveryID] = failedDelivery;
int retryWrites = 0;
using var retryHttp = new HttpClient(new ProviderHttpHandler(request => {
    if (request.Method == HttpMethod.Get) return Task.FromResult(Json("{'status_code':'FINISHED'}"));
    Interlocked.Increment(ref retryWrites);
    return Task.FromResult(Json("{'id':'12345'}"));
}));
var retryService = new MetaSocialPublishingService(new(retryHttp), retryStore);
await retryService.RefreshStatusAsync(restored, null!);
Check(restored.Status == "draft", "Reading a restored draft does not reapply the old failed status");
await retryService.PublishAsync(restored, null!);
await retryService.PublishAsync(restored, null!);
Check(restored.Status == "published" && retryWrites == 2 && retryStore.Deliveries.Count == 2,
    "Restored post republishes once, while the original rejected delivery remains intact");
Check(retryStore.Deliveries["instagram-post-1"].SocialPostID == "post", "Retry still belongs to the same post");
foreach (string state in new[] { "submission_unknown", "processing", "publishing", "published", "cancelled" }) {
    var unsafePost = Post("instagram"); unsafePost.Status = "failed";
    await Reject(() => { SocialPostService.RestoreDraft(unsafePost, new[] { new SocialPostDeliveryDb { Status = state } }); return Task.CompletedTask; },
        "Restore must reject unresolved or successful deliveries");
}
foreach (string url in new[] { "https://www.instagram.com/", "https://instagram.com/p/example/", "https://yopmail.com/en/wm",
    "https://example.org/photo.png", "http://example.org/photo.jpg", "https://localhost/photo.jpg", "not a URL" }) {
    await Reject(() => { MetaSocialPublishingService.ValidateInstagramImageUrl(url); return Task.CompletedTask; }, "Invalid Instagram image URL must fail validation");
}
foreach (string url in new[] { "https://example.org/photo.JPG?signature=abc", "https://example.org/media/123?signature=abc" }) {
    MetaSocialPublishingService.ValidateInstagramImageUrl(url); checks++;
}
foreach (string code in new[] { "36001", "9004", "190" }) {
    var errorStore = MemoryStore.For("instagram");
    using var errorHttp = new HttpClient(new ProviderHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) {
        Content = new StringContent("{\"error\":{\"code\":" + code + ",\"message\":\"do-not-expose-token\"}}")
    })));
    var errorPost = Post("instagram");
    await new MetaSocialPublishingService(new(errorHttp), errorStore).PublishAsync(errorPost, null!);
    var error = errorStore.Deliveries["instagram-post"];
    Check(error.Status == "failed" && error.ErrorCode == code && !error.ErrorMessage.Contains("do-not-expose-token"), "Provider failure is retained safely");
    Check(error.ErrorMessage.Contains(code == "190" ? "expired or invalid" : "JPEG"), "Provider error gives a specific correction");
}
var expiredStore = MemoryStore.For("instagram");
expiredStore.Tokens["instagram"].TokenExpiresDateUtc = DateTime.UtcNow.AddMinutes(-1);
using var noSendHttp = new HttpClient(new ProviderHttpHandler(_ => throw new Exception("Expired token must not reach Meta")));
try {
    await new MetaSocialPublishingService(new(noSendHttp), expiredStore).PublishAsync(Post("instagram"), null!);
    throw new Exception("Expired token was accepted");
} catch (ResponseStatusException exception) {
    Check(exception.Message.Contains("Reconnect") && exception.Message.Contains("expired"), "Expired-token preflight explains reconnection");
}

Console.WriteLine($"Passed {checks} Meta publishing regression checks.");
