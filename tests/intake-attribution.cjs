// Run with node tests/intake-attribution.cjs; uses the sibling Web2.0 TypeScript installation.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const webApps = path.resolve(__dirname, '../../../WebApps');
const ts = require(path.join(webApps, 'Web2.0/node_modules/typescript'));
const source = fs.readFileSync(path.join(webApps, 'GoGiveAnywhere.Intake/src/app/_services/social-attribution.service.ts'), 'utf8');
const output = ts.transpileModule(source, { compilerOptions: {
  module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2017, experimentalDecorators: true
}, reportDiagnostics: true });
assert.equal(output.diagnostics.length, 0);
const storage = () => { const values = new Map(); return { getItem: k => values.get(k), setItem: (k,v) => values.set(k,v) }; };
const postId = '11111111-2222-4333-8444-555555555555';
const clickId = '22222222-2222-4333-8444-555555555555';
const visitorId = '33333333-2222-4333-8444-555555555555';
const requests = [];
let navigate;
class NavigationEnd { constructor(url) { this.urlAfterRedirects = url; } }
class HttpClient { post(url, body) { return { subscribe: (success, failure) => requests.push({ url, body, success, failure }) }; } }
const context = {
  exports: {}, sessionStorage: storage(), localStorage: storage(),
  window: { location: { pathname: '/give', search: `?socialpostid=${postId}&socialclickid=${clickId}&socialvisitorid=${visitorId}&campaignId=campaign` }, crypto: require('node:crypto').webcrypto },
  require(name) {
    if (name === '@angular/core') return { Injectable: () => value => value };
    if (name === '@angular/common/http') return { HttpClient };
    if (name === '@angular/router') return { NavigationEnd };
    if (name.endsWith('/environment')) return { environment: { socialTrackingUrl: '/clicks' } };
    throw new Error(name);
  }
};
vm.runInNewContext(output.outputText, context);
const router = { parseUrl: url => ({ queryParams: Object.fromEntries(new URL(url, 'https://example.org').searchParams) }), events: { subscribe: callback => { navigate = callback; } } };
const service = new context.exports.SocialAttributionService({}, router);
service.initialize();
assert.equal(requests.length, 1);
assert.equal(requests[0].body.click_id, clickId);
assert.equal(requests[0].body.visitor_id, visitorId);
requests[0].failure();
navigate(new NavigationEnd('/payment'));
assert.equal(requests.length, 2);
assert.equal(requests[1].body.click_id, clickId);
requests[1].success();
navigate(new NavigationEnd('/payment'));
assert.equal(requests.length, 2);
assert.equal(service.paymentPayload({ campaign_id: 'campaign' }).social_post_id, postId);
assert.equal(service.paymentPayload({ campaign_id: 'other' }).social_post_id, undefined);
const restored = new context.exports.SocialAttributionService({}, router);
assert.equal(restored.paymentPayload({ campaign_id: 'campaign' }).social_post_id, postId);
console.log('Intake attribution handoff, retry, deduplication, campaign isolation and reload checks passed.');
