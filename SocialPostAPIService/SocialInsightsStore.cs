using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2.DocumentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScanPay.DataModel.Model;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService;

// A store instance is a single request and shares one budget across all parallel queries.
public sealed class SocialInsightsStore(IAmazonDynamoDB client, byte[]? cursorKey = null)
{
    public const int MaxDeliveries = 2000;
    public const int MaxEvaluatedRecords = 50000;
    private int evaluated;
    private double? capacity;
    private readonly object sync = new();

    public static QueryRequest Query(string table, string key, string value) => new()
    {
        TableName = table, IndexName = key + "-index", KeyConditionExpression = "#key = :value",
        ExpressionAttributeNames = new() { ["#key"] = key },
        ExpressionAttributeValues = new() { [":value"] = new AttributeValue(value) }
    };

    public async Task<List<T>> QueryAsync<T>(QueryRequest query, int maximum = MaxEvaluatedRecords)
    {
        var rows = new List<T>();
        do
        {
            if (Volatile.Read(ref evaluated) >= MaxEvaluatedRecords) throw TooLarge();
            query.Limit = Math.Min(250, maximum + 1 - rows.Count);
            query.ReturnConsumedCapacity = ReturnConsumedCapacity.TOTAL;
            QueryResponse page;
            try { page = await client.QueryAsync(query); }
            catch (AmazonDynamoDBException ex) when (ex is ResourceNotFoundException || ex.ErrorCode is "ValidationException" or "ResourceNotFoundException")
            { throw ResponseStatusFactory.Failed("Social analytics storage is not ready. Verify indexes and scoped migration."); }
            if (Interlocked.Add(ref evaluated, page.ScannedCount ?? 0) > MaxEvaluatedRecords) throw TooLarge();
            Capture(page.ConsumedCapacity);
            rows.AddRange((page.Items ?? new()).Select(Convert<T>));
            if (rows.Count > maximum) throw TooLarge();
            query.ExclusiveStartKey = page.LastEvaluatedKey;
        } while (query.ExclusiveStartKey?.Count > 0);
        return rows;
    }

    private async Task<SocialPostDb?> PostAsync(string id)
    {
        if (Interlocked.Increment(ref evaluated) > MaxEvaluatedRecords) throw TooLarge();
        var row = await client.GetItemAsync(new GetItemRequest { TableName = "SocialPost", ConsistentRead = true,
            Key = new() { ["SocialPostID"] = new AttributeValue(id) }, ReturnConsumedCapacity = ReturnConsumedCapacity.TOTAL });
        Capture(row.ConsumedCapacity);
        return row.Item?.Count > 0 ? Convert<SocialPostDb>(row.Item) : null;
    }

