# Intake2.0 migration and Hurtt Demo verification — 2026-11-10

## Frontend changes

Intake2.0 is the supported checkout repository. Added `src/app/core/social-checkout-attribution.ts`, initialized capture through GiveNowService and application navigation, and integrated attribution into the existing one-time payment payload.

- Captures post and delivery IDs, preserves handoff click/visitor IDs and the original timestamp.
- Retains click IDs across retries and records a different delivery as a new landing.
- Binds payment attribution to the checkout organization, target and transaction type where supplied by the link; the backend independently validates ownership and eligibility.
- Keeps the 24-hour window across reloads, isolates storage by API environment, and tolerates unavailable browser storage.
- Preserves SMS tracking and makes captured SMS/social sources mutually exclusive in payment payloads.
- Tracking failures do not prevent payment submission.

Removed the three changes introduced by this task in GoGiveAnywhere.Intake: the untracked attribution service, app initialization, and payment-service hooks. Verified the legacy repository is clean. Backups are in the SocialPost API's ignored `tmp/legacy-intake-tracking-backup` directory. The cross-app regression harness now uses Intake2.0, not legacy Intake.

Validation: all 142 Intake2.0 tests passed (20 files), including eight new social/payment cases. The DEV frontend build passed. The actual-source Web2.0/Intake2.0 handoff harness passed. No frontend deployment was performed by this task.

## Live DEV integration checks

Organization: Hurtt Demo (`1d3a11d5-ece9-4a24-ab3a-1b601fa15861`), verified as a `gany` DEV organization. Used uniquely identified temporary posts/deliveries representing publication; no social provider was called and no content was externally published.

The public DEV click route successfully persisted events with delivery, platform and environment scope. Retry requests did not inflate totals. Platform relabeling, unpublished delivery and wrong-environment requests were rejected.

Analytics was invoked through IAM with a trusted test event for Hurtt Demo; this is a deployed Lambda/DynamoDB integration test, not an authenticated browser/API Gateway end-to-end test. Post reporting returned three published deliveries (two tracked), two landing events, one browser visitor, two deliveries with landings, zero donations, and null engagement for the untracked delivery. Wrong-environment posts were excluded, caller-supplied organization did not override the trusted actor, and missing actor context was rejected.

## Two remaining DEV analytics failures

1. Date-range cohort requests return HTTP 400: `post_id must be a GUID and cannot be combined with cohort filters.` The handler was copying `DefaultValue.EMPTY_STRING` (a single space) from the legacy request into the cohort post filter. Fixed locally by normalizing the fallback through `SocialInsightsRequest.ApplyPostFallback`. Regression coverage exercises the legacy sentinel through cohort pagination, retains post-route fallback, and preserves an explicit query post ID.
2. Paginated delivery requests return HTTP 500 because DEV analytics version 18 does not contain `SOCIAL_ANALYTICS_CURSOR_KEY`. The valid 32-byte key remains staged on `$LATEST`. A fresh immutable analytics publication and DEV promotion are required; promoting the existing release manifest does not capture the key.

Local backend validation passed: social attribution regressions, all 38 cohort checks, and compilation of GetSocialPostAnalytics.csproj (zero errors; existing dependency warnings remain). The final AWS recheck still showed DEV version 18 without the key.

At the release inventory check, all 19 required `gany_dev` versions still matched the pre-infrastructure inventory. This task did not publish backend code or move aliases. Rebuild/publish `scanpay_get_social_post_analytics` with the local fallback fix and preserve its staged environment; promote only `gany_dev`. See `../BACKEND-DEPLOYMENT.md` for the remaining consumer inventory.

## Cleanup and limitations

All temporary fixture and click deletions used their unique identifiers and ownership conditions. Consistent reads verified zero remaining records for the final test run. Earlier interrupted runs also completed cleanup. No historical records were backfilled, no payments were created, and no PROD deployment was performed.

Authenticated browser-to-API tests, deployed frontend acceptance, provider publication/link generation, successful payment completion attribution, and live signed-cursor continuation/tampering tests remain unverified. Complete those after the fresh DEV analytics release and frontend deployment using an authenticated Hurtt Demo session and a noncharging payment fixture.

Evidence: `versions.json`, `cursor.json`, `hurtt-tests.json`, `hurtt-cohort-failure.json`, `hurtt-cleanup-verified.json`; prior interrupted attempts are retained separately.
