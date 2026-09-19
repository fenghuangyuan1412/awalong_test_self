/*
 * 阿瓦隆联机权威服务器（Phase 4）
 * - 规则引擎直接复用 avalon-preview/core.js（零 DOM、UMD），服务端是唯一事实来源。
 * - 每个座位只收到自己的过滤视图（docs/NETPLAY.md §3）：他人身份永不下发（终局除外）。
 * - 协议信封 {t, ...payload}；见 docs/NETPLAY.md §2。
 * 运行：node server.js [--port 8080] [--timeout 90]   自测：node test/sim.js
 */
'use strict';

const path = require('path');
const http = require('http');
const crypto = require('crypto');
const { WebSocketServer } = require('ws');
const C = require(path.join(__dirname, '..', 'avalon-preview', 'core.js'));

const ARGS = parseArgs(process.argv.slice(2));
const PORT = ARGS.port || 8080;
const ACTION_TIMEOUT = ARGS.timeout === undefined ? 90 : Number(ARGS.timeout); // 秒；0=关闭
const ROOM_TTL_EMPTY = 30 * 60 * 1000;

const rooms = new Map(); // code -> room
const CODE_CHARS = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';

function parseArgs(argv) {
  const out = {};
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--port') out.port = Number(argv[++i]);
    else if (argv[i] === '--timeout') out.timeout = Number(argv[++i]);
  }
  return out;
}

function makeCode() {
  for (;;) {
    let s = '';
    for (let i = 0; i < 6; i++) s += CODE_CHARS[crypto.randomInt(CODE_CHARS.length)];
    if (!rooms.has(s)) return s;
  }
}

function createRoom() {
  const room = {
    code: makeCode(),
    seats: new Array(10).fill(null), // {ws,name,token}
    n: 0,
    started: false,
    state: null,
    timer: null,
    lastActivity: Date.now(),
    createdAt: Date.now(),
  };
  rooms.set(room.code, room);
  return room;
}

function activeSeats(room) {
  const out = [];
  for (let i = 0; i < room.seats.length; i++) if (room.seats[i]) out.push(i);
  return out;
}

function send(ws, msg) {
  if (ws && ws.readyState === ws.OPEN) ws.send(JSON.stringify(msg));
}

function toast(room, seat, error) {
  const s = room.seats[seat];
  if (s) send(s.ws, { t: 'toast', error });
}

/* ---------------- 视图过滤（docs/NETPLAY.md §3） ---------------- */

function actorSeatOf(st) {
  if (st.phase === 'propose') return st.leader;
  if (st.phase === 'vote') return st.voteOrder[st.votes.length];
  if (st.phase === 'mission') return st.missionOrder[st.missionVotes.length];
  if (st.phase === 'assassinate') return C.assassinOf(st);
  return -1;
}

function buildView(room, seat) {
  const st = room.state;
  const players = [];
  for (let i = 0; i < st.n; i++) players.push({ seat: i, name: st.players[i].name });
  const v = {
    t: 'state',
    seat,
    phase: st.phase,
    n: st.n,
    round: st.round,
    voteTrack: st.voteTrack,
    leader: st.leader,
    results: st.results,
    failCards: st.failCards,
    history: st.history,
    log: st.log.slice(-40),
    players,
    maxRejects: C.MAX_REJECTS,
    actorSeat: -1,
    waiting: room.pausedFor || null,
  };
  if (st.proposal) v.proposal = st.proposal;
  if (st.phase !== 'over') v.quest = C.questOf(st);
  if (st.phase === 'vote') {
    v.voteProgress = st.votes.length;
    v.voteOrder = st.voteOrder;
  }
  if (st.phase === 'mission') {
    v.missionProgress = st.missionVotes.length;
    v.missionOrder = st.missionOrder; // 队伍名单在提案通过时即公开
  }
  if (st.phase !== 'over') v.actorSeat = actorSeatOf(st);

  const me = { seat, name: st.players[seat].name };
  if (room.started) {
    me.role = st.players[seat].role;
    me.known = C.knownInfo(st, seat);
  }
  v.me = me;

  if (st.phase === 'over') {
    v.winner = st.winner;
    v.winReason = st.winReason;
    v.assassinated = st.assassinated;
    for (let i = 0; i < st.n; i++) {
      players[i].role = st.players[i].role; // 终局才公开身份
      players[i].team = C.ROLES[st.players[i].role].team;
    }
  }
  return v;
}

function broadcast(room) {
  const seats = activeSeats(room);
  for (const s of seats) send(room.seats[s].ws, buildView(room, s));
  armTimeout(room);
}

/* 未开局的花名册广播（联机大厅界面用） */
function lobbyMsg(room) {
  const players = [];
  for (let i = 0; i < room.seats.length; i++)
    players.push(room.seats[i] ? { seat: i, name: room.seats[i].name, online: !!room.seats[i].ws } : null);
  return { t: 'lobby', players, started: room.started };
}

