# Staging browser rerun — November 10, 2026

At approximately 10:35 a.m. Pacific, the staging entry point still returned `main-YT6BLOG2.js`, Last-Modified November 9, 2026 22:23:38 UTC. A unique verification query and no-cache request returned the same entry point with `RefreshHit from cloudfront`.

The actual staging main and checkout-service chunks match the previously inspected older bundle byte-for-byte. The service chunk contains SMS capture but not the implemented social capture. This is evidence that the new frontend build is not being served at this hostname; it does not establish which deployment step failed.

## Browser test

- Opened `stage.givenow.gogiveanywhere.com` using Hurtt Demo organization `1d3a11d5-ece9-4a24-ab3a-1b601fa15861` and its DEV fund `80982d62-07e9-4c3a-a9d3-cff45891c001`.
- Created an isolated temporary post/delivery fixture representing publication, with no provider publication.
- Opened a fresh tagged link with unique post, delivery, click and visitor identifiers.
- Confirmed the browser rendered the HurttDemo donation form.
- The browser visit produced **zero click records**. DEV analytics recognized the tracked fixture delivery but returned **zero landing events and zero visitors**.

Result: **frontend capture failed**. Reload deduplication and different-delivery attribution could not be meaningfully validated because the initial landing was never captured. No backend control click was submitted in this rerun, so the zero-event result represents the actual browser-only flow.

## Next deployment check

Inspect the deployment job's source commit, output directory, destination bucket/origin for `stage.givenow.gogiveanywhere.com`, and cache invalidation. The served checkout code must include `SocialCheckoutAttribution` functionality (`intake.social:`, `social_delivery_id`, `social/posts/clicks`) before repeating acceptance. No hosting or CDN configuration was changed by this verification.

All temporary fixture rows were conditionally removed and their absence verified. PROD and DEV analytics aliases were verified unchanged (PROD v19, DEV v20). No payments, account creation, social publications or historical backfills occurred.

Evidence: `entry-point.json`, `assets.json`, `browser-tracking-results.json`, `browser-fixture-cleanup.json`, and `aliases.json` in this directory.
