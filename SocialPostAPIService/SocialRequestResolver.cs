using Newtonsoft.Json.Linq;
using ScanPay.Utility.Model;
using System.Collections.Generic;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public static class SocialRequestResolver
    {
        public static T Hydrate<T>(
            JObject request,
            T normalizedRequest)
            where T : SocialOrganizationRequest, new()
        {
            return LambdaRequestHydrator.ApplyProxyFallbacks(
                request,
                normalizedRequest,
                new Dictionary<string, string[]>
                {
                    ["OrganizationID"] =
                        new[]
                        {
                            "organization-id",
                            "organization_id",
                            "account-id",
                            "account_id",
                            "owner-account-id",
                            "owner_account_id"
                        },

                    ["SocialConnectionID"] =
                        new[]
                        {
                            "connection-id",
                            "connection_id",
                            "social-connection-id",
                            "social_connection_id"
                        },

                    ["SocialPostID"] =
                        new[]
                        {
                            "post-id",
                            "post_id",
                            "social-post-id",
                            "social_post_id"
                        },

                    ["SocialPlatform"] =
                        new[]
                        {
                            "platform",
                            "social-platform",
                            "social_platform"
                        },

                    ["RedirectUri"] =
                        new[]
                        {
                            "redirect-uri",
                            "redirect_uri",
                            "callback-url",
                            "callback_url"
                        }
                });
        }

        public static string RequireOrganizationID(
            SocialOrganizationRequest request)
        {
            string organizationID =
                request?.EffectiveOrganizationID
                ?? DefaultValue.EMPTY_STRING;

            if (FormatValue.EmptyValue(organizationID))
            {
                throw ResponseStatusFactory.BadRequest(
                    "organization_id is required.");
            }

            return organizationID;
        }
    }
}
