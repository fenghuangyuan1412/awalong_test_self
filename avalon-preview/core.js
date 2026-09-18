/*
 * 阿瓦隆规则引擎（预览版）· 纯 JS，零 DOM 依赖
 *
 * 架构约束（对应项目提示词的分层要求）：
 *  - 本文件只做规则建模（角色、组队、投票、任务、刺杀、胜负），不接触任何 UI。
 *  - 角色配置、任务人数表全部数据驱动，禁止在流程代码里硬编码规则数字。
 *  - 后续 Unity 版直接按本文件结构移植为 C# Core 层。
 *
 * 规则依据：The Resistance: Avalon 官方规则书（Indie Boards & Cards）
 *  - 任务人数表 / 第4轮失败票阈值 / 5 连否决坏人胜 / 角色配置表 均按官方规则书实现。
 */
(function (root, factory) {
  if (typeof module !== 'undefined' && module.exports) module.exports = factory();
  else root.AvalonCore = factory();
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  /* ---------------- 数据：角色 ---------------- */
  var ROLES = {
    merlin:   { id: 'merlin',   name: '梅林',           team: 'good', icon: '🔮', desc: '能看到除莫德雷德以外的所有坏人。隐藏好自己，别被刺客认出。' },
    percival: { id: 'percival', name: '派西维尔',       team: 'good', icon: '🛡️', desc: '看到“梅林与莫甘娜”两位候选人，但无法分辨谁是谁。保护梅林。' },
    servant:  { id: 'servant',  name: '亚瑟的忠臣',     team: 'good', icon: '⚔️', desc: '忠诚的圆桌骑士。好人只能打出任务成功。' },
    assassin: { id: 'assassin', name: '刺客',           team: 'evil', icon: '🗡️', desc: '好人赢下三轮任务后，由你指认梅林：指对则坏人直接获胜。' },
    morgana:  { id: 'morgana',  name: '莫甘娜',         team: 'evil', icon: '🌙', desc: '在派西维尔眼中与梅林一模一样。负责误导好人。' },
    mordred:  { id: 'mordred',  name: '莫德雷德',       team: 'evil', icon: '☠️', desc: '对梅林隐身：梅林看不到你。' },
    oberon:   { id: 'oberon',   name: '奥伯伦',         team: 'evil', icon: '👁️', desc: '孤狼：你看不到同伴，同伴也看不到你。' },
    minion:   { id: 'minion',   name: '莫德雷德的爪牙', team: 'evil', icon: '🩸', desc: '普通坏人，与同伴互相认识（奥伯伦除外）。' }
  };

  /* ---------------- 数据：各人数角色配置（官方规则书） ---------------- */
  var SETUP = {
    5:  ['merlin', 'percival', 'servant', 'assassin', 'morgana'],
    6:  ['merlin', 'percival', 'servant', 'servant', 'assassin', 'morgana'],
    7:  ['merlin', 'percival', 'servant', 'servant', 'assassin', 'morgana', 'minion'],
    8:  ['merlin', 'percival', 'servant', 'servant', 'servant', 'assassin', 'morgana', 'minion'],
    9:  ['merlin', 'percival', 'servant', 'servant', 'servant', 'servant', 'assassin', 'morgana', 'minion'],
    10: ['merlin', 'percival', 'servant', 'servant', 'servant', 'servant', 'assassin', 'morgana', 'mordred', 'oberon']
  };

  /* ---------------- 数据：任务人数表（官方规则书） ----------------
   * size: 上场人数；twoFails: 该轮需 2 张失败票才算任务失败（7人+ 的第4轮）
   */
  var QUESTS = {
    5:  [{ size: 2 }, { size: 3 }, { size: 2 }, { size: 3 }, { size: 3 }],
    6:  [{ size: 2 }, { size: 3 }, { size: 4 }, { size: 3 }, { size: 4 }],
    7:  [{ size: 2 }, { size: 3 }, { size: 3 }, { size: 4, twoFails: true }, { size: 4 }],
    8:  [{ size: 3 }, { size: 4 }, { size: 4 }, { size: 5, twoFails: true }, { size: 5 }],
    9:  [{ size: 3 }, { size: 4 }, { size: 4 }, { size: 5, twoFails: true }, { size: 5 }],
    10: [{ size: 3 }, { size: 4 }, { size: 4 }, { size: 5, twoFails: true }, { size: 5 }]
  };

  var MAX_REJECTS = 5; // 连续否决 5 次组队 → 坏人直接获胜

  /* ---------------- 工具 ---------------- */
  function mulberry32(seed) {
    var a = seed >>> 0;
    return function () {
      a |= 0; a = (a + 0x6D2B79F5) | 0;
      var t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  function shuffle(arr, rng) {
    var a = arr.slice();
    for (var i = a.length - 1; i > 0; i--) {
      var j = Math.floor(rng() * (i + 1));
      var t = a[i]; a[i] = a[j]; a[j] = t;
    }
    return a;
  }

  function isEvil(roleId) { return ROLES[roleId].team === 'evil'; }
  function isGood(roleId) { return ROLES[roleId].team === 'good'; }

  function err(msg) { return { ok: false, error: msg }; }

  /* ---------------- 建局 ---------------- */
  function createGame(names, seed) {
    var n = names.length;
    if (!SETUP[n]) throw new Error('人数必须在 5–10 人，当前：' + n);
    var rng = mulberry32(seed == null ? (Math.random() * 2147483647) | 0 : seed);
    var pool = shuffle(SETUP[n], rng);
    var players = [];
    for (var i = 0; i < n; i++) {
      players.push({ name: (names[i] || '').trim() || ('玩家' + (i + 1)), role: pool[i] });
    }
    var st = {
      n: n,
      players: players,
      rngSeed: (seed == null ? null : seed),
      leader: Math.floor(rng() * n),        // 随机起始队长
      round: 0,                             // 当前任务轮 0..4
      voteTrack: 0,                         // 本轮任务连续被否决次数
      phase: 'propose',                     // propose | vote | mission | assassinate | over
      proposal: null,                       // { leader, members:[座位号] }
      voteOrder: null, votes: null,         // 全员投票（顺序 = 从队长开始顺时针）
      missionOrder: null, missionVotes: null, // 队员任务票
      results: [null, null, null, null, null], // 'success' | 'fail'（公开）
      failCards: [null, null, null, null, null], // 每轮失败票张数（公开）
      history: [],                          // 每次组队提案的公开记录
      winner: null, winReason: null,        // 'good' | 'evil'
      assassinated: null,
      log: []
    };
    st.log.push('游戏开始：' + n + ' 人局，起始队长：' + players[st.leader].name);
    return st;
  }

  function questOf(st, round) {
    return QUESTS[st.n][(round == null ? st.round : round)];
  }

  /* ---------------- 私密视角 ---------------- */
  // 返回该玩家能看到的信息条目：{ idx, name, kind }
  // kind: 'evil'（确认坏人）| 'merlinOrMorgana'（二选一）| 'ally'（坏同伴）
  function knownInfo(st, idx) {
    var me = st.players[idx].role;
    var out = [];
    for (var i = 0; i < st.n; i++) {
      if (i === idx) continue;
      var role = st.players[i].role;
      if (me === 'merlin') {
        if (isEvil(role) && role !== 'mordred') out.push({ idx: i, name: st.players[i].name, kind: 'evil' });
      } else if (me === 'percival') {
        if (role === 'merlin' || role === 'morgana') out.push({ idx: i, name: st.players[i].name, kind: 'merlinOrMorgana' });
      } else if (isEvil(me) && me !== 'oberon') {
        if (isEvil(role) && role !== 'oberon') out.push({ idx: i, name: st.players[i].name, kind: 'ally' });
      }
    }
    return out;
  }

  /* ---------------- 组队提案 ---------------- */
  function proposeTeam(st, members) {
    if (st.phase !== 'propose') return err('当前阶段不能组队');
    var q = questOf(st);
    if (!members || members.length !== q.size) return err('需要选择 ' + q.size + ' 名队员');
    var seen = {};
    for (var i = 0; i < members.length; i++) {
      var m = members[i];
      if (m < 0 || m >= st.n) return err('队员座位号无效');
      if (seen[m]) return err('队员重复');
      seen[m] = true;
    }
    if (!seen[st.leader]) return err('队长必须在队伍中');
    var sorted = members.slice().sort(function (a, b) { return a - b; });
    st.proposal = { leader: st.leader, members: sorted };
    st.phase = 'vote';
    st.votes = [];
    st.voteOrder = [];
    for (var k = 0; k < st.n; k++) st.voteOrder.push((st.leader + k) % st.n);
    st.log.push('第' + (st.round + 1) + '轮 第' + (st.voteTrack + 1) + '次组队：' +
      st.players[st.leader].name + ' 提名 ' + sorted.map(function (i) { return st.players[i].name; }).join('、'));
    return { ok: true };
  }

  /* ---------------- 全员投票（同意/反对） ----------------
   * 返回 { ok, done, approved?, gameOver? }
   * 规则：同意票严格过半才算通过（平局即否决）。
   */
  function vote(st, approve) {
    if (st.phase !== 'vote') return err('当前不是投票阶段');
    st.votes.push(!!approve);
    if (st.votes.length < st.n) return { ok: true, done: false };

    var yes = 0, i;
    for (i = 0; i < st.votes.length; i++) if (st.votes[i]) yes++;
    var no = st.n - yes;
    var approved = yes * 2 > st.n;
    st.history.push({
      round: st.round, attempt: st.voteTrack, leader: st.proposal.leader,
      members: st.proposal.members.slice(), approved: approved, yes: yes, no: no,
      votes: st.votes.slice()
    });
    st.log.push('投票：同意 ' + yes + ' / 反对 ' + no + ' → ' + (approved ? '通过' : '被否决'));

    if (approved) {
      st.phase = 'mission';
      st.missionVotes = [];
      st.missionOrder = st.proposal.members.slice().sort(function (a, b) { return a - b; });
      return { ok: true, done: true, approved: true };
    }

    st.voteTrack++;
    if (st.voteTrack >= MAX_REJECTS) {
      st.winner = 'evil';
      st.winReason = '连续 ' + MAX_REJECTS + ' 次组队被否决，坏人直接获胜';
      st.phase = 'over';
      st.log.push(st.winReason);
      return { ok: true, done: true, approved: false, gameOver: true };
    }
    st.leader = (st.leader + 1) % st.n; // 队长顺延
    st.proposal = null;
    st.phase = 'propose';
    return { ok: true, done: true, approved: false };
  }

  /* ---------------- 任务票（秘密投成功/失败） ----------------
   * 好人只能投成功；坏人可自选。返回 { ok, done, outcome?, gameOver? }
   */
  function playMission(st, success) {
    if (st.phase !== 'mission') return err('当前不是任务阶段');
    var actor = st.missionOrder[st.missionVotes.length];
    if (isGood(st.players[actor].role) && !success) return err('好人只能打出任务成功');
    st.missionVotes.push({ player: actor, success: !!success });
    if (st.missionVotes.length < st.missionOrder.length) return { ok: true, done: false };

    var q = questOf(st);
    var fails = 0, i;
    for (i = 0; i < st.missionVotes.length; i++) if (!st.missionVotes[i].success) fails++;
    var needed = q.twoFails ? 2 : 1;
    var outcome = fails >= needed ? 'fail' : 'success';
    st.results[st.round] = outcome;
    st.failCards[st.round] = fails;
    st.log.push('第' + (st.round + 1) + '轮任务：' + (outcome === 'success' ? '成功 ✓' : '失败 ✗') +
      '（失败票 ' + fails + ' 张）');

    var succ = 0, fail = 0;
    for (i = 0; i < st.results.length; i++) {
      if (st.results[i] === 'success') succ++;
      else if (st.results[i] === 'fail') fail++;
    }

    if (succ >= 3) {
      st.phase = 'assassinate';
      return { ok: true, done: true, outcome: outcome };
    }
    if (fail >= 3) {
      st.winner = 'evil';
      st.winReason = '三项任务失败，坏人获胜';
      st.phase = 'over';
      st.log.push(st.winReason);
      return { ok: true, done: true, outcome: outcome, gameOver: true };
    }
    st.round++;
    st.voteTrack = 0;
    st.proposal = null;
    st.leader = (st.leader + 1) % st.n; // 队长顺延
    st.phase = 'propose';
    return { ok: true, done: true, outcome: outcome };
  }

  /* ---------------- 刺杀梅林 ---------------- */
  function assassinOf(st) {
    for (var i = 0; i < st.n; i++) if (st.players[i].role === 'assassin') return i;
    return -1;
  }

  function assassinate(st, targetIdx) {
    if (st.phase !== 'assassinate') return err('当前不是刺杀阶段');
    if (targetIdx < 0 || targetIdx >= st.n) return err('刺杀目标无效');
    var target = st.players[targetIdx];
    st.assassinated = targetIdx;
    if (target.role === 'merlin') {
      st.winner = 'evil';
      st.winReason = '刺客指认梅林正确，坏人获胜';
    } else {
      st.winner = 'good';
      st.winReason = '刺客指认错误（' + target.name + ' 并非梅林），好人获胜';
    }
    st.phase = 'over';
    st.log.push('刺客指认 ' + target.name + ' —— ' + (target.role === 'merlin' ? '指认正确' : '指认错误') +
      '，真实身份：' + ROLES[target.role].name);
    return { ok: true, correct: target.role === 'merlin', targetRole: target.role };
  }

  /* ---------------- 供 UI 使用的查询 ---------------- */
  function voteResultRows(st) {
    // 按座位顺序返回 [{player, approve}]
    var rows = [];
    for (var i = 0; i < st.voteOrder.length; i++) rows.push({ player: st.voteOrder[i], approve: st.votes[i] });
    rows.sort(function (a, b) { return a.player - b.player; });
    return rows;
  }

  return {
    ROLES: ROLES,
    SETUP: SETUP,
    QUESTS: QUESTS,
    MAX_REJECTS: MAX_REJECTS,
    mulberry32: mulberry32,
    isEvil: isEvil,
    isGood: isGood,
    createGame: createGame,
    questOf: questOf,
    knownInfo: knownInfo,
    proposeTeam: proposeTeam,
    vote: vote,
    playMission: playMission,
    assassinOf: assassinOf,
    assassinate: assassinate,
    voteResultRows: voteResultRows
  };
});
