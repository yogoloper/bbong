// WebGL 전용 WebSocket 브리지(단일 연결). WsClient가 P/Invoke로 호출.
// 브라우저 WS는 헤더 설정 불가 → 토큰은 URL 쿼리(access_token)로 전달된다.
mergeInto(LibraryManager.library, {
  BbongWsConnect: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    if (!Module.bbongWs) {
      Module.bbongWs = {};
    }
    var ctx = Module.bbongWs;
    ctx.queue = [];
    ctx.state = 0; // 0=connecting, 1=open, 2=closed, 3=error
    try {
      var ws = new WebSocket(url);
      ctx.socket = ws;
      ws.onopen = function () { ctx.state = 1; };
      ws.onmessage = function (e) { ctx.queue.push(e.data); };
      ws.onerror = function () { ctx.state = 3; };
      ws.onclose = function () { if (ctx.state !== 3) ctx.state = 2; };
    } catch (e) {
      ctx.state = 3;
    }
  },

  BbongWsState: function () {
    return Module.bbongWs ? Module.bbongWs.state : 2;
  },

  BbongWsSend: function (msgPtr) {
    var ctx = Module.bbongWs;
    if (ctx && ctx.socket && ctx.state === 1) {
      ctx.socket.send(UTF8ToString(msgPtr));
    }
  },

  // 수신 큐에서 한 건 꺼내 힙 문자열로 반환(호출측이 BbongWsFree). 없으면 0.
  BbongWsReceive: function () {
    var ctx = Module.bbongWs;
    if (!ctx || !ctx.queue || ctx.queue.length === 0) {
      return 0;
    }
    var msg = ctx.queue.shift();
    var size = lengthBytesUTF8(msg) + 1;
    var buf = _malloc(size);
    stringToUTF8(msg, buf, size);
    return buf;
  },

  BbongWsFree: function (ptr) {
    _free(ptr);
  },

  BbongWsClose: function () {
    var ctx = Module.bbongWs;
    if (ctx && ctx.socket) {
      try { ctx.socket.close(); } catch (e) { }
      ctx.socket = null;
      ctx.state = 2;
    }
  }
});
