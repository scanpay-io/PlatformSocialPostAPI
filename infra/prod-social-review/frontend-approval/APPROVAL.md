# PROD frontend publication approval

Prepared November 10, 2026. **This task has not uploaded or published either frontend. Approval is requested for Web2.0 only.** Backend changes are complete; see `../applied/RESULTS.md`.

## Web2.0 — ready for approval

- Isolated local branch: `codex/prod-social-web`.
- Reconciled PROD baseline: **efa3a5d**, based on `49a27a1` plus authentication fixes already present in live PROD from committed source `9387c63`.
- Release commit: **30522ed750472299e708b04e2123ea46c0efb27e**.
- Exact incremental change: **3 files, 53 insertions, 8 deletions**, recorded in `web.patch`:
  - `src/app/core/query-params/social-attribution.service.ts`
  - `src/app/core/query-params/social-attribution.service.spec.ts`
  - `src/app/core/query-params/query-param-preservation.service.ts`
- Behavior: preserve delivery, click, visitor and attribution-time parameters across navigation and checkout; deduplicate clicks per delivery; send delivery identity with click tracking.
- Excluded unrelated CRM/email-builder/AI working-tree changes. Original repositories were not reset or cleaned. No source push or remote merge was performed.

Baseline reconciliation: 358/358 deployed JavaScript modules matched after normalizing generated chunk filenames, import order and binding names. Static/dynamic dependency references also matched. Two modules additionally differed only in generated vendor identifier or local variable suffixes. CSS was byte-identical. See `../web-semantic-comparison.json`; the baseline-only source reconciliation is `web-baseline-reconciliation.patch`. This is artifact/source reconciliation, not a claim of browser visual coverage.

### Exact candidate artifact

Directory: `E:\GiveAnywhere\Platform\PlatformSocialPostAPI\tmp\prod-web-release\dist\prod-candidate\browser`

- Build: `node node_modules/@angular/cli/bin/ng.js build --configuration production --output-path dist/prod-candidate`
- **656 files**, **47,175,856 bytes**; individual SHA256 values in `web-manifest.json`.
- Main: **main-7OFPUJMC.js**.
- Index SHA256: **14f85dcb99697fcc2fcb1827418c4be7942e44c55b6f6fcc958e7ceddc4211ef**.
- Tracking API: `https://api.gogiveanywhere.com/prod`. Existing DEV-backed sample-reporting configuration is preserved; this candidate does not change that feature.
- Successful production build; 15 focused tests passed across two files; platform-UI, component-file and control-flow checks passed. Mocked cross-app delivery/session/retry/expiry/channel tests passed against candidate source.
- Existing bundle-size, component-CSS-budget and CommonJS warnings remain. No new UI layout change is included; desktop/mobile visual validation was not performed.

### Publication actions requiring approval

Hosting account **245092049965**, profile `jmah322-245`, region us-east-1:

1. Revalidate every candidate file against `web-manifest.json` and verify the live PROD index is still SHA256 **96f16101d7422df3034a41e3bc13b4837a42d64c7af4bd85593afbd10c5478c7**, main **main-VVT546VB.js**. If a concurrent release changed it, stop and reconcile again.
2. Capture the current S3 index metadata/version and rollback entry point. The read-only snapshot is `web-live-index.rollback.html`.
3. Upload reviewed assets to **s3://gogiveanywhere1.com/** without deleting old objects; `Cache-Control: public,max-age=86400`. Upload a non-secret source/hash manifest as `social-tracking-release.json`.
4. Upload reviewed `index.html` last, `Content-Type: text/html`, `Cache-Control: no-cache,max-age=0,must-revalidate`.
5. Invalidate **/* only on CloudFront E3293012YDHW2L**, wait for completion, and verify the served index/manifest/assets at `https://gogiveanywhere.com/` against the candidate hashes.
6. Verify tracking without creating payments or publishing posts. Any record-writing browser test must remain explicitly scoped and separately identified; this package's completed checks used mocked boundaries/read-only PROD requests.

Rollback: restore the immediately pre-publication Web index and its metadata, invalidate that same distribution, and verify its hash. Retain old hashed assets. No DNS, origin/cache-policy, bucket configuration, staging pipeline, or PROD pipeline changes are requested.

## Intake2.0 — already delivered by a concurrent release; no upload requested

At **19:25:09 UTC**, an independent measurement-health release replaced the earlier PROD build while this work was in progress. It includes the social tracking changes. This task did not perform that deployment.

- Live main: **main-BUPUDX7E.js**.
- Live index SHA256: **ebbdb8b5da0069dec548f472e5ebe294c28e3070639ef484aaa1216a0f63b1bb**.
- All **24 live assets** exactly matched the concurrent release's saved PROD artifact at `E:\GiveAnywhere\WebApps\Intake2.0\deployment\measurement-health-20261110\prod-build\browser`.
- Its five social-tracking source files match our tested candidate after line-ending normalization. Live bundles contain the social session namespace, delivery identity, and click endpoint. See `intake-current-release-verification.json` and `frontend-intake.json`.
- The isolated earlier candidate, commit **ff2746913cafa082cffdf68a727604701b0540ad**, built successfully and passed **142 tests**. It is **SUPERSEDED — DO NOT PUBLISH** because it predates the concurrent measurement fixes. Its `intake.patch` and `intake-manifest.json` are retained only for tracking-diff/test provenance.

Leave current Intake PROD in place. No Intake upload, invalidation, or pipeline action is included in the requested approval. The concurrent release was built from committed tracking source plus measurement working-tree changes; this report verifies its saved artifact/live identity, not a clean commit for that independent release.

**Approval requested: publish the exact Web2.0 artifact above to `gogiveanywhere.com`, leaving the current Intake release in place.** No historical backfill, payment creation, or social publication is included.
