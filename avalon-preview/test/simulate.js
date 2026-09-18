/*
 * 阿瓦隆规则引擎 · 整局模拟测试
 * 运行方式：
 *  - Node：node test/simulate.js
 *  - 浏览器控制台：先加载 index.html（core.js 已注入 window.AvalonCore），再 eval 本文件
 */
(function () {
  'use strict';
  var C;
  if (typeof require !== 'undefined') C = require('../core.js');
  else if (typeof window !== 'undefined' && window.AvalonCore) C = window.AvalonCore;
  else throw new Error('未找到 AvalonCore，请先加载 core.js');

  var failures = [];
  function check(cond, msg) {
    if (!cond) failures.push(msg);
  }
  function names(n) {
    var a = []; for (var i = 0; i < n; i++) a.push('P' + (i + 1)); return a;
  }

  /* ---------- 1. 静态配置测试 ---------- */
  var EXPECT_EVIL = { 5: 2, 6: 2, 7: 3, 8: 3, 9: 3, 10: 4 };
  var EXPECT_QUESTS = {
    5: [2, 3, 2, 3, 3], 6: [2, 3, 4, 3, 4], 7: [2, 3, 3, 4, 4],
    8: [3, 4, 4, 5, 5], 9: [3, 4, 4, 5, 5], 10: [3, 4, 4, 5, 5]
  };
  [5, 6, 7, 8, 9, 10].forEach(function (n) {
    var setup = C.SETUP[n];
    check(setup.length === n, 'SETUP[' + n + '] 长度应为 ' + n);
    var evil = setup.filter(C.isEvil).length;
    var good = setup.filter(C.isGood).length;
    check(evil === EXPECT_EVIL[n], n + '人局坏人数应为 ' + EXPECT_EVIL[n] + '，实际 ' + evil);
    check(good === n - EXPECT_EVIL[n], n + '人局好人数不正确');
    check(setup.filter(function (r) { return r === 'merlin'; }).length === 1, '梅林必须恰好 1 名');
    check(setup.filter(function (r) { return r === 'percival'; }).length === 1, '派西维尔必须恰好 1 名');
    check(setup.filter(function (r) { return r === 'assassin'; }).length === 1, '刺客必须恰好 1 名');
    var q = C.QUESTS[n];
    check(q.length === 5, n + '人局任务轮数应为 5');
    for (var i = 0; i < 5; i++) {
      check(q[i].size === EXPECT_QUESTS[n][i], n + '人局第' + (i + 1) + '轮人数应为 ' + EXPECT_QUESTS[n][i] + '，实际 ' + q[i].size);
      var needTwo = n >= 7 && i === 3;
      check(!!q[i].twoFails === needTwo, n + '人局第' + (i + 1) + '轮双失败票标记不正确');
    }
  });

  /* ---------- 2. 视角测试（10人局，固定种子扫描找齐角色） ---------- */
  function findGameWithAllRoles(n, seedStart) {
    for (var s = seedStart; s < seedStart + 500; s++) {
      var st = C.createGame(names(n), s);
      var roles = {};
      st.players.forEach(function (p) { roles[p.role] = true; });
      if (n === 10 && roles.mordred && roles.oberon) return st;
      if (n !== 10) return st;
    }
    throw new Error('未找到含全部角色的对局');
  }
  var st10 = findGameWithAllRoles(10, 1);
  var byRole = {};
  st10.players.forEach(function (p, i) { (byRole[p.role] = byRole[p.role] || []).push(i); });

  // 梅林：看到坏人 = 全部邪恶 − 莫德雷德
  var merlinIdx = byRole.merlin[0];
  var merlinSees = C.knownInfo(st10, merlinIdx);
  var expectedMerlinSees = st10.players.filter(function (p) { return C.isEvil(p.role) && p.role !== 'mordred'; }).length;
  check(merlinSees.length === expectedMerlinSees && expectedMerlinSees === 3,
    '10人局梅林应看到 3 名坏人（不含莫德雷德），实际 ' + merlinSees.length);
  check(merlinSees.every(function (k) { return k.kind === 'evil'; }), '梅林视野条目 kind 应为 evil');

  // 派西维尔：恰好看到 梅林 + 莫甘娜 两位
  var percivalSees = C.knownInfo(st10, byRole.percival[0]);
  check(percivalSees.length === 2 && percivalSees.every(function (k) { return k.kind === 'merlinOrMorgana'; }),
    '派西维尔应恰好看到 2 位“梅林/莫甘娜”候选');

  // 莫德雷德：看到刺客+莫甘娜（不含奥伯伦）
  var mordredSees = C.knownInfo(st10, byRole.mordred[0]);
  check(mordredSees.length === 2 && mordredSees.every(function (k) { return k.kind === 'ally'; }),
    '莫德雷德应看到 2 名同伴（不含奥伯伦），实际 ' + mordredSees.length);

  // 奥伯伦：无视野
  check(C.knownInfo(st10, byRole.oberon[0]).length === 0, '奥伯伦应无任何视野');

  // 莫甘娜：看到刺客+莫德雷德（不含奥伯伦）
  var morganaSees = C.knownInfo(st10, byRole.morgana[0]);
  check(morganaSees.length === 2, '莫甘娜应看到 2 名同伴（不含奥伯伦），实际 ' + morganaSees.length);

  // 忠臣：无视野
  check(C.knownInfo(st10, byRole.servant[0]).length === 0, '忠臣应无视野');

  /* ---------- 3. 强制场景：连续否决 5 次 → 坏人胜 ---------- */
  (function () {
    var st = C.createGame(names(5), 42);
    var guard = 0;
    while (st.phase !== 'over' && guard++ < 20) {
      var q = C.questOf(st);
      var members = [st.leader];
      for (var i = 1; i < q.size; i++) members.push((st.leader + i) % st.n);
      var r = C.proposeTeam(st, members);
      check(r.ok, '强制否决场景：组队应成功 ' + (r.error || ''));
      // 全员反对
      for (var v = 0; v < st.n; v++) C.vote(st, false);
    }
    check(st.phase === 'over', '连续否决后游戏应结束');
    check(st.winner === 'evil' && st.voteTrack === 5, '连续否决 5 次应判坏人胜，实际 voteTrack=' + st.voteTrack);
  })();

  /* ---------- 4. 强制场景：好人尝试投失败票被拒绝 ---------- */
  (function () {
    var st = C.createGame(names(5), 7);
    var guard = 0;
    // 推进到任务阶段
    while (st.phase !== 'mission' && guard++ < 50) {
      if (st.phase === 'propose') {
        var q = C.questOf(st), members = [st.leader];
        for (var i = 1; i < q.size; i++) members.push((st.leader + i) % st.n);
        C.proposeTeam(st, members);
      } else if (st.phase === 'vote') { C.vote(st, true); }
    }
    check(st.phase === 'mission', '应能推进到任务阶段');
    // 找到第一个好人队员，尝试投失败票
    var goodMember = st.missionOrder.filter(function (i) { return C.isGood(st.players[i].role); })[0];
    if (goodMember != null) {
      // 轮到该队员时尝试投失败
      var guard2 = 0;
      while (st.missionOrder[st.missionVotes.length] !== goodMember && guard2++ < 10) {
        C.playMission(st, true);
      }
      var r = C.playMission(st, false);
      check(!r.ok && /好人只能/.test(r.error), '好人投失败票必须被拒绝，实际：' + JSON.stringify(r));
    }
  })();

  /* ---------- 5. 随机整局模拟 ---------- */
  function simulateOnce(n, seed, policy) {
    var st = C.createGame(names(n), seed);
    var rng = C.mulberry32((seed * 2654435761) >>> 0 ^ 0x9e3779b9);
    var missionActions = []; // 审计：{actor, success, isGood}
    var guard = 0;
    while (st.phase !== 'over') {
      if (++guard > 5000) throw new Error('游戏未收敛（seed=' + seed + ', n=' + n + '）');
      if (st.phase === 'propose') {
        var q = C.questOf(st);
        var members = [st.leader];
        var others = [];
        for (var i = 0; i < st.n; i++) if (i !== st.leader) others.push(i);
        // 洗牌取前 size-1
        for (var k = others.length - 1; k > 0; k--) {
          var j = Math.floor(rng() * (k + 1));
          var t = others[k]; others[k] = others[j]; others[j] = t;
        }
        members = members.concat(others.slice(0, q.size - 1));
        var r = C.proposeTeam(st, members);
        check(r.ok, '模拟组队失败（seed=' + seed + '）：' + (r.error || ''));
      } else if (st.phase === 'vote') {
        var approve = rng() < policy.approveP;
        C.vote(st, approve);
      } else if (st.phase === 'mission') {
        var actor = st.missionOrder[st.missionVotes.length];
        var isGood = C.isGood(st.players[actor].role);
        var success = isGood ? true : (rng() < policy.evilFailP ? false : true);
        missionActions.push({ actor: actor, success: success, isGood: isGood });
        C.playMission(st, success);
      } else if (st.phase === 'assassinate') {
        var target = policy.smartAssassin
          ? st.players.map(function (p, i) { return i; })
              .filter(function (i) { return i !== C.assassinOf(st); })[Math.floor(rng() * (st.n - 1))]
          : Math.floor(rng() * st.n);
        var r2 = C.assassinate(st, target);
        check(r2.ok, '刺杀调用失败：' + (r2.error || ''));
      }
    }
    return { st: st, missionActions: missionActions };
  }

  function auditResult(res, n, seed) {
    var st = res.st;
    check(st.phase === 'over' && st.winner, 'seed=' + seed + ' n=' + n + ' 游戏应正常结束且有胜方');
    check(!!st.winReason, '应有胜负原因');
    var succ = st.results.filter(function (r) { return r === 'success'; }).length;
    var fail = st.results.filter(function (r) { return r === 'fail'; }).length;
    check(succ + fail <= 5, '已完成任务轮数不得超过 5');
    // 好人从未打出失败票
    res.missionActions.forEach(function (m) {
      check(!(m.isGood && !m.success), 'seed=' + seed + ' 好人打出了失败票，规则被违反！');
    });
    // 胜负与战局一致
    if (st.winner === 'good') {
      check(succ === 3 && st.assassinated != null, '好人获胜路径：应 3 胜且发生过刺杀');
    } else {
      var byFails = (fail === 3), byRejects = (st.voteTrack === 5), byAssassin = (succ === 3 && st.assassinated != null);
      check(byFails || byRejects || byAssassin, '坏人获胜路径不明确：succ=' + succ + ' fail=' + fail + ' voteTrack=' + st.voteTrack);
      check(!(byFails && byRejects && fail === 3 && succ > 0) ? true : true, '');
    }
    // failCards 与 results 一致（成功轮失败票必须少于阈值；双失败票轮出 1 张仍算成功）
    for (var i = 0; i < 5; i++) {
      var need = C.questOf(st, i).twoFails ? 2 : 1;
      if (st.results[i] === 'success') check(st.failCards[i] < need, '成功轮失败票应 < ' + need + '，实际 ' + st.failCards[i]);
      if (st.results[i] === 'fail') {
        check(st.failCards[i] >= need, '失败轮失败票应 ≥ ' + need);
      }
    }
  }

  var tally = {};
  var GAMES_PER_N = 300;
  [5, 6, 7, 8, 9, 10].forEach(function (n) {
    var evilWins = 0;
    for (var s = 1; s <= GAMES_PER_N; s++) {
      var seed = n * 100000 + s;
      var policy = {
        approveP: 0.75, evilFailP: 0.5, smartAssassin: (s % 2 === 0)
      };
      var res = simulateOnce(n, seed, policy);
      auditResult(res, n, seed);
      if (res.st.winner === 'evil') evilWins++;
    }
    // 低通过率策略：验证 5 连否决路径可自然发生
    var rejectWins = 0;
    for (var s2 = 1; s2 <= 150; s2++) {
      var seed2 = n * 100000 + 5000 + s2;
      var res2 = simulateOnce(n, seed2, { approveP: 0.45, evilFailP: 0.5, smartAssassin: false });
      auditResult(res2, n, seed2);
      if (res2.st.winReason && res2.st.winReason.indexOf('否决') >= 0) rejectWins++;
    }
    tally[n] = { evilRate: (evilWins / GAMES_PER_N * 100).toFixed(1) + '%', rejectPathGames: rejectWins };
  });

  /* ---------- 输出 ---------- */
  var summary = {
    ok: failures.length === 0,
    totalChecks: null,
    failures: failures.slice(0, 20),
    tally: tally
  };
  var text = failures.length === 0
    ? '✅ 全部测试通过（含 ' + 6 * GAMES_PER_N + ' 局随机模拟 + 强制场景）\n' + JSON.stringify(tally, null, 2)
    : '❌ 发现 ' + failures.length + ' 个问题：\n' + failures.slice(0, 20).join('\n');
  try { console.log(text); } catch (e) {}
  if (typeof window !== 'undefined') window.__simResults = summary;
  return summary;
})();
