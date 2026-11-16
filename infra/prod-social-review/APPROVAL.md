# PROD social tracking rollout — approval package

Prepared November 10, 2026 from read-only inspection. **Nothing has been deployed, promoted, configured, generated as a secret, or backfilled.** This package defines the proposed changes; approval does not waive the release gates below.

## Verified baseline

- Backend account **145505076425**, region **us-east-1**, REST API **6op50ujgu6**.
- PROD stage: deployment **vrokob**, `runwayAlias=gany_prod`. Verified DEV snapshot: **iarb8e**, `runwayAlias=gany_dev`.
- Full deployed API comparison found **15 method changes, all within social tracking scope**, and no unrelated method differences. Normalized live draft core integrations/authorization match DEV across 554 resources / 1,049 methods. We will reuse immutable snapshot iarb8e, rather than create a snapshot of the mutable draft.
- Shared `SocialPostDelivery.AccountPublishedAt-index` is already **ACTIVE**, keys `AccountScopeKey`/`PublishedAtKey`, projection ALL. No table/index/capacity change is required.
- Analytics: PROD **v19**, DEV **v20**, identical code hashes. PROD has **no cursor key**; DEV has its 32-byte key. Never promote v20 to PROD because that would reuse DEV's key.
- Hosting account **245092049965**. Intake PROD: `givenow.gogiveanywhere.com`, bucket `givenow1.gogiveanywhere.com`, distribution **EP3CSY54R3V37**. Web PROD: `gogiveanywhere.com`, bucket `gogiveanywhere1.com`, distribution **E3293012YDHW2L**.

## 1. Backend release

See **BACKEND-LAMBDAS.md** for all 19 functions and versions. Required changes are **11 promotions of existing immutable versions plus one new PROD analytics version**. Seven payment/resolver consumers already match DEV and should remain unchanged.

The 11 promotion candidates have identical inspected runtime configuration to their current PROD versions; their code differs. Full compared configuration includes environment, runtime, handler, role, VPC, timeout, memory, layers, tracing, architecture, storage, logging, and other returned release settings. See `runtime-parity.json`. Recheck hashes, alias RevisionIds, routing weights and configuration immediately before approval execution. Exact guarded update-alias inputs: `alias-promotions.pending.json`.

Existing immutable candidates avoid rebuilding different bytes from a moving source branch. If Joe chooses to rebuild, build these 12 functions against the approved PlatformLibrary sources and regenerate the version/hash manifest before alias promotion. PlatformLibrary is not a separate Lambda. The five OAuth/connection Lambdas in the social release runner are excluded.

### Analytics key and publication

1. Coordinate exclusive access to analytics `$LATEST` while preparing this version; capture its code/configuration and current DEV/PROD alias revisions.
2. Use code with the exact verified v20/v19 SHA256 recorded in `analytics-key-version-plan.json`. If `$LATEST` differs, use the known immutable package, not an unreviewed local rebuild.
3. Preserve the existing PROD environment entries and merge **SOCIAL_ANALYTICS_CURSOR_KEY**, containing base64 of 32 cryptographically random bytes, distinct from DEV. Generate only after approval. Do not print the key or save it in reports/files.
4. Guard mutations with RevisionId, wait for successful configuration update, then publish a new immutable version. Its number is assigned by AWS and cannot be specified before publication. Check code hash and that the version contains the intended PROD key.
5. Restore the captured `$LATEST` code/configuration, including its existing DEV key, so future DEV builds do not accidentally inherit the PROD key. Verify DEV alias/version/key fingerprint remains unchanged.
6. Promote only `gany_prod` to the newly verified version using the current alias RevisionId. Record version and code/configuration hashes without secret values.

Future deployment gate: preserve environment variables, validate at least 32 decoded key bytes, and compare environment-specific key fingerprints before promotion. The current runner's key-length guard alone does not establish DEV/PROD key separation. A PROD release must never blindly promote the DEV analytics version. No new secret-manager/IAM infrastructure is proposed.

## 2. Exact infrastructure changes

