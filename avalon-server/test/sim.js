/*
 * Phase 4 验收 gate 1（docs/NETPLAY.md §7）：
 * 5 个模拟 WS 客户端跑通一整局（含刺杀），断言：
 *   1) 与 core.js 直跑同种子结果一致（parity）
 *   2) 全程任何座位的视图里都不含他人身份（终局揭示除外）（privacy）
 *   3) 中途断线重连、越权动作被拒、超时默认行动
 *   4) 未开局离座收拢（leaveSeat 回归）
 * 运行：node test/sim.js
 */
'use strict';

const path = require('path');

// 让服务端把默认行动超时设为 2 秒（parseArgs 在 require 时读取 process.argv）
process.argv = process.argv.slice(0, 2).concat(['--timeout', '2']);

const C = require(path.join(__dirname, '..', '..', 'avalon-preview', 'core.js'));
const WebSocket = require('ws');
const { server, rooms } = require('../server.js');

const SEED = 42;
const NAMES = ['阿', '贝', '西', '德', '伊'];
const failures = [];

function assert(cond, msg) {
  if (cond) { console.log('  ok  ' + msg); return; }
  failures.push(msg);
  console.error('  FAIL ' + msg);
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

class Client {
  constructor(url, i) {
    this.i = i;
    this.inbox = [];
    this.stateWaiters = [];
    this.ws = new WebSocket(url);
    this.ws.on('message', (buf) => this.onMsg(JSON.parse(buf.toString('utf8'))));
  }
  onMsg(m) {
    if (m.t === 'welcome') {
      this.welcome = m;
      this.seat = m.seat;
      this.token = m.token;
      this.code = m.code;
    } else if (m.t === 'state') {
      checkPrivacy(m, this);
      this.last = m;
      const keep = [];
      while (this.stateWaiters.length) {
        const w = this.stateWaiters.shift();
        if (w.pred(m)) w.resolve(m); else keep.push(w);
      }
      this.stateWaiters = keep;
    } else {
      this.lastToast = m;
    }
    this.inbox.push(m);
  }
  send(obj) { this.ws.send(JSON.stringify(obj)); }
  async waitMsg(pred, what, ms = 8000) {
    const t0 = Date.now();
    for (;;) {
      const hit = this.inbox.find(pred);
      if (hit) { this.inbox.splice(this.inbox.indexOf(hit), 1); return hit; }
      if (Date.now() - t0 > ms) throw new Error('超时等待: ' + what);
      await sleep(40);
    }
  }
  nextState(prev, what) {
    return this.waitMsg((m) => m.t === 'state' && m !== prev, 'state ' + what);
  }
}

// 全程隐私断言：非终局视图不得包含任何他人身份；me.role 必须与服务端真值一致
function checkPrivacy(v, client) {
  const tag = 'seat' + client.seat;
  const room = rooms.get(client.code);
  if (v.phase !== 'over') {
    for (const p of v.players) {
      if (p.role !== undefined || p.team !== undefined) {
        failures.push(tag + ' 非终局视图泄露身份: seat' + p.seat);
      }
    }
  } else if (room) {
    const truth = room.state.players;
    v.players.forEach((p, i) => {
      if (p.role !== truth[i].role) failures.push(tag + ' 终局身份与真值不符: seat' + i);
    });
  }
  if (room && room.started && v.me && room.state.players[v.seat]
      && v.me.role !== undefined && v.me.role !== room.state.players[v.seat].role) {
    failures.push(tag + ' 自己的身份下发错误');
  }
}

// 参考直跑：与网络驱动完全相同的确定性策略
function referenceRun() {
  const st = C.createGame(NAMES.slice(), SEED);
  for (let guard = 0; guard < 500; guard++) {
    if (st.phase === 'over') break;
    if (st.phase === 'propose') {
      const size = C.questOf(st).size;
      const members = [];
      for (let i = 0; i < size; i++) members.push(i);
      C.proposeTeam(st, members);
    } else if (st.phase === 'vote') {
      C.vote(st, true);
    } else if (st.phase === 'mission') {
      C.playMission(st, true);
    } else if (st.phase === 'assassinate') {
      C.assassinate(st, (C.assassinOf(st) + 1) % st.n);
    }
  }
  return st;
}

async function main() {
  console.log('== avalon-server 模拟对局测试 ==');
  await new Promise((r) => server.listen(0, r));
  const url = 'ws://127.0.0.1:' + server.address().port;

  // ---- 连接 5 客户端，建房进座 ----
  const c = [];
  for (let i = 0; i < 5; i++) c.push(new Client(url, i));
  await Promise.all(c.map((x) => new Promise((r) => x.ws.on('open', r))));
  c[0].send({ t: 'create', name: NAMES[0] });
  await c[0].waitMsg((m) => m.t === 'welcome', 'welcome0');
  assert(c[0].seat === 0, '创建者落在座位 0');
  const code = c[0].code;
  for (let i = 1; i < 5; i++) c[i].send({ t: 'join', code, name: NAMES[i] });
  for (let i = 1; i < 5; i++) {
    await c[i].waitMsg((m) => m.t === 'welcome', 'welcome' + i);
    assert(c[i].seat === i, '玩家 ' + i + ' 落在座位 ' + i);
  }
  const lb = await c[4].waitMsg((m) => m.t === 'lobby' && m.players.filter(Boolean).length === 5, 'lobby 名单');
  assert(lb.players.filter(Boolean).every((p, i) => p.name === NAMES[i]), '大厅花名册广播 5 人齐全');

  // ---- 开局（同种子），随后立刻做越权/断连测试 ----
  const ref = referenceRun();
  c[0].send({ t: 'start', seed: SEED });
  await c[0].waitMsg((m) => m.t === 'state', 'first state');

  const v0 = c[0].last;
  const actor = v0.actorSeat;
  const outsider = c[(actor + 1) % 5];
  outsider.send({ t: 'propose', members: [0, 1] });
  const tt = await outsider.waitMsg((m) => m.t === 'toast' && m.error, 'toast');
  assert(/不该你组队/.test(tt.error), '越权组队被拒: ' + tt.error);

  // 断线 → 其他视图出现 waiting → 重连恢复
  const disc = actor === 4 ? c[3] : c[4];
  const discSeat = disc.seat;
  disc.ws.close();
  const paused = await c[0].waitMsg((m) => m.t === 'state' && m.waiting === discSeat, 'waiting');
  assert(paused.waiting === discSeat, '座位 ' + discSeat + ' 断线后房间进入等待（waiting=' + paused.waiting + '）');
  disc.ws = new WebSocket(url);
  await new Promise((r) => disc.ws.on('open', r));
  disc.ws.on('message', (buf) => disc.onMsg(JSON.parse(buf.toString('utf8'))));
  disc.send({ t: 'reconnect', code, seat: discSeat, token: disc.token });
  await disc.waitMsg((m) => m.t === 'welcome' && m.reconnected, 'reconnect welcome');
  await disc.waitMsg((m) => m.t === 'state', 'reconnect state');
  assert(disc.last.phase === v0.phase, '重连后收到当前对局视图（phase=' + disc.last.phase + '）');

  // ---- 超时默认行动：第一个组队阶段不发送，等 2s 让服务端自动组队（策略与手动一致）----
  const auto = await c[0].waitMsg(
    (m) => m.t === 'state' && m.phase !== v0.phase, 'timeout auto-action', 10000);
  assert(auto.log.some((l) => l.indexOf('超时') >= 0), '超时后服务端执行了默认组队');

  // ---- 驱动整局（纯视图驱动，与参考策略一致）----
  let v = c[0].last;
  let steps = 0;
  while (v.phase !== 'over' && steps < 300) {
    steps++;
    const a = v.actorSeat;
    if (v.phase === 'propose') {
      c[a].send({ t: 'propose', members: Array.from({ length: v.quest.size }, (_, i) => i) });
    } else if (v.phase === 'vote') {
      c[v.voteOrder[v.voteProgress]].send({ t: 'vote', approve: true });
    } else if (v.phase === 'mission') {
      c[a].send({ t: 'mission', success: true });
    } else if (v.phase === 'assassinate') {
      c[a].send({ t: 'assassinate', target: (a + 1) % v.n });
    } else break;
    v = await c[0].nextState(v, 'step' + steps);
  }
  assert(v.phase === 'over', '整局在 ' + steps + ' 步内结束（winner=' + v.winner + '）');

  // ---- parity：终局视图 vs 直跑 ----
  assert(v.winner === ref.winner, 'winner 一致: ' + v.winner);
  assert(v.winReason === ref.winReason, 'winReason 一致');
  assert(v.assassinated === ref.assassinated, 'assassinated 一致: seat ' + v.assassinated);
  assert(JSON.stringify(v.results) === JSON.stringify(ref.results), 'results 一致');
  assert(JSON.stringify(v.failCards) === JSON.stringify(ref.failCards), 'failCards 一致');
  assert(v.history.length === ref.history.length,
    'history 次数一致: ' + ref.history.length + ' vs ' + v.history.length);
  for (let i = 0; i < v.history.length; i++) {
    const a1 = v.history[i], b1 = ref.history[i];
    assert(a1.approved === b1.approved && JSON.stringify(a1.members) === JSON.stringify(b1.members),
      'history[' + i + '] 一致');
  }

  // ---- 未开局离座收拢（回归 seatsKeep bug）----
  const lobby = [new Client(url, 0), new Client(url, 1), new Client(url, 2)];
  await Promise.all(lobby.map((x) => new Promise((r) => x.ws.on('open', r))));
  lobby[0].send({ t: 'create', name: '甲' });
  await lobby[0].waitMsg((m) => m.t === 'welcome', 'lobby0');
  lobby[1].send({ t: 'join', code: lobby[0].code, name: '乙' });
  lobby[2].send({ t: 'join', code: lobby[0].code, name: '丙' });
  await lobby[2].waitMsg((m) => m.t === 'welcome', 'lobby2');
  lobby[1].ws.close();
  await sleep(300);
  const lr = rooms.get(lobby[0].code);
  assert(lr.seats[0] && lr.seats[0].name === '甲', '收拢后座位0=甲');
  assert(lr.seats[1] && lr.seats[1].name === '丙', '收拢后座位1=丙（原座位2平移）');
  assert(lr.seats[2] === null, '座位2 已空');
  assert(lr.seats[1].token === lobby[2].token && lr.seats[1].ws, '平移座位保留 token 与连接');

  server.close();
  for (const x of c.concat(lobby)) { try { x.ws.close(); } catch (e) { /* noop */ } }
}

main().catch((e) => { failures.push('异常: ' + (e.stack || e)); console.error(e); })
  .finally(async () => {
    await sleep(200);
    console.log('\n== 结果: ' + (failures.length ? failures.length + ' 项失败' : '全部通过') + ' ==');
    failures.forEach((f) => console.error('- ' + f));
    process.exit(failures.length ? 1 : 0);
  });
