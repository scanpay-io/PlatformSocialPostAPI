# Deployed Intake2.0 browser verification — 2026-11-10

## Result

**The active staging frontend does not contain the new social tracking implementation.** Backend tracking and analytics work, but the browser-to-backend flow fails at frontend capture. No frontend deployment was performed during verification.

Actual staging URL supplied by the user: `https://stage.givenow.gogiveanywhere.com/`. The earlier `givenow2.gogiveanywhere.com` buildspec hostname is not the active staging URL and fails DNS resolution.

The supplied example link belongs to `joe-orgtest999`. Verification used Hurtt Demo instead:

- Organization: `1d3a11d5-ece9-4a24-ab3a-1b601fa15861`.
- DEV general fund: `80982d62-07e9-4c3a-a9d3-cff45891c001`, resolved through Offering's AccountID index.
- Browser rendered the HurttDemo checkout and donation form. No donor details or payment method were entered and no payment button was submitted.

## Actual browser landing

Created one uniquely tagged temporary DEV post/delivery fixture representing publication (no provider call), then opened the staging checkout with socialpostid, socialdeliveryid, socialclickid, socialvisitorid and socialat parameters.

After the checkout rendered, DynamoDB contained **zero click records** for that fixture. Analytics recognized the eligible tracked delivery but reported **zero landings**. This is a failed browser tracking check, not an end-to-end pass.

The browser loaded `main-YT6BLOG2.js`. Direct reads of that staging asset and `chunk-IKFMLQM2.js` matched the inspected older S3 bundle byte-for-byte. The older dependency tree contains SMS capture but lacks the social storage key, social delivery parameter/payload handling, and social click endpoint used by the newly implemented source. Evidence: `staging-asset-verification.json` and `intake-deployed-bundle-inspection.json`.

## Backend control

After saving the zero-click browser result, sent one explicitly identified control click directly to `https://api.gogiveanywhere.com/dev/social/posts/clicks` for the same fixture. This request was made by the verification script, not the browser.

- CORS OPTIONS returned 200, allowed POST/OPTIONS and Content-Type, and allowed the staging origin via `*`.
- Click POST returned 200.
- DynamoDB held one correctly attributed event.
- Analytics returned one landing, one browser visitor and one delivery with a landing.

Evidence: `browser-tracking-before-control.json` (browser failure), `staging-backend-control.json` and `backend-control-analytics.json` (backend control). Analytics used IAM invocation for Hurtt Demo; no authenticated browser analytics session was claimed.

## Required deployment

Build the current Intake2.0 source containing `src/app/core/social-checkout-attribution.ts` and its GiveNowService/AppComponent integration, and deploy that build to the hosting target actually serving **stage.givenow.gogiveanywhere.com**. Ensure the entry point and associated chunks are updated and any stale CDN/browser entry-point caching is addressed through the existing staging deployment process. The repository's older buildspec target is not sufficient evidence that this hostname received the build.

Repeat the tagged browser landing and reload/delivery-handoff checks after that deployment. Payment attribution submission remains intentionally untested because real payments are prohibited.

## Safety and cleanup

Temporary fixture rows and the control click were removed using exact IDs and ownership conditions; cleanup verification is recorded in `browser-fixture-cleanup.json`. No historical backfill, payment, social publication, PROD deployment, or alias update occurred. Analytics aliases were verified unchanged: DEV v20, PROD v19.
