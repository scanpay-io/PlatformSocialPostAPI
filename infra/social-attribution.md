# Social attribution rollout

## QA investigation: 29 October 2026

AWS checks confirmed the analytics Lambda failed because `SocialPostClick` did not exist and `TransactionAmount` lacked `SocialPostID-index`. Both repairs were applied with user approval. The click-table stack update is complete, and the transaction index is ACTIVE with 8 read / 2 write capacity units. Existing table capacity was preserved. The original reported post's dev Analytics endpoint now returns successful delivery and engagement data.

Executed change set `social-analytics-20261029` on `platform-socialpost-dynamodb` in `us-east-1`. It added only `SocialPostClickTable`, with no replacements. These tables are shared across API stages.

The transaction table uses provisioned capacity. The migration now accepts explicit index capacity instead of refusing provisioned tables. Applied command:

```powershell
./infra/social-attribution-index.ps1 -Profile default -Region us-east-1 -ReadCapacityUnits 8 -WriteCapacityUnits 2
```

Runtime analytics queries passed after backfill, verifying the Lambda's access to both indexes. IAM simulation was unavailable, but the actual endpoint succeeded.

Instagram diagnosis: Meta returned HTTP 401/code 190 for the affected connection, reporting expiry at 2026-10-26 06:00 UTC. The callback had stored a short-lived token with no expiry. New code exchanges authorization-code tokens for long-lived Instagram tokens and persists the provider expiry. Explicit rejections now become `failed` with safe delivery diagnostics; ambiguous outcomes retain duplicate protection.

Live acceptance testing also exposed a shared-library timestamp bug: ServiceStack rounded `2026-10-29T14:58:52.5183319Z` to `...5183318Z` on read, causing conditional publish/cancel writes to reject unchanged records. The sibling PlatformLibrary `DbCRUD` now restores exact top-level DateTime values when reading raw storage attributes, consistently reading one record, or batch-reading records. Existing unrelated library edits were preserved.

Release `20261029-social-qa-002` was published and deployed successfully to all 16 `gany_dev` aliases. It supersedes `20261029-social-qa-001`, whose live tests exposed the precision defect. The production aliases were not promoted; the publish function remains version 13 in `gany_prod`, while dev is version 15.

Validation: 119 publishing regression checks and 116 OAuth checks passed. Eleven deployed API acceptance checks passed: draft creation/status/cancellation, repeated cancellation, expired-token rejection with reconnect guidance, duplicate-send protection, cancellation of a definitively rejected post, and analytics for the reported and newly created posts. POST tests used authenticated AWS API Gateway test invocation with `runwayAlias=gany_dev`; GET tests used the actual dev HTTPS endpoints. These are API integration checks, not browser end-to-end acceptance. Evidence is in `tmp/dev-social-verification.json` and `tmp/qa-dev-verification-002.log`; the final deployment audit is under `artifacts/lambda-runs/20261029T151354Z-a0ca7993-Deploy/`.

Remaining acceptance: sign into the opened stage browser and reconnect Instagram, then verify successful publication from both the grid and Create Post UI. The browser remained at sign-in and no fresh Instagram authorization was available during this run. No successful Instagram publication is claimed. The original `submission_unknown` record was left unchanged: automatic approval review rejected a proposed historical-state rewrite, so verification used fresh QA data instead.

Additional test limitation: the existing sibling-library metadata initialization stress test intermittently reported one error in 24,000 checks (enumeration/null-reference), and also completed with zero errors on a subsequent run. Its baseline comparison and current outputs are saved in `tmp/qa-dynamo-comparison.json`; the timestamp regression itself passed. This separate concurrency stress issue was not changed as part of the social-post fix.

API Gateway click routing was deployed on 2026-10-01 to API `6op50ujgu6`, stages `dev` and `prod`, using stack `giveanywhere-social-click-route`. The integration uses each stage's `runwayAlias` (`gany_dev` / `gany_prod`). Deployment IDs are `a6iu65` (dev) and `6eb7y8` (prod); previous IDs were `8f89si` and `h4mbu7`. The existing `/operations/dashboard` route was also published to prod with explicit approval. This does not confirm deployment of the remaining database or application changes. No payments or outreach were sent during development.

## Completion and analytics contract

