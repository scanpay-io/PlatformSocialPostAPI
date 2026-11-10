# DEV redeployment recheck — 2026-11-10

Verification did not find the reported new deployment on the previously established targets. No configuration, aliases, infrastructure, payment records, or historical data were changed during this recheck. No new tracking fixtures were created because the release gate failed.

## Analytics

Verified account `145505076425` using STS and inspected Lambda in `us-east-1`.

- `scanpay_get_social_post_analytics:gany_dev` remains version **18**, release `social-20261110-001`, last modified 2026-11-10 15:11:16 UTC.
- `development` also points to **18**. No published version newer than 18 exists.
- Version 18 has **no SOCIAL_ANALYTICS_CURSOR_KEY**. The key remains on unpublished `$LATEST`; PROD remains version 17.
- Read-only Hurtt Demo analytics invocations executed version 18 and reproduced both failures: publication-date cohort HTTP **400** (`post_id must be a GUID and cannot be combined with cohort filters`); cursor request HTTP **500** (missing cursor-key configuration).
- These were IAM-authorized Lambda integration checks with the supplied organization ID, not authenticated browser requests.

See `analytics-redeployment.json` and `hurtt-redeployment-recheck.json` for the sanitized results. Key values were never logged or saved.

## Intake2.0

The repository buildspec names bucket/hostname `givenow2.gogiveanywhere.com` and CloudFront distribution `E1PIY8XCR3AMMP`.

- A direct HTTPS fetch of that hostname failed from this environment.
- AWS returned `NoSuchDistribution` for the configured distribution ID. No distribution with an Intake/givenow alias or origin was found in this account's distribution inventory.
- The configured S3 bucket's `index.html` is dated **2026-11-09 22:23:38 UTC**, before the tracking changes. It references `main-YT6BLOG2.js`.

This establishes that the documented target does not show the expected new frontend entry point; it does not establish the state of a different hosting target. Requested the actual DEV URL and backend release ID/deployment command to reconcile the discrepancy.

## Required next information

Confirm the analytics release ID and AWS account/region used, plus the deployed Intake2.0 DEV URL. The previous positive click/post-summary tests and local frontend checks remain recorded in `INTAKE2-HURTT.md`; they are not evidence that this reported redeployment is active. Full browser-to-backend acceptance and live signed pagination remain pending.
