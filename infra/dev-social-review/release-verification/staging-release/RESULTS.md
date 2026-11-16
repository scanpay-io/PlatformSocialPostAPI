# Intake2.0 staging release — November 10, 2026

Released commit **dd4d990d8cfbf2b3d36e9371746654b9efbf05c9** to remote `stage`. The release was prepared from the previous stage tip, integrating only social tracking, its SMS attribution dependency, tests, and deployment fixes. Unrelated feature-branch changes were excluded.

## Deployment

- Applied the approved `GGA-Intake2.0PipeLine` configuration in hosting account 245092049965: source `stage`, push trigger for `stage`, and Source/Build stages with the duplicate S3 Deploy action removed.
- Pipeline version **5**, execution **12d8913c-dac3-47f1-a87c-9acce1da6fb0**, status **Succeeded**.
- CodeBuild **Intake2-0BuildProject:7a36a5f8-143a-4e11-b741-21f1da69de19** completed successfully.
- Staging serves **main-3BL5C4OC.js**. Public `build-info.json` identifies the approved commit; public HTML SHA256 matches the build manifest.
- Existing assets were retained, HTML uploaded last, and staging CloudFront invalidation completed.
- DEV analytics remains **v20**, with a **32-byte cursor key**. The key was verified without printing or saving it.
- Local validation: **136 frontend tests**, **9 deployment guard tests**, and development build passed.

## Actual browser verification

Used Hurtt Demo organization `1d3a11d5-ece9-4a24-ab3a-1b601fa15861` and Offering `80982d62-07e9-4c3a-a9d3-cff45891c001`. The deployed page rendered HurttDemo. No form fields were filled and no payment action was taken.

| Action | Persisted clicks | Landing events | Unique visitors | Deliveries with landing |
|---|---:|---:|---:|---:|
| Open Facebook delivery link | 1 | 1 | 1 | 1 |
| Reload the same link | 1 | 1 | 1 | 1 |
| Open Instagram delivery link in the same tab | 2 | 2 | 1 | 2 |

Each delivery has exactly one landing. Donations and revenue remained zero. These events came from the deployed browser application; no direct control click was injected into this browser fixture. Analytics was verified through IAM Lambda invocation with the Hurtt Demo actor context, not through a signed-in analytics UI.

The separate DEV backend suite passed **20 checks**, including publication-date cohort totals, click retry deduplication, platform/tenant/environment protections, trusted-actor requirements, three-page traversal, deterministic cursor replay, and rejection of tampered or filter-mismatched cursors.

## Cleanup and scope

Temporary records were deleted conditionally using fixture ownership markers. Final consistent reads verify both backend-suite and browser fixture/click keys are absent; see `final-checks.json` and `browser-fixture-cleanup.json`.

PROD's frontend index metadata, CloudFront configuration, and analytics alias match the pre-release snapshot; see `prod-unchanged.json`. No PROD update was issued. No IAM, DNS, bucket configuration, or Lambda update was made. No payments, provider social publications, or historical backfills were performed. Completed payment/conversion behavior was deliberately not exercised.

Evidence: `pipeline-applied.json`, `execution-status.json`, `deployed-verification.json`, `browser-tracking-first.json`, `browser-tracking-reload.json`, `browser-tracking-delivery-switch.json`, `hurtt-tests.json`, and cleanup evidence in this directory. The saved prior stage index is `index-before.html`.