Landing clients POST `/social/posts/clicks` with `social_post_id`, `click_id` and `visitor_id` (GUIDs), and optional `social_platform`. The server supplies organization, campaign and timestamp. Conditional insertion of post ID + click ID makes retries and cross-app handoffs idempotent. Unique visitors are browser identifiers, not verified people. Tracking errors do not block giving.

Payment requests carry `social_post_id`. New pledges and recurring schedules persist it. Existing pledge payments use saved pledge attribution. Campaign and organization membership are validated before payment. `CompleteOneTimePay` passes the identifier through `LogTransactionToDbAsync` / `WriteTransactionToDbAsync` to `TransactionAmount.SocialPostID` and `GivingRecord.social_post_id`. `SocialDonationAmount` excludes tips and covered fees. Analytics reads this canonical record and requires `StatusState=success`. Completion still accepts processing/settling responses, but the transaction write now preserves their actual status instead of marking them successful; subsequent failed/refunded status changes exclude the transaction from successful metrics.

`GetSocialPostAnalytics` returns the existing delivery summary plus `engagement`: clicks, unique visitors, donations, donors, amount raised per currency, conversion rate, and clicks per platform. Conversion rate is successful donation transactions / recorded landing events × 100; repeated donations can make it exceed 100%. The combined amount is null for multiple currencies. Donations deduplicate by transaction ID. Donor counts use account ID, falling back to normalized email without exposing it in responses.

The analytics Lambda also accepts `organization_id` and optional `campaign_id` without a post ID, returning aggregate engagement and per-post analytics. Expose that through authenticated `GET /social/analytics` using the same organization authorization as existing social reads. Do not expose organization analytics publicly.

## Deployment order

1. Deploy `socialpost-dynamodb.yaml` to add `SocialPostClick`. Existing tables retain their retain policies.
2. Run `social-attribution-index.ps1` with the intended AWS profile and region to add `SocialPostID-index` to the existing `TransactionAmount` table. Wait for ACTIVE before releasing analytics. The script does not replace the table.
3. Allow the click Lambda GetItem on SocialPost and PutItem on SocialPostClick; allow analytics Query on SocialPostClick/SocialPostID-index and TransactionAmount/SocialPostID-index. Payment and CRM roles need GetItem on SocialPost. Preserve existing permissions.
4. Deploy updated PlatformLibrary consumers: PaymentAPIOneTimePay, PaymentAPICompleteOneTimePay, recurring schedule/payment Lambdas, CRM pledge creation/read Lambdas, and SocialPostAPIGetSocialPostAnalytics. Deploy the new SocialPostAPIRecordSocialPostClick project, included in the solution and lambda-runner.json.
5. `social-click-route.yaml` manages public POST /social/posts/clicks, CORS OPTIONS, and stage-scoped Lambda permissions on the existing REST API. Parameters are RestApiId, RootResourceId, and the unqualified ClickLambdaArn. It uses the existing runwayAlias stage variable. The dev and prod routes are deployed; future route updates require deploying the intended stages. Apply the API's public endpoint throttling. Add authenticated GET /social/analytics for campaign/organization rollups if required.
6. Deploy DataAPILookupTinyUrl and QRCodeAPIGetRedirectUrl. They preserve a supplied `socialpostid` in the destination after cache lookup. Resolver callers must forward the incoming parameter.
7. Deploy Web2.0 and GoGiveAnywhere.Intake. Verify each Intake `environment.socialTrackingUrl` points to the same backend environment as its payment API. Existing legacy payment URLs were not changed.
8. In DEV, follow tagged direct and short links through refresh, authentication, test payment and completion. Verify one attributed transaction, correct amounts, failed/pending exclusion and duplicate completion behavior. These live payment checks have not been performed.

## Limits

Attribution uses a 24-hour browser session window. Internal navigation/reloads reuse the landing event ID; a different social post replaces session attribution. Checkout handoff carries `socialclickid` and `socialvisitorid` to avoid a second click for the same journey. Blocked storage limits persistence; cleared storage and cross-device visitors cannot be deduplicated.

Existing multi-platform posts share one GUID. Without a platform hint, clicks infer a platform only for single-platform posts; others use `unknown`. Donation platform attribution requires a per-platform post or an additional propagated platform/delivery identifier. Historical untagged transactions are not backfilled.