| Resource | Proposed change | Exact artifact |
|---|---|---|
| New stack `giveanywhere-social-analytics-prod-invoke` | Add one `AWS::Lambda::Permission` for `scanpay_get_social_post_analytics:gany_prod`; principal `apigateway.amazonaws.com`; SourceAccount 145505076425; SourceArn `arn:aws:execute-api:us-east-1:145505076425:6op50ujgu6/prod/GET/social/analytics` | `analytics-prod-permission.pending.json` |
| API stage `prod` | Change deployment **vrokob → iarb8e**; retain `runwayAlias=gany_prod` and every unrelated stage setting | `prod-stage-update.pending.json` |
| PROD POST `/social/posts/clicks` | Set rate **50 requests/sec**, burst **100**; remains public | Same stage patch |
| PROD GET `/social/analytics` | Set rate **10 requests/sec**, burst **20**; authenticated | Same stage patch |

The permission template passed AWS `validate-template`; no stack or change set was created. At execution, inspect its change set and require exactly one permission resource addition.

Promoting snapshot iarb8e makes these exact API changes effective in PROD:

- Require Cognito authorizer **waut26**, header **X-Authorization-SP**, on four previously unauthenticated GETs: post list, post detail, post status, and post analytics under `/organization/{organizationid}/social/posts`.
- Inject trusted Cognito `sub` and use `passthroughBehavior=never` for all ten post-management method templates: list GET/POST; detail GET/PUT/DELETE; publish POST; schedule POST; cancel POST; status GET; analytics GET. Unsupported unmapped content types will be rejected.
- Existing post analytics forwards `contract`, `post_id`, `campaign_id`, `from_utc`, `to_utc`, `outcome_before_utc`, `view`, `limit`, and `next_token`.
- GET `/data/tinyurl/{shortcode}` and GET `/qrcode/{qrcodeid}/url` forward `socialpostid` and `socialdeliveryid`.
- POST `/give/onetime` forwards optional JSON `social_post_id`, preserving the other payment mapping fields.
- Add the already-created `/social/analytics` GET/OPTIONS to PROD's deployed snapshot. GET uses Cognito and AWS_PROXY to analytics with `${stageVariables.runwayAlias}`. OPTIONS permits GET/OPTIONS and Content-Type/Authorization/X-Authorization-SP.

Exact 15 before/after method objects: `social-prod-method-changes.json`. Exact 13 before/after mapping templates: `mappings/`. No shared API control-plane patches or new API resources are required because they already exist. Do not recreate the DEV route stack or shared index. No execution-role expansion, DNS, VPC, DynamoDB capacity, transaction migration, or unrelated throttle change is proposed.

## 3. Frontend publication scope and release gate

Both frontends require a PROD build. Inspection followed **13 Intake assets** and **358 Web assets**: Intake has no social capture markers; Web has older social capture but lacks `socialdeliveryid` and `socialat` handoff markers.

Intake tracking reference is stage commit **dd4d990d8cfbf2b3d36e9371746654b9efbf05c9**, whose direct Hurtt Demo flow passed. This is a reference for the tracking patch, **not authorization to overwrite PROD with the entire stage branch**. The remote `production` branch is **171dff7d8aa242c8dee3bfa869377d8921fbb489**, dated November 2, while the served PROD index is dated November 9; stage differs in 25 application/config files including unrelated measurement and pledge work. The exact current PROD source must be reconciled before selecting a release baseline.

Web2.0's working tree also contains unrelated edits. Use a clean isolated release and only the reviewed tracking/handoff changes, preserving current PROD features and environment settings. Do not publish the dirty working tree or merge an entire feature branch.

Before any frontend upload, produce and review the exact release commit, source diff, production build command/output, asset manifest, API endpoint and content hashes for each frontend. **Those final PROD artifacts are not prepared or approved by this planning step.** This is the remaining release gate; the plan is not a claim of complete frontend deployment readiness.

For Intake use the production Angular configuration (`pnpm exec ng build --configuration production`, output `dist/intake/browser`, API `https://api.gogiveanywhere.com/prod/`). Do not use the stage-only `intake2-buildspec.yml` or `scripts/deploy-stage.cjs`. Do not change the approved staging pipeline to point to PROD.

Once artifacts are reviewed: snapshot existing index/object version and build manifest; upload assets without deleting old objects; upload a non-secret commit/hash manifest; upload index.html last with `no-cache,max-age=0,must-revalidate`; invalidate `/*` only on the corresponding PROD distribution; wait and verify served manifest/index hashes. Exact hosting targets and metadata are in `frontend-publication.pending.json`. No origin, cache-policy, bucket configuration, or pipeline configuration change is proposed.

