/*
 * 阿瓦隆 · 统一矢量美术资产（原创绘制，零外部依赖）
 * 风格：金色线稿徽章 + 队伍色渐变底盘（好人=蓝钢 / 坏人=绯红）
 * 说明：全部为 SVG 矢量，可无损缩放；替换 Emoji 占位，对应 Phase 3 美术门禁
 *      （风格统一 / 命名规范 / 无版权风险：本文件所有图形均为本项目原创）。
 */
var AvalonArt = (function () {
  'use strict';

  var GOLD_L = '#F0D98C', GOLD = '#D4AF37', GOLD_D = '#A8862A';
  var TEAM = {
    merlin: 'good', percival: 'good', servant: 'good',
    assassin: 'evil', morgana: 'evil', mordred: 'evil', oberon: 'evil', minion: 'evil'
  };
  var NAMES = {
    merlin: '梅林', percival: '派西维尔', servant: '亚瑟的忠臣',
    assassin: '刺客', morgana: '莫甘娜', mordred: '莫德雷德', oberon: '奥伯伦', minion: '莫德雷德的爪牙'
  };

  var uid = 0;

  function defs(team) {
    uid++;
    var gid = 'ag' + uid, did = 'ad' + uid;
    var top = team === 'good' ? '#3E64B8' : '#8A312A';
    var bot = team === 'good' ? '#141F42' : '#2C1013';
    return '<defs>' +
      '<linearGradient id="' + gid + '" x1="0" y1="0" x2="0" y2="120" gradientUnits="userSpaceOnUse">' +
      '<stop offset="0%" stop-color="' + GOLD_L + '"/><stop offset="55%" stop-color="' + GOLD + '"/>' +
      '<stop offset="100%" stop-color="' + GOLD_D + '"/></linearGradient>' +
      '<radialGradient id="' + did + '" cx="60" cy="42" r="70" gradientUnits="userSpaceOnUse">' +
      '<stop offset="0%" stop-color="' + top + '"/><stop offset="100%" stop-color="' + bot + '"/></radialGradient>' +
      '</defs>';
  }

  function gold(id) { return 'url(#ag' + id + ')'; }

  // 角色徽章立绘：viewBox 0 0 120 120
  var EMBLEMS = {
    merlin: function (g) { // 尖顶法帽 + 星辉
      return '<path d="M60 15 L41 60 Q60 69 79 60 Z" fill="none" stroke="' + g + '" stroke-width="4.5" stroke-linejoin="round"/>' +
        '<path d="M31 62 Q60 77 89 62" fill="none" stroke="' + g + '" stroke-width="5.5" stroke-linecap="round"/>' +
        '<path d="M60 32 l3 7 7 3 -7 3 -3 7 -3 -7 -7 -3 7 -3 Z" fill="' + g + '"/>' +
        '<path d="M88 24 v9 M83.5 28.5 h9" stroke="' + g + '" stroke-width="3" stroke-linecap="round"/>' +
        '<path d="M30 82 v7 M26.5 85.5 h7" stroke="' + g + '" stroke-width="3" stroke-linecap="round" opacity=".85"/>';
    },
    percival: function (g) { // 鸢盾 + 人字纹
      return '<path d="M60 17 L87 27 V55 Q87 81 60 97 Q33 81 33 55 V27 Z" fill="none" stroke="' + g + '" stroke-width="4.5" stroke-linejoin="round"/>' +
        '<path d="M45 49 L60 63 L75 49" fill="none" stroke="' + g + '" stroke-width="4.5" stroke-linecap="round" stroke-linejoin="round"/>';
    },
    servant: function (g) { // 交叉双剑
      return '<path d="M41 29 L83 85" stroke="' + g + '" stroke-width="4.5" stroke-linecap="round"/>' +
        '<path d="M35 41 L50 26" stroke="' + g + '" stroke-width="4.5" stroke-linecap="round"/>' +
        '<circle cx="38.5" cy="25.5" r="3" fill="' + g + '"/>' +
        '<path d="M79 29 L37 85" stroke="' + g + '" stroke-width="4.5" stroke-linecap="round"/>' +
        '<path d="M70 26 L85 41" stroke="' + g + '" stroke-width="4.5" stroke-linecap="round"/>' +
        '<circle cx="81.5" cy="25.5" r="3" fill="' + g + '"/>';
    },
    assassin: function (g) { // 短刃 + 血滴
      return '<rect x="55" y="12" width="10" height="15" rx="3" fill="' + g + '"/>' +
        '<path d="M44 31 L76 31" stroke="' + g + '" stroke-width="5" stroke-linecap="round"/>' +
        '<path d="M52 34 L68 34 L64 59 L60 73 L56 59 Z" fill="' + g + '"/>' +
        '<path d="M60 80 Q65.5 89 60 97 Q54.5 89 60 80 Z" fill="' + g + '"/>';
    },
    morgana: function (g) { // 新月 + 星（遮罩法挖出月牙）
      return '<mask id="am' + uid + '"><rect width="120" height="120" fill="#fff"/>' +
        '<circle cx="86" cy="50" r="31" fill="#000"/></mask>' +
        '<circle cx="55" cy="62" r="34" fill="' + g + '" mask="url(#am' + uid + ')"/>' +
        '<path d="M85 66 l3.2 7.3 7.3 3.2 -7.3 3.2 -3.2 7.3 -3.2 -7.3 -7.3 -3.2 7.3 -3.2 Z" fill="' + g + '"/>';
    },
    mordred: function (g) { // 骷髅
      return '<path d="M60 21 Q85 21 85.5 46 Q86 60 76 67 L76 80 Q76 90 66 90 L54 90 Q44 90 44 80 L44 67 Q34 60 34.5 46 Q35 21 60 21 Z" fill="none" stroke="' + g + '" stroke-width="4" stroke-linejoin="round"/>' +
        '<circle cx="49" cy="49" r="5.5" fill="' + g + '"/>' +
        '<circle cx="71" cy="49" r="5.5" fill="' + g + '"/>' +
        '<path d="M60 59 L55.5 68 L64.5 68 Z" fill="' + g + '"/>' +
        '<path d="M52 77 v9 M60 77 v11 M68 77 v9" stroke="' + g + '" stroke-width="3.5" stroke-linecap="round"/>';
    },
    oberon: function (g) { // 之眼 + 射线
      return '<path d="M25 62 Q60 35 95 62 Q60 89 25 62 Z" fill="none" stroke="' + g + '" stroke-width="4" stroke-linejoin="round"/>' +
        '<circle cx="60" cy="62" r="11" fill="none" stroke="' + g + '" stroke-width="4"/>' +
        '<circle cx="60" cy="62" r="4" fill="' + g + '"/>' +
        '<path d="M60 20 v9 M40 24 l4 9 M80 24 l-4 9" stroke="' + g + '" stroke-width="3.5" stroke-linecap="round"/>';
    },
    minion: function (g) { // 爪痕
      return '<path d="M37 23 Q52 58 39 97" fill="none" stroke="' + g + '" stroke-width="5.5" stroke-linecap="round"/>' +
        '<path d="M60 21 Q75 58 62 99" fill="none" stroke="' + g + '" stroke-width="5.5" stroke-linecap="round"/>' +
        '<path d="M83 23 Q98 58 85 97" fill="none" stroke="' + g + '" stroke-width="5.5" stroke-linecap="round"/>';
    }
  };

  // 大徽章（角色立绘）
  function roleIcon(roleId, size) {
    var team = TEAM[roleId] || 'good';
    uid++;
    var d = defs(team);
    return '<svg viewBox="0 0 120 120" width="' + size + '" height="' + size + '" role="img" aria-label="' + NAMES[roleId] + '" class="role-art">' +
      d + '<circle cx="60" cy="60" r="56" fill="url(#ad' + uid + ')" stroke="' + gold(uid) + '" stroke-width="2.5"/>' +
      EMBLEMS[roleId](gold(uid)) + '</svg>';
  }

  // 小图标（UI 功能图标）
  function mini(inner, size, viewBox) {
    viewBox = viewBox || '0 0 24 24';
    uid++;
    var d = '<defs><linearGradient id="ag' + uid + '" x1="0" y1="0" x2="0" y2="24" gradientUnits="userSpaceOnUse">' +
      '<stop offset="0%" stop-color="' + GOLD_L + '"/><stop offset="100%" stop-color="' + GOLD + '"/></linearGradient></defs>';
    return '<svg viewBox="' + viewBox + '" width="' + size + '" height="' + size + '" aria-hidden="true" style="vertical-align:-3px">' +
      d + inner(gold(uid)) + '</svg>';
  }

  var crown = function (size) {
    return mini(function (g) {
      return '<path d="M4.5 17.5 L4.5 8 L10.5 12 L16 3.5 L21.5 12 L27.5 8 L27.5 17.5 Z" fill="' + g + '"/>' +
        '<rect x="4.5" y="19" width="23" height="3" rx="1.5" fill="' + g + '"/>';
    }, size, '0 0 32 24');
  };
  var doc = function (size) {
    return mini(function (g) {
      return '<rect x="5.5" y="3" width="13" height="18" rx="2" fill="none" stroke="' + g + '" stroke-width="2"/>' +
        '<path d="M9 8.5 h6 M9 12 h6 M9 15.5 h4" stroke="' + g + '" stroke-width="2" stroke-linecap="round"/>';
    }, size);
  };
  var book = function (size) {
    return mini(function (g) {
      return '<path d="M12 5.5 Q8 2.5 3.5 4.5 V19.5 Q8 17.5 12 20.5 Q16 17.5 20.5 19.5 V4.5 Q16 2.5 12 5.5 Z" fill="none" stroke="' + g + '" stroke-width="2" stroke-linejoin="round"/>' +
        '<path d="M12 5.5 V20.5" stroke="' + g + '" stroke-width="2"/>';
    }, size);
  };
  var info = function (size) {
    return mini(function (g) {
      return '<circle cx="12" cy="12" r="9" fill="none" stroke="' + g + '" stroke-width="2"/>' +
        '<path d="M12 11 V17" stroke="' + g + '" stroke-width="2.4" stroke-linecap="round"/>' +
        '<circle cx="12" cy="7.4" r="1.4" fill="' + g + '"/>';
    }, size);
  };

  return {
    roleIcon: roleIcon,
    crown: crown,
    doc: doc,
    book: book,
    info: info,
    NAMES: NAMES,
    TEAM: TEAM
  };
})();
