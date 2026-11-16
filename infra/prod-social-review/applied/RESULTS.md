# PROD backend rollout — completed November 10, 2026

Applied in account **145505076425**, us-east-1. No historical backfill, payment creation, social publication, or frontend publication was performed by this rollout.

## Backend versions

All names below have prefix `scanpay_`; alias is `gany_prod`.

| Lambda | Previous | Current |
|---|---:|---:|
| create_social_post | 21 | 22 |
| update_social_post | 17 | 18 |
| delete_social_post | 17 | 18 |
| cancel_social_post | 18 | 19 |
| schedule_social_post | 18 | 19 |
| publish_social_post | 19 | 20 |
| get_social_post | 16 | 17 |
| get_social_posts | 16 | 17 |
| get_social_post_status | 17 | 18 |
| record_social_post_click | 9 | 10 |
| get_social_post_analytics | 19 | **21** |
| add_donor_pledge | 61 at planning | **62**, already promoted by concurrent release |

This rollout changed **11 aliases**. The guarded cutover detected that pledge had independently reached the approved v62; it verified the code hash and preserved that release and its description. Seven payment/resolver consumers required no updates. No backend rebuild remains necessary for this rollout.

Analytics v21 uses the approved code hash `Uq0OGZoD0MuMaCwPpqrlLCGlNR02aAtIgK0sDoDMzpE=` and a new 32-byte PROD cursor key. The key was generated in memory and is not included in this report. PROD and DEV keys differ. Analytics `$LATEST` was restored to its captured environment; DEV remained v20 with its existing key.

## Infrastructure applied

- Stack `giveanywhere-social-analytics-prod-invoke`: one Lambda invoke permission for `scanpay_get_social_post_analytics:gany_prod`, principal `apigateway.amazonaws.com`, SourceAccount `145505076425`, SourceArn `arn:aws:execute-api:us-east-1:145505076425:6op50ujgu6/prod/GET/social/analytics`.
- API `6op50ujgu6`, stage `prod`: immutable deployment **vrokob → iarb8e**; retained `runwayAlias=gany_prod` and unrelated settings.
- POST `/social/posts/clicks`: rate **50/sec**, burst **100**.
- GET `/social/analytics`: rate **10/sec**, burst **20**.
- Existing shared `SocialPostDelivery.AccountPublishedAt-index` was ACTIVE and unchanged. No DNS, table, index, execution-role, or frontend pipeline change was needed.

## Verification and limits

At 19:15 UTC, alias/stage comparisons confirmed all intended PROD targets, unchanged DEV backend aliases/stage/key, and unchanged other aliases. See `verified.json`, `aliases-applied.json`, and `already-at-approved-target.json`.

Nine read-only checks passed: five unauthenticated API reads rejected with 401, analytics CORS passed, IAM-invoked Hurtt Demo cohort summary and empty delivery pagination returned 200, and malformed cursor returned 400. These are not an authenticated browser-to-PROD conversion test or populated multi-page pagination test. No PROD test records were created. See `read-only-smoke.json`.

The forward-only compatibility decision remains: unscoped historical records are excluded by scoped readers and cannot be managed through scoped mutations. Historical records and queued schedules were not changed.

Local release-runner safeguards now reject analytics promotions that reuse the other environment's key or unexpectedly rotate the target environment's existing key. **10 guard checks passed.** These safeguards are local repository changes in `release-lib.ps1`, `release-runner.ps1`, and `tests/cursor-deployment-guard.ps1`; they have not been pushed to a remote release runner.

Frontend status and the remaining approval are documented in `../frontend-approval/APPROVAL.md`.
