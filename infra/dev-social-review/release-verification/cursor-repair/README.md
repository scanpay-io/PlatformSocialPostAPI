# DEV analytics cursor repair — 2026-11-10

Restored cursor signing with a newly generated 32-byte cryptographic key, base64 encoded. Merged it into the existing `$LATEST` environment without printing or persisting its value in review artifacts. Published analytics version **20** with the same code hash as version 19, then promoted only **gany_dev** using revision guards.

**PROD remains on version 19.** Its alias revision, description and version were verified unchanged against the pre-repair snapshot. The `development` alias also remains unchanged on version 19. API Gateway DEV uses `gany_dev`.

## Deployment protection

- `SocialPostAPIGetSocialPostAnalytics/aws-lambda-tools-defaults.json` now uses `append-environment-variables` rather than `environment-variables`. The installed Amazon Lambda Tools help confirms that the former merges settings while the latter replaces the live environment.
- `release-lib.ps1` validates the analytics cursor key before publication. Its existing configuration hash checks continue to detect environment drift during code updates.
- `release-runner.ps1` validates the target immutable analytics version before promotion, preventing an old keyless release from being promoted through this runner.
- `tests/cursor-deployment-guard.ps1` passed all six checks: absent/malformed/short keys rejected; valid key accepted; unrelated functions unaffected; append-only defaults required.

Use these updated repository deployment files for subsequent releases. Keep the key in Lambda environment configuration; do not copy it into source-controlled defaults. Explicit manual commands that replace the environment or bypass the runner are outside these guards.

## Verification evidence

`verified.json` records the DEV version, PROD version, 32-byte key-length check, unchanged-code check and unchanged-other-alias check. No secret values are included. The Hurtt Demo pagination suite uses temporary fixtures and IAM-authorized analytics invocation; it does not represent signed-in browser acceptance. Test results and cleanup evidence are stored in the parent directory.
