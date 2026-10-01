using Amazon.Lambda.Core;
using Newtonsoft.Json.Linq;
using ScanPay.DataModel.Model;
using ScanPay.SocialPostService;
using ScanPay.Utility.Model;
using System.Threading.Tasks;
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]
namespace ScanPay.Lambda.RecordSocialPostClick
{
    public class Function : BaseLambdaClient
    {
        public async Task<object?> FunctionHandler(JObject request, ILambdaContext context)
        {
            return await ProxyExecApiAsync<SocialClickRequest, object>(
                context: context, operation: "RecordSocialPostClick", request: request,
                action: async normalized =>
                {
                    await new SocialEngagementService().RecordClickAsync(normalized, context);
                    return new LambdaResponse { Status = "success", Code = 200 };
                }, correlationID: GetCorrelationID(request, context), serviceName: "SocialPostAPI");
        }
    }
}
