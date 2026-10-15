# Social publishing deployment — 15 October 2026

## What to build and deploy

Deploy the SocialPost API first, then Web2.0. Do not deploy PlatformLibrary independently: its assemblies are included in each Lambda package.

From `E:\GiveAnywhere\Platform\PlatformSocialPostAPI`, build and publish the complete 16-Lambda inventory in `lambda-runner.json` so every endpoint carries the same shared service version:

```powershell
.\build-all-lambdas.bat Release
.\publish-all-lambdas.bat 20261015-social-posting-001 Release default
.\deploy-all-lambdas.bat development 20261015-social-posting-001 default
```

Use a fresh release ID if that ID has already been used. The existing release workflow requires `GANY_RELEASE_BUCKET` and `GANY_RELEASE_ACCOUNT` (or populated `release-settings.json`); the checked-in settings are blank. See `../RELEASES-README.md`. Deploy takes an alias and release ID, not a build configuration. Confirm that `development` is the alias used by the intended API Gateway stage. No AWS resources were modified as part of this fix.

The directly changed endpoint behavior is in these functions:

- `scanpay_authorize_social_connection` (Facebook Page-list permission)
- `scanpay_create_social_post`, `scanpay_update_social_post` (media/settings validation, safe editing)
- `scanpay_get_social_connection` (Facebook Page discovery without returning tokens)
- `scanpay_publish_social_post` (Facebook/Instagram/Threads provider requests)
- `scanpay_get_social_post_status` (container status and final publication)
- `scanpay_get_social_post`, `scanpay_get_social_post_analytics` (consistent delivery reads)
- `scanpay_schedule_social_post` (explicit unsupported response)
- `scanpay_cancel_social_post`, `scanpay_delete_social_post` (submitted-post protection)

Deploying all 16 also includes the existing local OAuth callback changes. Review other pre-existing local changes before publishing the release.

From `E:\GiveAnywhere\WebApps\Web2.0`:

```powershell
node tools/check-hosted-environments.cjs
node node_modules/@angular/cli/bin/ng.js build --configuration stage
```

Publish `E:\GiveAnywhere\WebApps\Web2.0\dist\NewWeb\browser` using the existing Web2.0 stage hosting pipeline. The composer now sends separate campaign and image URLs, selected accounts and Facebook Pages, and polls publication status. No hosting changes, new Lambda, queue, scheduler, DynamoDB table, or index are required. New `MetaSocial`, `TargetAccountID`, and `ContainerID` properties are ordinary item attributes.

## Provider configuration

For Facebook, Instagram and Threads, ensure the appropriate environment's `social_service_providers.<platform>` configuration has `supported`, `oauth_enabled`, and `posting_enabled` enabled **after** its credentials and approved access are ready. Existing X/LinkedIn publishing also requires its configured posting flag. Do not enable an unapproved production integration merely to clear the application check.

- Facebook: configure `pages_show_list pages_manage_posts pages_read_engagement`; explicit configured scopes override code defaults. Reconnect existing users to grant Page-list permission. The backend lists manageable Pages and publishes using the selected Page token, never a personal-profile token. When only one Page exists, the API can select it automatically; multiple Pages require `facebook_page_id`.
- Instagram: Instagram Login with `instagram_business_basic,instagram_business_content_publish`; use a professional account and a public JPEG image URL. Caption text alone cannot be published. Tokens that expire require reconnection.
- Threads: `threads_basic,threads_content_publish`; text and a single optional public image are supported. Tokens that expire require reconnection.
- X and LinkedIn retain their dedicated text composers; TikTok retains its dedicated video/inbox composer and approval requirements in `tiktok-setup.md`.

Refresh provider configuration through the normal environment-specific cache procedure after changing it. Do not copy app secrets into the browser or source files.

## API contract and processing

Example create-post body (organization ID may also be supplied through the existing organization route):

```json
{
  "organization_id": "organization-id",
  "content": "Support our campaign",
  "destination_url": "https://example.org/campaign",
  "platforms": ["facebook", "instagram", "threads"],
  "meta_social": [
    { "platform": "facebook", "connection_id": "facebook-connection", "facebook_page_id": "123456" },
    { "platform": "instagram", "connection_id": "instagram-connection", "image_url": "https://example.org/photo.jpg" },
    { "platform": "threads", "connection_id": "threads-connection" }
  ]
}
```

Call the existing publish endpoint for the resulting post ID. Facebook publishes immediately. Instagram/Threads save their container IDs and return `publishing`; call the existing status endpoint until completion. The Web2.0 composer polls ten times at two-second intervals, then directs the user to Status to continue. Closing the browser before a container is published can leave it pending until another status check. There is no background worker.

Only a confirmed provider post ID produces `published`. Conditional delivery creation and conditional container claims prevent repeat sends. Ambiguous failures remain `submission_unknown` and are not automatically retried; check the provider before creating another post. Submitted posts cannot be edited or locally cancelled as if they had never been sent.

Scheduling remains unavailable and now fails explicitly instead of silently leaving posts in an unprocessed queue. Recreate old queued posts after checking their destinations; they are never automatically replayed. Old Instagram drafts need a separate image URL. This change does not add Instagram video/carousel publishing or X/LinkedIn media attachments.

## Validation and stage acceptance

Local results: the Release solution build succeeded with zero errors; the OAuth suite passed 95 checks and the Meta publishing suite passed 71 checks. Backend and intake attribution checks passed. The focused frontend suite passed 13 tests, Angular template/TypeScript compilation succeeded, required UI structural checks passed, and the stage browser build completed with dependency/optimization warnings. No live posts or deployment were performed.

Run:

```powershell
dotnet build SocialPostAPIService.sln -c Release -m:1
dotnet run --project tests/OAuthRegression -c Release
dotnet run --project tests/PublishingRegression -c Release
dotnet run --project tests/SocialAttributionRegression -c Release
node tests/intake-attribution.cjs
```

The OAuth suite covers the existing providers; the publishing suite uses intercepted HTTP and an in-memory store to verify Meta request contracts, account/Page authorization, token ownership/expiry, container lifecycle, repeated requests, concurrent polls, unknown outcomes, and storage failure after remote acceptance. These tests do not establish live provider access or DynamoDB integration.

For Web2.0, the focused suite runs with:

```powershell
node node_modules/vitest/vitest.mjs run src/app/features/crm/crm-social-regression.spec.ts --config vitest.account-support.config.mjs
node node_modules/@angular/compiler-cli/bundles/src/bin/ngc.js -p tsconfig.app.json --noEmit
```

The standard Angular test build currently also compiles unrelated sponsorship-order tests that reference a missing `eventId` property; use the focused test command while those tests are repaired separately. Desktop/mobile browser visual validation and live social posting are still required after deployment.

Use approved test accounts and approved content to publish one Facebook Page post, Instagram JPEG, Threads post, X text post, LinkedIn Page post, and TikTok video/inbox upload. Confirm actual provider posts and IDs, not merely HTTP success or queued status. Repeated publish/status calls must not create duplicates. Check partial failure and revoked/expired-token behavior, then promote the same release only after stage acceptance.

Provider references: [Meta Facebook collection](https://www.postman.com/meta/facebook/documentation/r56bjfd/facebook-api), [Meta Instagram collection](https://www.postman.com/meta/instagram/documentation/6yqw8pt/instagram-api), [Meta Threads container status](https://www.postman.com/meta/threads/request/m47wqlq/check-container-s-publishing-status).