## 4. Historical-data compatibility decision

Current shared tables contain **73 posts and 64 deliveries**, all missing environment scope. Among them are **4 queued posts/4 queued deliveries**, 14 published posts/deliveries, and one submission_unknown post/delivery. These counts do not prove which environment owns a record.

With the new strict readers and mutation guards, unscoped historical records are excluded from listing/analytics and cannot be managed/published through scoped operations. Historical links will not gain attribution automatically. Existing queued work may fail scope validation when attempted. Do not silently label these records PROD based on organization or status.

**No historical backfill, cancellation, replay, or external publication is included.** Approval must explicitly accept this forward-only behavior, or defer cutover until a separate ownership-backed migration/queued-work plan is reviewed. Leave those records and schedules untouched during this rollout.

## 5. Coordinated rollout and verification

1. Resolve the frontend source/artifact gate and historical-record decision. Re-read aliases, stage snapshot/variables/settings, policies, table/index state and frontend index metadata; stop on unreviewed drift.
2. Prepare the new PROD analytics version without changing its alias or DEV. Inspect/create only the approved route-specific permission. Snapshot rollback artifacts.
3. Use a coordinated cutover window. The new backend readers require trusted actor context, while old writers do not stamp scope; API and backend version changes are not transactional. First apply the PROD API snapshot/auth/throttles, then promptly promote the analytics version and the eleven listed aliases. Do not allow normal social create/publish/schedule operations during the mixed-version window. Do not alter a scheduler or create a maintenance mechanism without separately reviewing that action.
4. Verify route authentication and version/key isolation before normal traffic. Publish the two reviewed PROD frontends and verify public build identity, PROD API targets, and correct staging/PROD separation.
5. Non-mutating PROD smoke tests: static frontend identity, analytics unauthenticated 401/CORS, authenticated empty/owned read behavior, foreign-tenant/environment rejection, and stable signed pagination where existing scoped records permit it. Capture metrics without secrets.
6. Live PROD synthetic post/delivery/click writes are **not included** in this approval package. Until separately approved, retain the completed isolated DEV browser/cohort evidence as write-path validation. No payments, actual provider posts, notifications, or historical writes are part of acceptance. Completed payment/conversion and external provider publication were not exercised by the DEV browser test and must not be reported as proven.
7. Monitor social API authorization/5xx rates, analytics cursor errors and latency, click write failures, delivery tracking coverage and empty historical results during the release window. Roll back on unexplained regression; distinguish expected exclusion of old unscoped data from new scoped failures.

## 6. Rollback

- Restore PROD aliases to captured versions in `alias-rollback.json` using freshly read RevisionIds; preserve routing weights. Analytics returns to v19 (known pre-rollout limitation: no cursor signing key).
- Restore PROD API deployment **vrokob** and prior method settings from `stages.json`; proposed patch is `prod-stage-rollback.json`. Keep DEV on iarb8e and its versions unchanged.
- Restore each saved PROD index and matching manifest, then invalidate only the affected PROD distribution. Old assets remain available.
- Leave the additive shared GSI and historical data intact. The narrow invoke permission may remain dormant after API rollback; deleting its stack requires deliberate cleanup review.
- Restore captured `$LATEST` analytics configuration if publication preparation did not already restore it. Never rotate or overwrite DEV's immutable key as a rollback shortcut.

## Approval requested

Approve the **11 named immutable-version promotions, one PROD-specific analytics key/version publication, one route-scoped invoke permission, PROD stage vrokob→iarb8e, and the two specified throttle overrides**. Confirm whether to accept the documented forward-only historical behavior.

Frontend upload authorization should follow review of the reconciled source and exact PROD build manifests. No infrastructure or deployment action will occur on the basis of this document alone. No backfill, PROD synthetic writes, payments, or provider publication is included.

Evidence: `lambda-inventory.json`, `runtime-parity.json`, `stages.json`, `prod-api.json`, `dev-api.json`, `unrelated-dev-prod-differences.json`, `draft-dev-core-drift.json`, `delivery-table.json`, `historical-scope-summary.json`, and `frontend-*.json`.