function broadcastLobby(room) {
  const m = lobbyMsg(room); // send() 统一做 JSON 编码，这里不要提前 stringify
  for (const s of activeSeats(room)) send(room.seats[s].ws, m);
}

/* ---------------- 超时默认行动（§4） ---------------- */

function armTimeout(room) {
  if (room.timer) { clearTimeout(room.timer); room.timer = null; }
  if (!ACTION_TIMEOUT || !room.state || room.state.phase === 'over') return;
  const st = room.state;
  const actor = actorSeatOf(st);
  if (actor < 0) return;
  room.timer = setTimeout(() => autoAction(room, actor), ACTION_TIMEOUT * 1000);
}

function autoAction(room, seat) {
  if (!room.state || room.state.phase === 'over') return;
  const st = room.state;
  room.state.log.push('超时：' + st.players[seat].name + ' 执行默认行动');
  if (st.phase === 'propose') {
    const size = C.questOf(st).size;
    const members = [];
    for (let i = 0; i < st.n && members.length < size; i++) members.push(i);
    C.proposeTeam(st, members);
  } else if (st.phase === 'vote') {
    C.vote(st, false);
  } else if (st.phase === 'mission') {
    const actor = st.missionOrder[st.missionVotes.length];
    C.playMission(st, C.isGood(st.players[actor].role));
  } else if (st.phase === 'assassinate') {
    let target = -1;
    for (let i = 0; i < st.n; i++) if (C.isGood(st.players[i].role)) target = Math.random() < 0.5 ? i : target;
    if (target < 0) target = 0;
    C.assassinate(st, target);
  }
  broadcast(room);
}

/* ---------------- 消息处理 ---------------- */

function checkSeatOrder(room, seat) {
  // 座位必须连续加入（0,1,2,...）
  return seat === 0 || !!room.seats[seat - 1];
}

function handle(ws, msg) {
  const ctx = ws._avalon || {};
  switch (msg.t) {
    case 'create': {
      if (ctx.room) return send(ws, { t: 'toast', error: '你已在房间中' });
      const room = createRoom();
      joinSeat(ws, room, 0, msg.name);
      send(ws, { t: 'welcome', code: room.code, seat: 0, token: room.seats[0].token });
      return broadcastLobby(room);
    }
    case 'join': {
      if (ctx.room) return send(ws, { t: 'toast', error: '你已在房间中' });
      const room = rooms.get(String(msg.code || '').toUpperCase());
      if (!room) return send(ws, { t: 'toast', error: '房间不存在：' + msg.code });
      if (room.started) return send(ws, { t: 'toast', error: '对局已开始' });
      const seat = activeSeats(room).length; // 追加到下一个空位
      if (seat >= 10) return send(ws, { t: 'toast', error: '房间已满' });
      if (!checkSeatOrder(room, seat)) return send(ws, { t: 'toast', error: '座位异常' });
      joinSeat(ws, room, seat, msg.name);
      send(ws, { t: 'welcome', code: room.code, seat, token: room.seats[seat].token });
      return broadcastLobby(room);
    }
    case 'reconnect': {
      const room = rooms.get(String(msg.code || '').toUpperCase());
      if (!room) return send(ws, { t: 'toast', error: '房间不存在' });
      const seat = Number(msg.seat);
      const s = room.seats[seat];
      if (!s || s.token !== msg.token) return send(ws, { t: 'toast', error: '重连凭证无效' });
      if (s.ws && s.ws !== ws) s.ws.close();
      s.ws = ws;
      ws._avalon = { room, seat };
      room.pausedFor = null;
      send(ws, { t: 'welcome', code: room.code, seat, token: s.token, reconnected: true });
      return broadcast(room); // 重连者立刻拿到当前视图，其他人解除等待提示
    }
    case 'start': {
      const { room, seat } = requireActor(ctx);
      if (!room) return;
      if (seat !== 0) return toast(room, seat, '只有创建者能开局');
      if (room.started) return toast(room, seat, '已开局');
      const seats = activeSeats(room);
      if (seats.length < 5 || seats.length > 10) return toast(room, seat, '需要 5–10 人，当前 ' + seats.length);
      const names = seats.map((i) => room.seats[i].name);
      try {
        room.state = C.createGame(names, msg.seed == null ? undefined : Number(msg.seed));
      } catch (e) {
        return toast(room, seat, String(e.message || e));
      }
      room.started = true;
      return broadcast(room);
    }
    case 'propose': {
      const { room, seat } = requireActor(ctx);
      if (!room) return;
      const st = room.state;
      if (!st || st.phase !== 'propose' || seat !== st.leader) return toast(room, seat, '当前不该你组队');
      const r = C.proposeTeam(st, (msg.members || []).map(Number));
      if (!r.ok) return toast(room, seat, r.error);
      return broadcast(room);
    }
    case 'vote': {
      const { room, seat } = requireActor(ctx);
      if (!room) return;
      const st = room.state;
      if (!st || st.phase !== 'vote' || st.voteOrder[st.votes.length] !== seat) return toast(room, seat, '当前不该你投票');
      const r = C.vote(st, !!msg.approve);
      if (!r.ok) return toast(room, seat, r.error);
      return broadcast(room);
    }
    case 'mission': {
      const { room, seat } = requireActor(ctx);
      if (!room) return;
      const st = room.state;
      if (!st || st.phase !== 'mission' || st.missionOrder[st.missionVotes.length] !== seat) return toast(room, seat, '当前不该你出任务票');
      const r = C.playMission(st, !!msg.success);
      if (!r.ok) return toast(room, seat, r.error);
      return broadcast(room);
    }
    case 'assassinate': {
      const { room, seat } = requireActor(ctx);
      if (!room) return;
      const st = room.state;
      if (!st || st.phase !== 'assassinate' || C.assassinOf(st) !== seat) return toast(room, seat, '当前不该你刺杀');
      const r = C.assassinate(st, Number(msg.target));
      if (!r.ok) return toast(room, seat, r.error);
      return broadcast(room);
    }
    case 'restart': {
      const { room, seat } = requireActor(ctx);
      if (!room) return;
      if (seat !== 0) return toast(room, seat, '只有创建者能重开');
      const names = activeSeats(room).map((i) => room.seats[i].name);
      try {
        room.state = C.createGame(names, undefined);
      } catch (e) {
        return toast(room, seat, String(e.message || e));
      }
      return broadcast(room);
    }
    case 'leave': {
      const { room, seat } = ctx;
      if (room) leaveSeat(room, seat);
      return send(ws, { t: 'toast', error: null });
    }
    default:
      return send(ws, { t: 'toast', error: '未知消息类型: ' + msg.t });
  }
}

