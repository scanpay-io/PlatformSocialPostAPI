using Amazon.Lambda.Core;
using Newtonsoft.Json.Linq;
using ScanPay.DataModel.Model;
using ScanPay.SocialPostService;
using ScanPay.Utility.Model;
using System.Collections.Generic;
using System.Threading.Tasks;

[assembly: LambdaSerializer(
    typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace ScanPay.Lambda.GetSocialPostAnalytics
{
    public class SocialAnalyticsRequest : SocialPostRequest
    {
        [Newtonsoft.Json.JsonProperty("campaign_id")]
        public string CampaignID { get; set; } = "";
    }

    public class Function :
        BaseLambdaClient
    {
        private const string OperationName =
            "GetSocialPostAnalytics::FunctionHandler";

        public async Task<object?> FunctionHandler(
            JObject request,
            ILambdaContext context)
        {
            if (string.IsNullOrWhiteSpace(SocialAuthorization.Actor(request)))
                return new { statusCode = 401, body = "Authentication is required." };
            return await ProxyExecApiAsync<SocialAnalyticsRequest, object>(
                context: context,
                operation: OperationName,
                request: request,
                action: async normalizedRequest =>
                {
                    normalizedRequest = await SocialAuthorization.HydrateAsync(request, normalizedRequest, context);

                    string organizationID =
                        SocialRequestResolver.RequireOrganizationID(
                            normalizedRequest);

                    var query = request["queryStringParameters"] as JObject;
                    var contract = (string?)query?["contract"];
                    if (!string.IsNullOrEmpty(contract) && contract != "cohort_v1")
                        throw ResponseStatusException.ResponseStatusFactory.BadRequest("Unknown analytics contract.");
                    if (contract == "cohort_v1")
                    {
                        SocialInsightsRequest insights;
                        try { insights = query!.ToObject<SocialInsightsRequest>(Newtonsoft.Json.JsonSerializer.Create(
                            new Newtonsoft.Json.JsonSerializerSettings { DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc }))!; }
                        catch (Newtonsoft.Json.JsonException) { throw ResponseStatusException.ResponseStatusFactory.BadRequest("Invalid analytics query parameters."); }
                        insights.ApplyPostFallback(normalizedRequest.SocialPostID);
                        using var client = DbCRUD<SocialPostDb>.DBClient(context);
                        return await new SocialInsightsStore(client).ReadAsync(organizationID, SocialTracking.Scope(context, organizationID), insights, System.DateTime.UtcNow);
                    }
                    if (string.IsNullOrWhiteSpace(normalizedRequest.SocialPostID))
                        return await new SocialEngagementService().ReadCampaignAsync(organizationID, normalizedRequest.CampaignID, context);
                    return await SocialPostOperation.GetPostAnalyticsAsync(
                        organizationID,
                        normalizedRequest.SocialPostID,
                        context);
                },
                correlationID: GetCorrelationID(request, context),
                serviceName: "SocialPostAPI",
                dataFactory: () =>
                    new Dictionary<string, string>
                    {
                        ["component"] = "GetSocialPostAnalytics",
                        ["lambda_request_id"] = context?.AwsRequestId ?? string.Empty,
                        ["operation"] = OperationName
                    });
        }
    }
}
