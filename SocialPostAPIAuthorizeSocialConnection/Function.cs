using Amazon.Lambda.Core;
using Newtonsoft.Json.Linq;
using ScanPay.DataModel.Model;
using ScanPay.SocialPostService;
using ScanPay.Utility.Model;
using System.Collections.Generic;
using System.Threading.Tasks;

[assembly: LambdaSerializer(
    typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace ScanPay.Lambda.AuthorizeSocialConnection
{
    public class Function :
        BaseLambdaClient
    {
        private const string OperationName =
            "AuthorizeSocialConnection::FunctionHandler";

        public async Task<object?> FunctionHandler(
            JObject request,
            ILambdaContext context)
        {
            return await ProxyExecApiAsync<
                SocialConnectionAuthorizeRequest,
                SocialConnectionAuthorizeResponse>(
                    context: context,
                    operation: OperationName,
                    request: request,
                    action: async normalizedRequest =>
                    {
                        normalizedRequest =
                            SocialRequestResolver.Hydrate(
                                request,
                                normalizedRequest);

                        SocialRequestResolver.RequireOrganizationID(
                            normalizedRequest);

                        return await SocialPostOperation.AuthorizeConnectionAsync(
                            normalizedRequest,
                            context);
                    },
                    correlationID:
                        GetCorrelationID(
                            request,
                            context),
                    serviceName:
                        "SocialPostAPI",
                    dataFactory: () =>
                        new Dictionary<string, string>
                        {
                            ["component"] =
                                "AuthorizeSocialConnection",

                            ["lambda_request_id"] =
                                context?.AwsRequestId
                                ?? string.Empty,

                            ["operation"] =
                                OperationName
                        });
        }
    }
}