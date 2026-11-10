// Executes the actual Web2.0 and Intake2.0 services against a mocked browser/HTTP boundary.
// No servers, outreach, payments, or real HTTP calls.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const webApps = path.resolve(__dirname, '../../../WebApps');
const ts = require(path.join(webApps, 'Web2.0/node_modules/typescript'));
const storage = () => {
  const values = new Map();
  return { getItem: key => values.get(key), setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) };
};
const post = '11111111-2222-4333-8444-555555555555';
const delivery = 'facebook-' + post;
const secondDelivery = 'threads-' + post;
const timestamp = Date.now() - 23 * 3600000;
const landing = '/give?socialpostid=' + post + '&socialdeliveryid=' + delivery + '&campaignId=campaign&socialat=' + timestamp;
class NavigationStart { constructor(url) { this.url = url; } }
class NavigationEnd { constructor(url) { this.urlAfterRedirects = url; } }
function load(relative, intake = false, url = landing) {
  let navigate;
  const requests = [];
  const events = { pipe() { return this; }, subscribe(callback) { navigate = callback; } };
  const router = { url, events, parseUrl: value => ({ queryParams: Object.fromEntries(new URL(value, 'https://example.org').searchParams) }) };
  class HttpClient { post(target, body) { return { pipe() { return this; }, subscribe: handlers => requests.push({ target, body, success: () => { handlers.next?.(); handlers.complete?.(); }, failure: () => handlers.error?.() }) }; } }
  const context = {
    exports: {}, URL, URLSearchParams, sessionStorage: storage(), localStorage: storage(), crypto: require('node:crypto').webcrypto,
    window: { location: { pathname: url.split('?')[0], search: '?' + url.split('?')[1] }, crypto: require('node:crypto').webcrypto },
    require(name) {
      if (name === '@angular/core') return { Injectable: () => value => value };
      if (name === '@angular/router') return { NavigationStart, NavigationEnd };
      if (name === '@angular/common/http') return { HttpClient };
      if (name === 'rxjs') return { filter: () => {}, timeout: () => {} };
      if (name.endsWith('/environment')) return { environment: { baseUrlGive: 'https://example.org/dev/' } };
      throw new Error(name);
    }
  };
  const source = fs.readFileSync(path.join(webApps, relative), 'utf8');
  const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, experimentalDecorators: true }, reportDiagnostics: true });
  assert.equal(compiled.diagnostics.length, 0);
  vm.runInNewContext(compiled.outputText, context);
  const service = intake ? new context.exports.SocialCheckoutAttribution(new HttpClient(), 'https://example.org/dev/') : new context.exports.SocialAttributionService();
  return { service, requests, context, router, navigate: value => intake ? service.capture(new URL(value, 'https://example.org').search) : navigate(new NavigationEnd(value)) };
}
(async () => {
  const web = load('Web2.0/src/app/core/query-params/social-attribution.service.ts');
  const recorded = [];
  web.service.initialize(web.router, async body => recorded.push(body));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(recorded[0].social_delivery_id, delivery);
  web.navigate('/login');
  assert.equal(recorded.length, 1);
  const handoff = new URL(web.service.checkoutUrl('https://example.org/give?data=6001:org:campaign:us', 'campaign'));
  assert.equal(handoff.searchParams.get('socialdeliveryid'), delivery);
  assert.equal(handoff.searchParams.get('socialat'), String(timestamp));
  assert.equal(handoff.searchParams.get('socialclickid'), recorded[0].click_id);
  web.navigate('/give?socialpostid=' + post + '&socialdeliveryid=' + secondDelivery + '&campaignId=campaign');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(recorded.length, 2);
  assert.equal(recorded[1].social_delivery_id, secondDelivery);
  assert.notEqual(recorded[1].click_id, recorded[0].click_id);
  const intake = load('Intake2.0/src/app/core/social-checkout-attribution.ts', true, handoff.pathname + handoff.search);
  assert.equal(intake.requests[0].target, 'https://example.org/dev/social/posts/clicks');
  assert.equal(intake.requests[0].body.social_delivery_id, delivery);
  assert.equal(intake.requests[0].body.click_id, recorded[0].click_id);
  intake.requests[0].failure();
  intake.navigate('/login');
  assert.equal(intake.requests[1].body.click_id, recorded[0].click_id);
  intake.requests[1].success();
  intake.navigate('/give');
  assert.equal(intake.requests.length, 2);
  assert.equal(intake.service.fields('org', 'campaign', 6001).social_post_id, post);
  assert.equal(intake.service.fields('org', 'other', 6001).social_post_id, undefined);
  assert.equal(JSON.parse(intake.context.sessionStorage.getItem('intake.social:https://example.org/dev/')).at, timestamp);
  const expired = load('Intake2.0/src/app/core/social-checkout-attribution.ts', true,
    '/give?socialpostid=' + post + '&socialat=' + (Date.now() - 25 * 3600000));
  assert.equal(expired.requests.length, 0);
  web.service.capture(post, 'campaign', 'sms');
  const sms = new URL(web.service.checkoutUrl(handoff.toString(), 'campaign'));
  assert.equal(sms.searchParams.get('socialdeliveryid'), null);
  assert.equal(sms.searchParams.get('socialat'), null);
  console.log('Web2.0/Intake2.0 delivery handoff, session continuity, retries, isolation, expiry and channel replacement passed.');
})().catch(error => { console.error(error); process.exitCode = 1; });
