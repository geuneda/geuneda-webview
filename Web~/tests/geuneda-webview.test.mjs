// geuneda-webview.js 테스트. 실행: node --test "Web~/tests/*.test.mjs" (Node 21 이상, 패키지 루트에서)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const source = readFileSync(fileURLToPath(new URL('../geuneda-webview.js', import.meta.url)), 'utf8');
const KEY = 'session-key';

// 웹뷰 페이지 흉내: transport 는 'unity'(Android 의 window.Unity), 'webkit'(iOS·macOS 의 messageHandlers), null(일반 브라우저)
// failCalls: 네이티브 전달 경로가 명령(call)을 보낼 때 던진다(true 면 Error, 그 밖의 값이면 그 값)
function load({ transport = 'unity', href = 'https://notice.example.com/ko/index.html', failCalls = false } = {}) {
  const sent = [];
  const raw = [];
  const clickListeners = [];
  const window = {
    location: { href, origin: new URL(href).origin },
    crypto: globalThis.crypto,
    setTimeout: (fn) => fn
  };

  if (transport === 'unity') {
    window.Unity = {
      call: (text) => {
        const message = JSON.parse(text);

        if (failCalls && message.type === 'call') {
          throw failCalls === true ? new Error('native bridge failed') : failCalls;
        }

        raw.push(text);
        sent.push(message);
      }
    };
  } else if (transport === 'webkit') {
    // gree 의 iOS·macOS 처럼 받은 글을 URL 디코딩한다. iOS 는 window.Unity 도 주입하지만 webkit 경로가 먼저다
    window.Unity = { call: () => assert.fail('window.Unity must not be used when webkit handlers exist') };
    window.webkit = { messageHandlers: { unityControl: { postMessage: (text) => { raw.push(text); sent.push(JSON.parse(decodeURIComponent(text))); } } } };
  }

  const document = {
    addEventListener: (type, listener) => {
      if (type === 'click') {
        clickListeners.push(listener);
      }
    }
  };

  const context = vm.createContext({ window, document, URL, Promise, JSON, String, Uint8Array, Math, setTimeout: window.setTimeout });
  vm.runInContext(source, context);
  const geuneda = window.geuneda;
  const doc = sent.length > 0 ? sent[0].doc : null;

  // Unity 가 준비 알림에 답해 메인 프레임에 키를 넘기는 것
  function bind(forDoc = doc) {
    geuneda.__receive({ type: 'bind', doc: forDoc, key: KEY });
  }

  function click(href, options = {}) {
    const link = { href };
    const event = {
      defaultPrevented: false,
      button: 0,
      target: { closest: (selector) => (selector === 'a[href]' ? link : null) },
      preventDefault() { this.defaultPrevented = true; },
      ...options
    };

    clickListeners.forEach((listener) => listener(event));
    return event;
  }

  // Unity 가 EvaluateJS 로 실행하는 스크립트(WebViewMessages.Script 와 같은 모양)
  function evaluate(message, allowed = ['https://notice.example.com']) {
    const condition = allowed.map((origin) => `o===${JSON.stringify(origin)}`).join('||');
    vm.runInContext(`(function(o){if(${condition}){window.geuneda&&window.geuneda.__receive(${JSON.stringify(message)});}})(window.location.origin);`, context);
  }

  return { geuneda, sent, raw, doc, bind, click, evaluate };
}

// vm 컨텍스트의 객체는 다른 realm 이라 deepStrictEqual 이 프로토타입 차이로 실패한다
function plain(value) {
  return JSON.parse(JSON.stringify(value));
}

test('reports ready with a random document id', () => {
  const first = load();
  const second = load();

  assert.equal(first.sent.length, 1);
  assert.equal(first.sent[0].type, 'ready');
  assert.match(first.doc, /^[0-9a-f]{32}$/);
  assert.notEqual(first.doc, second.doc);
  assert.equal(first.sent[0].key, undefined);
});

test('reports ready through webkit message handlers', () => {
  const { sent } = load({ transport: 'webkit' });
  assert.equal(sent[0].type, 'ready');
});

test('calls wait for the key, then go out with the document id and key', () => {
  const { geuneda, sent, doc, bind } = load();
  geuneda.call('notice.list', { category: 'event' });

  assert.equal(sent.length, 1, 'nothing is sent before the key arrives');

  bind();

  assert.deepEqual(plain(sent.slice(1)), [
    { type: 'bound', doc, key: KEY },
    { type: 'call', id: '1', command: 'notice.list', args: { category: 'event' }, doc, key: KEY }
  ]);

  geuneda.call('notice.refresh');
  assert.deepEqual(plain(sent[3]), { type: 'call', id: '2', command: 'notice.refresh', args: {}, doc, key: KEY });
});

test('a bind for another document or a second bind is ignored', () => {
  const { geuneda, sent, bind } = load();
  geuneda.call('notice.list');

  bind('other-document');
  assert.equal(sent.length, 1);

  bind();
  geuneda.__receive({ type: 'bind', doc: sent[0].doc, key: 'another-key' });
  assert.equal(sent.filter((message) => message.type === 'bound').length, 1);
  assert.equal(sent[2].key, KEY);
});

