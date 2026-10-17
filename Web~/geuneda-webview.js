/*
 * Geuneda WebView 브리지 (com.geuneda.webview 0.2.0). MIT License.
 *
 * 페이지 스크립트보다 먼저 <script src="geuneda-webview.js"></script> 로(async·defer 없이) 넣습니다.
 * 이 파일이 브리지 준비를 알려야 Unity 의 웹뷰 열기가 성공합니다.
 *
 *   geuneda.call(command, args) -> Promise<{ ok, code, message, value }>   거절(reject)하지 않음
 *   geuneda.on(name, handler) / geuneda.off(name, handler)                  Unity 가 보낸 이벤트
 *   geuneda.close()                                                         웹뷰 닫기
 *   geuneda.openUrl(url)                                                    외부 브라우저로 열기
 *   geuneda.isAvailable()                                                   일반 브라우저에서는 false
 *
 * 다른 출처로 가는 링크는 외부 브라우저로 엽니다(웹뷰는 허용한 출처 밖으로 이동하지 않음).
 *
 * 연결: 문서마다 무작위 ID(doc)를 만들어 준비를 알리면 Unity 가 그 열기의 키를 EvaluateJS 로 넘깁니다. EvaluateJS 는 메인 프레임에서만
 * 실행되므로 다른 출처의 iframe 은 키를 알 수 없어 명령을 보낼 수 없습니다. 키를 받기 전의 명령은 기다렸다가 보내고, 결과는 같은 doc 의 것만 받습니다
 * (같은 웹뷰에서 페이지를 옮기거나 새로고침해도 이전 문서의 결과가 섞이지 않음).
 */
(function (window, document) {
  'use strict';

  if (window.geuneda) {
    return;
  }

  var codes = {
    unknown: 1,
    loadFailed: 12001,
    unknownCommand: 12002,
    invalidArguments: 12003,
    bridgeUnavailable: 12004
  };

  // 프로토타입이 없는 사전: 'constructor', '__proto__' 같은 이름도 보통 키다
  var pending = Object.create(null);
  var listeners = Object.create(null);
  var queue = [];
  var nextId = 1;
  var doc = newDocId();
  var key = null;

  function newDocId() {
    var bytes = new Uint8Array(16);

    if (window.crypto && window.crypto.getRandomValues) {
      window.crypto.getRandomValues(bytes);
    } else {
      for (var i = 0; i < bytes.length; i++) {
        bytes[i] = Math.floor(Math.random() * 256);
      }
    }

    var text = '';

    for (var j = 0; j < bytes.length; j++) {
      text += (bytes[j] + 256).toString(16).substring(1);
    }

    return text;
  }

  // gree unity-webview 의 전달 경로
  function transport() {
    // iOS·macOS(WKWebView): gree 가 받은 글을 URL 디코딩하므로(UnEscapeURL, '+' 는 공백이 됨) 인코딩해 보낸다
    var handlers = window.webkit && window.webkit.messageHandlers;

    if (handlers && handlers.unityControl) {
      return function (text) { handlers.unityControl.postMessage(encodeURIComponent(text)); };
    }

    // Android: JavaScript 인터페이스 window.Unity, 디코딩하지 않음
    var unity = window.Unity;

    if (unity && typeof unity.call === 'function') {
      return function (text) { unity.call(text); };
    }

    return null;
  }

  function serialize(message) {
    message.doc = doc;

    if (key !== null) {
      message.key = key;
    }

    return JSON.stringify(message);
  }

  function send(message) {
    var post = transport();

    if (!post) {
      return false;
    }

    post(serialize(message));
    return true;
  }

  // 명령 하나를 보낸다. 인자를 JSON 으로 만들 수 없거나(순환 참조, BigInt) 전달 경로가 던지면 그 요청만 실패 결과로 끝낸다.
  // 거절하지 않고, 키를 받기 전에 쌓인 대기열의 뒤 요청도 계속 보낸다
  function deliver(message) {
    var post = transport();
    var text;

    try {
      text = serialize(message);
    } catch (error) {
      fail(message.id, codes.invalidArguments, 'Command args could not be serialized: ' + describe(error));
      return;
    }

    try {
      post(text);
    } catch (error) {
      fail(message.id, codes.unknown, 'Could not send the command: ' + describe(error));
    }
  }

  // 던진 값은 Error 가 아닐 수 있다(Symbol, 문자열로 바꿀 수 없는 객체). 설명을 만들다 다시 던지지 않는다
  function describe(error) {
    try {
      return String(error);
    } catch (ignored) {
      return 'unprintable error';
    }
  }

  function fail(id, code, text) {
    var resolve = pending[id];

    if (!resolve) {
      return;
    }

    delete pending[id];
    resolve({ ok: false, code: code, message: text });
  }

  function call(command, args) {
    return new Promise(function (resolve) {
      if (!transport()) {
        resolve({ ok: false, code: codes.bridgeUnavailable, message: 'Not inside the app web view' });
        return;
      }

      var id = String(nextId++);
      var message = { type: 'call', id: id, command: command, args: args || {} };
      pending[id] = resolve;

      if (key === null) {
        queue.push(message);
      } else {
        deliver(message);
      }
    });
  }

  function bind(message) {
    if (message.doc !== doc || key !== null || typeof message.key !== 'string') {
      return;
    }

    key = message.key;
    send({ type: 'bound' });

    var waiting = queue;
    queue = [];

    for (var i = 0; i < waiting.length; i++) {
      deliver(waiting[i]);
    }
  }

  function receive(message) {
    if (!message) {
      return;
    }

    if (message.type === 'bind') {
      bind(message);
      return;
    }

    if (message.type === 'result') {
      if (message.doc !== doc) {
        return;
      }

      var resolve = pending[message.id];

      if (!resolve) {
        return;
      }

      delete pending[message.id];
      resolve({ ok: message.ok === true, code: message.code, message: message.message, value: message.value });
      return;
    }

    if (message.type === 'event') {
      var handlers = (listeners[message.name] || []).slice();

      for (var i = 0; i < handlers.length; i++) {
        try {
          handlers[i](message.value);
        } catch (error) {
          // 처리기 하나의 예외가 나머지를 막지 않게, Unity 의 EvaluateJS 호출 밖에서 다시 던져 콘솔에 남김
          setTimeout(function (e) { return function () { throw e; }; }(error), 0);
        }
      }
    }
  }

  function on(name, handler) {
    (listeners[name] = listeners[name] || []).push(handler);
  }

  function off(name, handler) {
    var handlers = listeners[name];

    if (!handlers) {
      return;
    }

    var index = handlers.indexOf(handler);

    if (index >= 0) {
      handlers.splice(index, 1);
    }
  }

  function close() {
    call('close');
  }

  function openUrl(url) {
    return call('openUrl', { url: new URL(url, window.location.href).href });
  }

  function isAvailable() {
    return transport() !== null;
  }

  document.addEventListener('click', function (event) {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || !isAvailable()) {
      return;
    }

    var link = event.target && event.target.closest ? event.target.closest('a[href]') : null;

    if (!link) {
      return;
    }

    var url;

    try {
      url = new URL(link.href, window.location.href);
    } catch (error) {
      return;
    }

    if ((url.protocol === 'http:' || url.protocol === 'https:') && url.origin !== window.location.origin) {
      event.preventDefault();
      openUrl(url.href);
    }
  }, false);

  window.geuneda = {
    codes: codes,
    call: call,
    on: on,
    off: off,
    close: close,
    openUrl: openUrl,
    isAvailable: isAvailable,
    __receive: receive
  };

  send({ type: 'ready' });
})(window, document);
