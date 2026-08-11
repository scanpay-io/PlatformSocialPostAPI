using Newtonsoft.Json;
using ScanPay.Utility.Model;
using System;
using System.Collections.Generic;

namespace ScanPay.SocialPostService
{
    public class SocialConnectionFrontend
    {
        [JsonProperty("social_connection_id")]
        public string SocialConnectionID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("organization_id")]
        public string OrganizationID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("platform")]
        public string Platform { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("external_account_id")]
        public string ExternalAccountID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("display_name")]
        public string DisplayName { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("token_expires_date_utc")]
        public DateTime? TokenExpiresDateUtc { get; set; }

        [JsonProperty("granted_scopes")]
        public string GrantedScopes { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("status")]
        public string Status { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("create_date")]
        public DateTime CreateDate { get; set; }

        [JsonProperty("last_update")]
        public DateTime LastUpdate { get; set; }

        public static SocialConnectionFrontend From(
            SocialConnectionDb connection)
        {
            return new SocialConnectionFrontend
            {
                SocialConnectionID =
                    connection.SocialConnectionID,

                OrganizationID =
                    connection.OrganizationID,

                Platform =
                    connection.Platform,

                ExternalAccountID =
                    connection.ExternalAccountID,

                DisplayName =
                    connection.DisplayName,

                TokenExpiresDateUtc =
                    connection.TokenExpiresDateUtc,

                GrantedScopes =
                    connection.GrantedScopes,

                Status =
                    connection.Status,

                CreateDate =
                    connection.CreateDate,

                LastUpdate =
                    connection.LastUpdate
            };
        }
    }

    public class SocialConnectionResponse :
        LambdaResponse
    {
        [JsonProperty("social_connection")]
        public SocialConnectionFrontend? SocialConnection { get; set; }
    }

    public class SocialConnectionsResponse :
        LambdaResponse
    {
        [JsonProperty("social_connections")]
        public List<SocialConnectionFrontend> SocialConnections { get; set; } =
            new();
    }

    public class SocialConnectionAuthorizeResponse :
        LambdaResponse
    {
        [JsonProperty("authorization_url")]
        public string AuthorizationUrl { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("oauth_message")]
        public string OAuthMessage { get; set; } =
            DefaultValue.EMPTY_STRING;
    }

    public class SocialPostResponse :
        LambdaResponse
    {
        [JsonProperty("social_post")]
        public SocialPostDb? SocialPost { get; set; }
    }

    public class SocialPostsResponse :
        LambdaResponse
    {
        [JsonProperty("social_posts")]
        public List<SocialPostDb> SocialPosts { get; set; } =
            new();
    }

    public class SocialPostStatusResponse :
        LambdaResponse
    {
        [JsonProperty("social_post")]
        public SocialPostDb? SocialPost { get; set; }

        [JsonProperty("deliveries")]
        public List<SocialPostDeliveryDb> Deliveries { get; set; } =
            new();
    }

    public class SocialPostAnalyticsResponse :
        LambdaResponse
    {
        [JsonProperty("social_post")]
        public SocialPostDb? SocialPost { get; set; }

        [JsonProperty("summary")]
        public SocialPostAnalyticsSummary Summary { get; set; } =
            new();

        [JsonProperty("analytics")]
        public List<object> Analytics { get; set; } =
            new();
    }
}
