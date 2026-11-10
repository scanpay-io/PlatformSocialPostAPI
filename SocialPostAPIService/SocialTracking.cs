using Amazon.Lambda.Core;
using Newtonsoft.Json.Linq;
using ScanPay.DataModel.Model;
using ScanPay.Utility.Model;
using ScanPay.Utility.Model.AWS;
using System;
using System.Linq;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService;

public static class SocialTracking
{
    public static string Scope(ILambdaContext context, string organization) =>
        AWSBaseClass.GetAliasApp(context) + ":" + AWSBaseClass.GetAlias(context) + ":" + organization;

    public static bool Owns(SocialPostDb post, string organization, string scope) =>
        post.OrganizationID == organization && post.AccountScopeKey == scope &&
        !string.IsNullOrWhiteSpace(post.AppName) && !string.IsNullOrWhiteSpace(post.Runway) &&
        post.AppName + ":" + post.Runway + ":" + organization == scope;

    public static bool Owns(SocialPostDeliveryDb delivery, SocialPostDb post) =>
        delivery.SocialPostID == post.SocialPostID && delivery.OrganizationID == post.OrganizationID &&
        delivery.AccountScopeKey == post.AccountScopeKey && delivery.AppName == post.AppName && delivery.Runway == post.Runway;

    public static void Ensure(SocialPostDb post, ILambdaContext context)
    {
        if (!Owns(post, post.OrganizationID, Scope(context, post.OrganizationID)))
            throw ResponseStatusFactory.NotFound("Social post was not found in this environment.");
    }

    public static void Stamp(SocialPostDb post, ILambdaContext context)
    {
        post.AppName = AWSBaseClass.GetAliasApp(context);
        post.Runway = AWSBaseClass.GetAlias(context);
        post.AccountScopeKey = Scope(context, post.OrganizationID);
    }

    public static void PrepareDelivery(SocialPostDeliveryDb delivery, SocialPostDb post, bool includeLink = true)
    {
        delivery.AppName = post.AppName;
        delivery.Runway = post.Runway;
        delivery.AccountScopeKey = post.AccountScopeKey;
        delivery.TrackingUrl = includeLink ? Destination(post.DestinationUrl, post.SocialPostID, delivery.DeliveryID) : "";
    }

    // A direct first-party destination needs no redirect service. Never retain competing
    // channel IDs or visitor/session IDs from a copied checkout URL in a public post.
    public static string Destination(string destination, string postID, string deliveryID)
    {
        if (string.IsNullOrWhiteSpace(destination)) return "";
        if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            throw ResponseStatusFactory.BadRequest("Tracked destinations require an HTTPS URL without credentials.");
        var builder = new UriBuilder(uri);
        var parts = builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => { var key = Uri.UnescapeDataString(p.Split('=')[0]);
                return !key.StartsWith("social", StringComparison.OrdinalIgnoreCase) &&
                    !new[] { "smsid", "smsat", "smsclickid", "smsvisitorid" }.Contains(key, StringComparer.OrdinalIgnoreCase); }).ToList();
        parts.Add("socialpostid=" + Uri.EscapeDataString(postID));
        if (!string.IsNullOrEmpty(deliveryID)) parts.Add("socialdeliveryid=" + Uri.EscapeDataString(deliveryID));
        builder.Query = string.Join("&", parts);
        return builder.Uri.ToString();
    }

    public static SocialPostDb WithDestination(SocialPostDb post, string destination)
    {
        var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<SocialPostDb>(Newtonsoft.Json.JsonConvert.SerializeObject(post))!;
        copy.DestinationUrl = destination;
        return copy;
    }
}

public static class SocialAuthorization
{
    // Only gateway-injected claims are trusted. Body/header account identifiers are not identity.
    public static string Actor(JObject request) =>
        (string?)request["requestContext"]?["authorizer"]?["claims"]?["sub"] ?? "";

    public static async Task<T> HydrateAsync<T>(JObject request, T body, ILambdaContext context)
        where T : SocialOrganizationRequest, new()
    {
        body = SocialRequestResolver.Hydrate(request, body);
        var actor = Actor(request);
        if (string.IsNullOrWhiteSpace(actor)) throw ResponseStatusFactory.Forbidden("Authentication is required.");
        var account = await DbCRUD<AccountDb>.ReadAsync(actor, context);
        if (account == null) throw ResponseStatusFactory.Forbidden("Account membership was not found.");
        var organization = actor;
        if (AccountType.StringToType(account.AccountType) == AccountType.Type.Staff)
        {
            var staff = await DbCRUD<OrganizationStaffDb>.ReadAsync(actor, context);
            organization = staff?.OrganizationID ?? "";
        }
        ApplyOrganization(body, organization);
        return body;
    }

    public static void ApplyOrganization(SocialOrganizationRequest body, string organization)
    {
        if (string.IsNullOrWhiteSpace(organization)) throw ResponseStatusFactory.Forbidden("Organization membership is required.");
        // As in SMS, a caller-supplied account cannot select another tenant.
        body.OrganizationID = body.AccountID = body.OwnerAccountID = organization;
    }
}