test('Unity scripts reach the page only while the main frame is on an allowed origin', () => {
  const allowed = load();
  allowed.evaluate({ type: 'bind', doc: allowed.doc, key: KEY }, ['http://127.0.0.1:5700', 'https://notice.example.com']);
  assert.equal(allowed.sent[1].type, 'bound');

  // 이동 제한을 비껴가 허용 밖 문서가 메인 프레임이 된 경우: 그 문서가 브리지를 흉내 내도 키를 받지 못한다
  const outside = load({ href: 'https://evil.com/collect' });
  outside.evaluate({ type: 'bind', doc: outside.doc, key: KEY });
  assert.equal(outside.sent.length, 1);
});

test('webkit messages are URL-encoded so plus and percent survive the decoding', () => {
  const { geuneda, sent, raw, bind } = load({ transport: 'webkit' });
  bind();
  geuneda.call('coupon.redeem', { code: 'A+B 50%' });

  const last = raw[raw.length - 1];
  assert.equal(last.includes('+'), false);
  assert.equal(last.includes(' '), false);
  assert.deepEqual(plain(sent[sent.length - 1].args), { code: 'A+B 50%' });
});

test('android messages are sent as plain JSON', () => {
  const { geuneda, raw, doc, bind } = load();
  bind();
  geuneda.call('coupon.redeem', { code: 'A+B' });

  assert.equal(raw[raw.length - 1], `{"type":"call","id":"1","command":"coupon.redeem","args":{"code":"A+B"},"doc":"${doc}","key":"${KEY}"}`);
});

test('call resolves with the matching result of its own document', async () => {
  const { geuneda, doc, bind } = load();
  bind();
  const first = geuneda.call('notice.list', { category: 'event' });
  const second = geuneda.call('coupon.redeem', { code: 'ABCD' });

  // 이전 문서(같은 웹뷰에서 페이지를 옮기기 전)의 같은 ID 결과는 무시한다
  geuneda.__receive({ type: 'result', doc: 'previous-document', id: '1', ok: true, code: 0, value: 'stale' });
  // 응답 순서가 바뀌어도 ID 로 짝짓는다
  geuneda.__receive({ type: 'result', doc, id: '2', ok: false, code: 11003, message: 'Expired' });
  geuneda.__receive({ type: 'result', doc, id: '1', ok: true, code: 0, value: { notices: [] } });

  assert.deepEqual(plain(await first), { ok: true, code: 0, value: { notices: [] } });
  assert.deepEqual(plain(await second), { ok: false, code: 11003, message: 'Expired' });
});

test('unknown results and empty messages are ignored', async () => {
  const { geuneda, doc, bind } = load();
  bind();
  const pending = geuneda.call('notice.refresh');

  geuneda.__receive({ type: 'result', doc, id: '99', ok: true, code: 0 });
  geuneda.__receive(null);
  geuneda.__receive({ type: 'result', doc, id: '1', ok: true, code: 0 });
  assert.equal((await pending).ok, true);
});

test('a call whose args cannot be serialized resolves with invalidArguments instead of rejecting', async () => {
  const { geuneda, sent, doc, bind } = load();
  bind();
  const args = {};
  args.self = args;

  const result = await geuneda.call('test.echo', args);
  const big = await geuneda.call('test.echo', { n: 10n });

  assert.equal(result.ok, false);
  assert.equal(result.code, 12003);
  assert.match(result.message, /could not be serialized/);
  assert.equal(big.code, 12003);
  assert.equal(sent.length, 2, 'only ready and bound went out');

  geuneda.call('notice.refresh');
  assert.deepEqual(plain(sent[2]), { type: 'call', id: '3', command: 'notice.refresh', args: {}, doc, key: KEY });
});

test('a queued call that cannot be serialized does not hold back the calls after it', async () => {
  const { geuneda, sent, doc, bind } = load();
  const args = {};
  args.self = args;
  const bad = geuneda.call('test.echo', args);
  geuneda.call('notice.list', { category: 'event' });

  bind();

  assert.deepEqual(plain(sent.slice(1)), [
    { type: 'bound', doc, key: KEY },
    { type: 'call', id: '2', command: 'notice.list', args: { category: 'event' }, doc, key: KEY }
  ]);
  assert.equal((await bad).code, 12003);
});

test('a native bridge exception resolves the call with unknown', async () => {
  const { geuneda, bind } = load({ failCalls: true });
  const queued = geuneda.call('notice.list');
  bind();
  const direct = geuneda.call('notice.refresh');

  assert.deepEqual(plain(await queued), { ok: false, code: 1, message: 'Could not send the command: Error: native bridge failed' });
  assert.equal((await direct).code, 1);
});

