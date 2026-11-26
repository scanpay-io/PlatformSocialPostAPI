# X publishing validation fix

The DEV stage export inspected on 2026-11-26 omits `text_social` from the POST `/organization/{organizationid}/social/posts` integration mapping. The frontend supplies `connection_id` and `consent`, but the Lambda never receives them. Publishing then rejects the saved draft before calling X. Connecting the account again cannot repair that draft.

`social-post-create.vtl` shows the corrected contract. Generate a narrowly scoped integration patch from a fresh gateway export using `prepare-social-post-mapping.ps1 -ApiExport <export.json> -OutputFile <patch.json>`. The generator preserves the existing authorizer context and all existing mapping fields, adding optional `text_social` and `tiktok` passthrough. Review and apply that patch to the existing create-post integration, then deploy the intended DEV stage. No gateway change or deployment is performed by the generator.

The Web2.0 text composer now uses the existing `validationErrorMessage` helper for capitalized and lowercase API messages, and a friendly fallback for transport failures. It rejects missing account IDs before creating a draft. The backend distinguishes missing platform, account, and consent errors while preserving mandatory consent.

After deployment, create a fresh post with the selected X account and explicit confirmation. Existing drafts whose options were dropped cannot be repaired by repeating Publish. Verify the returned provider post ID and URL after an approved test publication. Do not automatically retry an ambiguous submission.

## Stage deployment — 2026-11-26

- API source: `fdc4da3`; frontend stage source: `1e28d7df5aa1449bcf0e63c4fa8415a09287adea`.
- DEV gateway deployment: `0icd2f` (previous `5c83wu`). Exported the deployed stage and verified that `text_social` is forwarded.
- Publishing Lambda: `scanpay_publish_social_post:gany_dev` version `22` (previous `21`); package SHA-256 base64 `ogqY6qU+z3k11BDHDYJ8mpvMaMh8a6nw66GDnyp6Ax4=`.
- Frontend pipeline `Web2-0PipeLine` execution `6c8db3bc-926b-49dc-9cb4-c2648e2aef3c` succeeded at the stated source commit. Stage serves the deployed `main-QKFQFJVK.js` asset.
- Local validation: 29 backend checks and 5 focused frontend tests passed; shared UI/component checks passed. The full frontend test compilation has unrelated existing template-editor and promo-code test errors.
- Live browser check: stage Social Media loads under HurttDemo, with X shown as not connected. No live post was submitted. Reconnection is awaiting explicit user authorization after automatic approval review blocked CONNECT as a new OAuth authorization.
