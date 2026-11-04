# LinkedIn organization Page publishing

The existing Connect LinkedIn action uses the authorize-connection endpoint with `platform: "linkedin"`. The callback requires the authorization code and signed state, bound to the authenticated GiveAnywhere organization. Redirects are backend configured. Connection responses exclude tokens; credentials remain in the backend SocialConnectionToken store. Ensure that table has encryption at rest and access limited to the service IAM role.

Configure `cloud_services.social_service_providers.linkedin` with `supported`, `oauth_enabled`, `posting_enabled`, `client_id`, `client_secret`, and the exact HTTPS `redirect_uri` registered with LinkedIn. Obtain LinkedIn Community Management access for `rw_organization_admin` and `w_organization_social`. Both permissions are requested even when a legacy scope override is configured. Reconnect existing accounts to grant them. Set `SOCIAL_LINKEDIN_API_VERSION` to a supported YYYYMM version (default 202609). Credential and redirect environment fallbacks follow the existing OAuth service; use provider configuration for refresh credentials too.

Read the connection to obtain `linkedin_pages`; the administrator must select a manageable organization Page. Create a post using the existing create endpoint:

```json
{
  "organization_id": "organization-id",
  "platforms": ["linkedin"],
  "content": "Support our community",
  "destination_url": "https://example.org/campaign",
  "text_social": {
    "connection_id": "connection-id",
    "linkedin_page_urn": "urn:li:organization:123",
    "consent": true
  }
}
```

Publish the returned post ID through the existing publish endpoint. Consent records approval of this post. Validation runs at creation and again before publishing, including the final tracked destination URL within the 3,000-character limit. This integration supports text and links only: media options are rejected, not uploaded. Do not send attachments. The Page role is rechecked at publication, and the connection and token must belong to the organization. Expired tokens refresh when a refresh token is available; otherwise reconnect.

A successful delivery retains LinkedIn's `x-restli-id` as `ExternalPostID` and its feed URL as `ExternalPostUrl`. A missing ID or ambiguous submission never reports published and must be checked remotely before retrying. Existing conditional delivery creation guards against duplicate submissions.

Validation: `dotnet run --project tests/LinkedInRegression -c Release` plus the existing OAuth and publishing regression suites. These are local tests with fake HTTP; deployment and a live approved Page post are still required to verify app access. This repository supplies the API, not the Web2.0 Connect button.

References: [LinkedIn Posts API](https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/posts-api), [LinkedIn authorization](https://learn.microsoft.com/en-us/linkedin/shared/authentication/authorization-code-flow).

Local verification (2026-10-29): a clean Release solution build passed with zero errors and 18 existing nullable warnings in PlatformLibrary. All 42 checked project references resolve. The earlier apparent missing sibling paths were caused by restricted build access, not incorrect project references; no path changes are needed. Run builds with read/write access to the sibling PlatformLibrary (including its build output directories) and NuGet restore access.

Verified without disabling dependency builds or restore:

```powershell
dotnet clean SocialPostAPIService.sln -c Release -m:1
dotnet build SocialPostAPIService.sln -c Release -m:1
dotnet clean tests/LinkedInRegression/LinkedInRegression.csproj -c Release -m:1
dotnet run --project tests/LinkedInRegression -c Release
```

All 24 LinkedIn checks passed after the clean rebuild. The earlier OAuth run passed 116 checks and Meta publishing run passed 119 checks.


## Callback mapping fix — 2026-11-02

The development API Gateway callback mapping omitted `state`. The reported authorization started at 08:37:02 UTC and failed at 08:37:06 UTC, before its 08:47:02 UTC expiration. The Lambda received no signed state and correctly rejected the callback. Forward `state` unchanged alongside code, platform, organization ID, and redirect URI; the template is in `social-connection-callback.vtl`.

Updated REST API `6op50ujgu6`, resource `5s0egm`, POST integration and deployed stage `dev` as deployment `cb9g20` (previous deployment `e1xplq`). Verified with synthetic credentials that state is forwarded and invalid state is still rejected, then verified the deployed stage export. No real authorization code was replayed. A fresh end-to-end LinkedIn connection is required to verify token exchange and Page discovery.