test('a thrown value that cannot be printed still ends only that call', async () => {
  const { geuneda, sent, doc, bind } = load();
  const symbol = { toJSON() { throw Symbol('serialization failed'); } };
  const bare = { toJSON() { throw Object.create(null); } };
  const queued = geuneda.call('test.echo', symbol);
  geuneda.call('notice.list');

  bind();
  const direct = geuneda.call('test.echo', bare);

  assert.deepEqual(plain(sent.slice(1)), [
    { type: 'bound', doc, key: KEY },
    { type: 'call', id: '2', command: 'notice.list', args: {}, doc, key: KEY }
  ]);
  // '...' + symbol 은 TypeError 를 던진다. String(symbol) 은 되고, 프로토타입 없는 객체는 문자열로 바꿀 수 없다
  assert.deepEqual(plain(await queued), { ok: false, code: 12003, message: 'Command args could not be serialized: Symbol(serialization failed)' });
  assert.deepEqual(plain(await direct), { ok: false, code: 12003, message: 'Command args could not be serialized: unprintable error' });
});

test('a native bridge that throws an unprintable value resolves the call with unknown', async () => {
  const { geuneda, bind } = load({ failCalls: Object.create(null) });
  const queued = geuneda.call('notice.list');
  bind();
  const direct = geuneda.call('notice.refresh');

  assert.deepEqual(plain(await queued), { ok: false, code: 1, message: 'Could not send the command: unprintable error' });
  assert.equal((await direct).code, 1);
});

test('call outside the app resolves with bridgeUnavailable', async () => {
  const { geuneda, sent } = load({ transport: null });
  const result = await geuneda.call('notice.list');

  assert.equal(sent.length, 0);
  assert.equal(geuneda.isAvailable(), false);
  assert.deepEqual(plain(result), { ok: false, code: 12004, message: 'Not inside the app web view' });
});

test('events reach every handler even when one throws', () => {
  const { geuneda } = load();
  const received = [];
  const failing = () => { throw new Error('handler bug'); };
  const handler = (value) => received.push(value);

  geuneda.on('notice.changed', failing);
  geuneda.on('notice.changed', handler);
  geuneda.__receive({ type: 'event', name: 'notice.changed', value: 1 });
  geuneda.off('notice.changed', handler);
  geuneda.__receive({ type: 'event', name: 'notice.changed', value: 2 });

  assert.deepEqual(received, [1]);
});

test('names that exist on Object.prototype are ordinary names', async () => {
  const { geuneda, doc, bind } = load();
  const received = [];

  geuneda.__receive({ type: 'event', name: 'toString' });
  geuneda.__receive({ type: 'event', name: '__proto__' });
  geuneda.on('constructor', (value) => received.push(value));
  geuneda.on('__proto__', (value) => received.push(value));
  geuneda.__receive({ type: 'event', name: 'constructor', value: 1 });
  geuneda.__receive({ type: 'event', name: '__proto__', value: 2 });
  geuneda.off('hasOwnProperty', () => {});
  geuneda.__receive({ type: 'result', doc, id: 'constructor', ok: true, code: 0 });

  bind();
  assert.deepEqual(received, [1, 2]);
  const pending = geuneda.call('notice.list');
  geuneda.__receive({ type: 'result', doc, id: '1', ok: true, code: 0 });
  assert.equal((await pending).ok, true);
});

test('close and openUrl call the built-in commands', () => {
  const { geuneda, sent, doc, bind } = load();
  bind();
  geuneda.close();
  geuneda.openUrl('/terms');

  assert.deepEqual(plain(sent.slice(2)), [
    { type: 'call', id: '1', command: 'close', args: {}, doc, key: KEY },
    { type: 'call', id: '2', command: 'openUrl', args: { url: 'https://notice.example.com/terms' }, doc, key: KEY }
  ]);
});

test('links to another origin open in the system browser', () => {
  const { sent, bind, click } = load();
  bind();
  const external = click('https://www.youtube.com/watch?v=1');
  const sameOrigin = click('https://notice.example.com/ko/detail.html');
  const otherScheme = click('mailto:help@example.com');
  const withModifier = click('https://store.example.com/', { metaKey: true });

  assert.equal(external.defaultPrevented, true);
  assert.equal(sameOrigin.defaultPrevented, false);
  assert.equal(otherScheme.defaultPrevented, false);
  assert.equal(withModifier.defaultPrevented, false);
  const calls = sent.filter((message) => message.type === 'call');
  assert.deepEqual(plain(calls.map((message) => [message.command, message.args])), [['openUrl', { url: 'https://www.youtube.com/watch?v=1' }]]);
});

test('links navigate normally outside the app', () => {
  const { click } = load({ transport: null });
  assert.equal(click('https://www.youtube.com/').defaultPrevented, false);
});

test('loading twice keeps the first bridge', () => {
  const { geuneda, sent } = load();
  const context = vm.createContext({ window: { geuneda, location: {} }, document: {}, URL, Promise, JSON, String, Uint8Array, Math });
  vm.runInContext(source, context);

  assert.equal(context.window.geuneda, geuneda);
  assert.equal(sent.length, 1);
});