    public static QueryRequest CohortQuery(string scope, SocialInsightsRequest request) => new()
    {
        TableName = "SocialPostDelivery", IndexName = "AccountPublishedAt-index",
        KeyConditionExpression = "#scope = :scope AND #published BETWEEN :from AND :to",
        ExpressionAttributeNames = new() { ["#scope"] = "AccountScopeKey", ["#published"] = "PublishedAtKey" },
        ExpressionAttributeValues = new() { [":scope"] = new AttributeValue(scope),
            [":from"] = new AttributeValue(request.FromUtc!.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)),
            [":to"] = new AttributeValue(request.ToUtc!.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)) }
    };

    public async Task<object> ReadAsync(string organization, string scope, SocialInsightsRequest request, DateTime now)
    {
        SocialInsightsCursor? cursor = null;
        if (!string.IsNullOrEmpty(request.NextToken))
        {
            cursor = SocialInsightsCursor.Decode(request.NextToken, scope, request, cursorKey);
            request.OutcomeBeforeUtc = cursor.Cutoff;
        }
        request.Validate(now);
        var cutoff = request.OutcomeBeforeUtc!.Value;
        var posts = new Dictionary<string, SocialPostDb>();
        List<SocialPostDeliveryDb> deliveries;
        if (!string.IsNullOrEmpty(request.PostID))
        {
            var post = await PostAsync(request.PostID);
            if (post == null || !SocialTracking.Owns(post, organization, scope)) throw ResponseStatusFactory.NotFound("Social post was not found.");
            posts.Add(post.SocialPostID, post);
            deliveries = await QueryAsync<SocialPostDeliveryDb>(Query("SocialPostDelivery", "SocialPostID", post.SocialPostID), MaxDeliveries);
        }
        else
        {
            deliveries = await QueryAsync<SocialPostDeliveryDb>(CohortQuery(scope, request), MaxDeliveries);
            // BETWEEN is inclusive; the API upper bound is exclusive.
            deliveries = deliveries.Where(d => d.PublishedDateUtc >= request.FromUtc && d.PublishedDateUtc < request.ToUtc).ToList();
            using var gate = new SemaphoreSlim(8);
            await Task.WhenAll(deliveries.Select(d => d.SocialPostID).Distinct().Select(async id =>
            {
                await gate.WaitAsync();
                try { var post = await PostAsync(id); if (post != null) lock (posts) posts[id] = post; }
                finally { gate.Release(); }
            }));
        }
        deliveries = deliveries.Where(d => posts.TryGetValue(d.SocialPostID, out var post) &&
            SocialTracking.Owns(post, organization, scope) && SocialTracking.Owns(d, post) &&
            d.PublishedDateUtc.HasValue && d.PublishedDateUtc < cutoff &&
            (string.IsNullOrEmpty(request.CampaignID) || post.ResourceID == request.CampaignID))
            .OrderBy(d => d.PublishedDateUtc).ThenBy(d => d.DeliveryID, StringComparer.Ordinal).ToList();
        if (cursor != null) deliveries = deliveries.Where(d => d.PublishedDateUtc > cursor.PublishedAt ||
            d.PublishedDateUtc == cursor.PublishedAt && string.CompareOrdinal(d.DeliveryID, cursor.DeliveryID) > 0).ToList();
        bool more = request.View == "deliveries" && deliveries.Count > request.Limit;
        if (request.View == "deliveries") deliveries = deliveries.Take(request.Limit).ToList();
        var clicks = new List<SocialPostClickDb>();
        var transactions = new List<TransactionAmountDb>();
        using (var gate = new SemaphoreSlim(8))
        {
            await Task.WhenAll(deliveries.Select(d => d.SocialPostID).Distinct().Select(async id =>
            {
                await gate.WaitAsync();
                try
                {
                    var visits = await QueryAsync<SocialPostClickDb>(Query("SocialPostClick", "SocialPostID", id));
                    // Detail pages expose no payment attribution at delivery level.
                    var paid = request.View == "summary" ? await QueryAsync<TransactionAmountDb>(Query("TransactionAmount", "SocialPostID", id)) : new();
                    lock (sync) { clicks.AddRange(visits); transactions.AddRange(paid); }
                }
                finally { gate.Release(); }
            }));
        }
        var report = JObject.FromObject(SocialInsights.Report(organization, scope, posts.Values, deliveries, clicks, transactions, cutoff,
            request.View == "deliveries" || !string.IsNullOrEmpty(request.PostID)));
        string next = "";
        if (more)
        {
            var last = deliveries.Last();
            next = new SocialInsightsCursor { Cutoff = cutoff, PublishedAt = last.PublishedDateUtc!.Value, DeliveryID = last.DeliveryID }.Encode(scope, request, cursorKey);
        }
        return new { status = "success", code = 200, contract = "cohort_v1", as_of_utc = now, outcome_before_utc = cutoff,
            from_utc = request.FromUtc, to_utc = request.ToUtc, consistency = "eventual_current_transaction_state",
            revenue_basis = "post_attributed_successful_transactions_including_repeat_gifts",
            evaluated_records = evaluated, consumed_read_capacity_units = capacity, next_token = next,
            analytics = request.View == "summary" ? report : null, deliveries = request.View == "deliveries" ? report["deliveries"] : null };
    }

    private void Capture(ConsumedCapacity? value) { if (value?.CapacityUnits is double units) lock (sync) capacity = (capacity ?? 0) + units; }
    private static T Convert<T>(Dictionary<string, AttributeValue> row) => JsonConvert.DeserializeObject<T>(Document.FromAttributeMap(row).ToJson())!;
    private static Exception TooLarge() => ResponseStatusFactory.BadRequest("Social report exceeds its record budget. Narrow the publication range or select a post.");
}

public sealed class SocialInsightsCursor
{
    public DateTime Cutoff { get; set; }
    public DateTime PublishedAt { get; set; }
    public string DeliveryID { get; set; } = "";
    public string Binding { get; set; } = "";
    private static string Bind(string scope, SocialInsightsRequest r) => JsonConvert.SerializeObject(new
        { scope, r.PostID, r.CampaignID, r.FromUtc, r.ToUtc, r.View, r.Limit });
    private static byte[] Key(byte[]? supplied)
    {
        if (supplied?.Length >= 32) return supplied;
        try { var key = System.Convert.FromBase64String(Environment.GetEnvironmentVariable("SOCIAL_ANALYTICS_CURSOR_KEY") ?? ""); if (key.Length >= 32) return key; }
        catch (FormatException) { }
        throw ResponseStatusFactory.Failed("Configure a separate SOCIAL_ANALYTICS_CURSOR_KEY of at least 32 random bytes (base64) for each environment.");
    }
    public string Encode(string scope, SocialInsightsRequest request, byte[]? key = null)
    {
        Binding = Bind(scope, request);
        var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this));
        return System.Convert.ToBase64String(body) + "." + System.Convert.ToBase64String(HMACSHA256.HashData(Key(key), body));
    }
    public static SocialInsightsCursor Decode(string token, string scope, SocialInsightsRequest request, byte[]? key = null)
    {
        var signingKey = Key(key);
        try
        {
            if (token.Length > 4096) throw new FormatException();
            var parts = token.Split('.'); if (parts.Length != 2) throw new FormatException();
            var body = System.Convert.FromBase64String(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(signingKey, body), System.Convert.FromBase64String(parts[1]))) throw new FormatException();
            var cursor = JsonConvert.DeserializeObject<SocialInsightsCursor>(Encoding.UTF8.GetString(body))!;
            if (cursor.Binding != Bind(scope, request) || request.OutcomeBeforeUtc.HasValue && request.OutcomeBeforeUtc != cursor.Cutoff) throw new FormatException();
            return cursor;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or NullReferenceException)
        { throw ResponseStatusFactory.BadRequest("Invalid next_token or changed report filters."); }
    }
}
