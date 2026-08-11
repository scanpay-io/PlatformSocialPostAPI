using Newtonsoft.Json;
using ScanPay.Utility.Model;
using System;
using System.Collections.Generic;

namespace ScanPay.SocialPostService
{
    public class SocialOrganizationRequest :
        LambdaRequest
    {
        [JsonProperty("organization_id")]
        public string OrganizationID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("account_id")]
        public string AccountID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("owner_account_id")]
        public string OwnerAccountID { get; set; } =
            DefaultValue.EMPTY_STRING;

        public string EffectiveOrganizationID =>
            FormatValue.NotEmptyValue(OrganizationID)
                ? OrganizationID
                : FormatValue.NotEmptyValue(OwnerAccountID)
                    ? OwnerAccountID
                    : AccountID;
    }

    public class SocialConnectionRequest :
        SocialOrganizationRequest
    {
        [JsonProperty("connection_id")]
        public string SocialConnectionID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("social_connection_id")]
        public string SocialConnectionIDAlias
        {
            get => SocialConnectionID;
            set => SocialConnectionID = value;
        }
    }

    public class SocialConnectionAuthorizeRequest :
        SocialOrganizationRequest
    {
        [JsonProperty("platform")]
        public string SocialPlatform { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("redirect_uri")]
        public string RedirectUri { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("callback_url")]
        public string CallbackUrl
        {
            get => RedirectUri;
            set => RedirectUri = value;
        }
    }

    public class SocialConnectionCallbackRequest :
        SocialConnectionAuthorizeRequest
    {
        [JsonProperty("code")]
        public string Code { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("state")]
        public string State { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("external_account_id")]
        public string ExternalAccountID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("display_name")]
        public string DisplayName { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("access_token")]
        public string AccessToken { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("token_secret_id")]
        public string TokenSecretID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("token_expires_date_utc")]
        public DateTime? TokenExpiresDateUtc { get; set; }

        [JsonProperty("granted_scopes")]
        public string GrantedScopes { get; set; } =
            DefaultValue.EMPTY_STRING;
    }

    public class SocialPostRequest :
        SocialOrganizationRequest
    {
        [JsonProperty("post_id")]
        public string SocialPostID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("social_post_id")]
        public string SocialPostIDAlias
        {
            get => SocialPostID;
            set => SocialPostID = value;
        }
    }

    public class CreateSocialPostRequest :
        SocialOrganizationRequest
    {
        [JsonProperty("resource_type")]
        public string ResourceType { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("resource_id")]
        public string ResourceID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("outreach_item_id")]
        public string OutreachItemID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("content")]
        public string Content { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("destination_url")]
        public string DestinationUrl { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("platforms")]
        public List<string> Platforms { get; set; } =
            new();
    }

    public class UpdateSocialPostRequest :
        CreateSocialPostRequest
    {
        [JsonProperty("post_id")]
        public string SocialPostID { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("social_post_id")]
        public string SocialPostIDAlias
        {
            get => SocialPostID;
            set => SocialPostID = value;
        }

        [JsonProperty("status")]
        public string Status { get; set; } =
            DefaultValue.EMPTY_STRING;

        [JsonProperty("scheduled_date_utc")]
        public DateTime? ScheduledDateUtc { get; set; }
    }

    public class ScheduleSocialPostRequest :
        SocialPostRequest
    {
        [JsonProperty("scheduled_date_utc")]
        public DateTime? ScheduledDateUtc { get; set; }
    }
}