function requireActor(ctx) {
  if (!ctx.room || ctx.seat == null) {
    send(ctx.ws, { t: 'toast', error: '未加入房间' });
    return { room: null };
  }
  return ctx;
}

function joinSeat(ws, room, seat, name) {
  const token = crypto.randomBytes(12).toString('hex');
  room.seats[seat] = { ws, name: String(name || '').trim().slice(0, 8) || ('玩家' + (seat + 1)), token };
  ws._avalon = { room, seat, ws };
  room.lastActivity = Date.now();
}

function leaveSeat(room, seat) {
  const s = room.seats[seat];
  if (!s) return;
  room.lastActivity = Date.now();
  if (!room.started) {
    // 未开局：离座即释放，收拢座位并同步平移后各连接记的座位号
    s.ws = null;
    room.seats[seat] = null;
    const keep = activeSeats(room).map((i) => room.seats[i]);
    room.seats.fill(null);
    keep.forEach((entry, i) => {
      room.seats[i] = entry;
      if (entry.ws && entry.ws._avalon) entry.ws._avalon.seat = i;
    });
    return broadcastLobby(room);
  }
  s.ws = null;
  if (room.state && room.state.phase !== 'over') room.pausedFor = seat; // 开局后等待重连
}

/* ---------------- 网络 ---------------- */

const server = http.createServer((req, res) => {
  res.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' });
  res.end('avalon-server ok, rooms=' + rooms.size + '\n');
});
const wss = new WebSocketServer({ server });

wss.on('connection', (ws) => {
  ws.on('message', (buf) => {
    let msg;
    try { msg = JSON.parse(buf.toString('utf8')); } catch (e) { return send(ws, { t: 'toast', error: 'JSON 解析失败' }); }
    try { handle(ws, msg); } catch (e) { console.error('handler error:', e); send(ws, { t: 'toast', error: '服务器内部错误' }); }
  });
  ws.on('close', () => {
    const ctx = ws._avalon || {};
    if (ctx.room && ctx.room.seats[ctx.seat] && ctx.room.seats[ctx.seat].ws === ws) {
      leaveSeat(ctx.room, ctx.seat);
      if (ctx.room.started) broadcast(ctx.room);
    }
  });
});

setInterval(() => {
  const now = Date.now();
  for (const [code, room] of rooms) {
    const empty = activeSeats(room).every((i) => !room.seats[i].ws);
    if (empty && now - room.lastActivity > ROOM_TTL_EMPTY) {
      if (room.timer) clearTimeout(room.timer);
      rooms.delete(code);
      console.log('回收房间', code);
    }
  }
}, 60 * 1000).unref();

if (require.main === module) {
  server.listen(PORT, () => console.log('[avalon-server] listening on :' + PORT + '  timeout=' + ACTION_TIMEOUT + 's'));
}

module.exports = { server, wss, rooms };
