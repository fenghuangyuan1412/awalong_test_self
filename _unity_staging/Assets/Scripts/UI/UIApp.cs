using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Avalon.Core;
using Avalon.Game;

namespace Avalon.UI
{
    /// <summary>
    /// 热座界面（Phase 2）：纯代码构建 UGUI，屏幕流转完全由 GameSession 步骤驱动。
    /// 本层只做展示与输入，不含任何规则判断。
    /// </summary>
    public sealed class UIApp : MonoBehaviour
    {
        static class Col
        {
            public static readonly Color Bg = Hex("#10152a");
            public static readonly Color Panel = Hex("#1b2340");
            public static readonly Color Panel2 = Hex("#222c50");
            public static readonly Color Line = Hex("#3a4468");
            public static readonly Color Gold = Hex("#d4af37");
            public static readonly Color Gold2 = Hex("#f0d98c");
            public static readonly Color Text = Hex("#e9e7f2");
            public static readonly Color Muted = Hex("#97a0bf");
            public static readonly Color Good = Hex("#3d6fd6");
            public static readonly Color Good2 = Hex("#7fb0ff");
            public static readonly Color Evil = Hex("#b03a30");
            public static readonly Color Evil2 = Hex("#e07060");
            public static readonly Color Ok = Hex("#2e9e5b");
            public static Color Hex(string s)
            {
                ColorUtility.TryParseHtmlString(s, out var c);
                return c;
            }
        }

        GameSession _ses;
        Canvas _canvas;
        RectTransform _root;
        Font _font;
        ScrollRect _scroll;
        RectTransform _content;
        RectTransform _overlay;
        Text _toast;
        float _toastUntil;

        int _count = 5;
        string[] _names = new string[10];
        readonly List<int> _selected = new List<int>();
        int _assassinTarget = -1;

        void Awake()
        {
            _ses = new GameSession();
            _ses.Changed += Rebuild;
            _ses.Toast += ShowToast;
            BuildSkeleton();
            Rebuild();
        }

        /* ================= 骨架 ================= */
        void BuildSkeleton()
        {
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Droid Sans Fallback", "sans-serif", "Arial" }, 34);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var camGo = new GameObject("UICamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Col.Bg;

            var cvGo = new GameObject("Canvas");
            _canvas = cvGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            var scaler = cvGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(600, 1066);
            scaler.matchWidthOrHeight = 0.5f;
            cvGo.AddComponent<GraphicRaycaster>();

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();

            _root = Stretch(NewRect(cvGo.transform, "Root"));
            var bgImg = _root.gameObject.AddComponent<Image>();
            bgImg.color = Col.Bg;

            // 主内容滚动区
            var srGo = NewRect(_root, "Scroll");
            _scroll = Stretch(srGo).gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false; _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            Stretch(srGo);
            var sImg = srGo.gameObject.AddComponent<Image>(); sImg.color = Color.clear;
            _scroll.viewport = srGo;
            _scroll.content = Stretch(NewRect(srGo, "Content")).gameObject.AddComponent<RectTransform>();
            var vlg = _scroll.content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10; vlg.padding = new RectOffset(16, 16, 14, 24);
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            var fitter = _scroll.content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // 顶部锚定
            var crt = _scroll.content;
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(0, 1);
            crt.pivot = new Vector2(0.5f, 1); crt.anchoredPosition = Vector2.zero;
            _scroll.content.gameObject.AddComponent<LayoutElement>().minHeight = 0;

            // 弹窗遮罩
            _overlay = Stretch(NewRect(_root, "Overlay"));
            var oImg = _overlay.gameObject.AddComponent<Image>();
            oImg.color = new Color(0.03f, 0.04f, 0.09f, 0.85f);
            _overlay.gameObject.AddComponent<LayoutElement>();
            _overlay.gameObject.SetActive(false);

            // Toast
            var tGo = NewRect(_root, "Toast");
            var trt = _overlayToast(tGo);
            _toast = trt;
            tGo.gameObject.SetActive(false);
        }

        Text _overlayToast(RectTransform tGo)
        {
            var rt = tGo;
            rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 90); rt.sizeDelta = new Vector2(520, 54);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.08f, 0.1f, 0.18f, 0.95f);
            return AddText(rt, "", 16, Col.Text, TextAnchor.MiddleCenter);
        }

        static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
        static RectTransform NewRect(RectTransform parent, string name) => NewRect(parent.transform, name);

        static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        Text AddText(RectTransform parent, string text, int size, Color color, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var t = NewRect(parent, "T").gameObject.AddComponent<Text>();
            t.text = text; t.font = _font; t.fontSize = size; t.color = color;
            t.alignment = anchor; t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        Button AddButton(RectTransform parent, string label, int size, Color bg, Color fg, UnityAction onClick, bool outline = false)
        {
            var rt = NewRect(parent, "B_" + label);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = outline ? Col.Panel2 : bg;
            if (outline)
            {
                var ol = rt.gameObject.AddComponent<Outline>();
                ol.effectColor = Col.Line; ol.effectDistance = new Vector2(1, -1);
            }
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = size >= 17 ? 52 : 44;
            var trt = Stretch(NewRect(rt, "L"));
            var t = trt.gameObject.AddComponent<Text>();
            t.text = label; t.font = _font; t.fontSize = size; t.color = fg;
            t.alignment = TextAnchor.MiddleCenter;
            return btn;
        }

        RectTransform AddCard(RectTransform parent, bool glow = false)
        {
            var rt = NewRect(parent, "Card");
            var img = rt.gameObject.AddComponent<Image>();
            img.color = glow ? Col.Hex("#20294c") : Col.Panel;
            if (glow) { var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = Col.Gold; o.effectDistance = new Vector2(1.2f, -1.2f); }
            else { var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = Col.Line; o.effectDistance = new Vector2(1, -1); }
            var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8; vlg.padding = new RectOffset(16, 16, 14, 16);
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        void ShowToast(string msg)
        {
            _toast.text = msg;
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 2.2f;
        }

        void Update()
        {
            if (_toast != null && _toast.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
                _toast.gameObject.SetActive(false);
        }

        void Clear(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) Destroy(rt.GetChild(i).gameObject);
        }

        /* ================= 角色徽章（占位：色底 + 首字，Phase 3 换美术） ================= */
        void AddRoleBadge(RectTransform parent, RoleId role, int px)
        {
            var info = Rules.Roles[role];
            var rt = NewRect(parent, "Badge");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minWidth = px; le.preferredWidth = px; le.minHeight = px; le.preferredHeight = px;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = info.Team == Team.Good ? Col.Hex("#22335f") : Col.Hex("#471f1f");
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = info.Team == Team.Good ? Col.Good2 : Col.Evil2;
            o.effectDistance = new Vector2(1.2f, -1.2f);
            var t = AddText(Stretch(rt), info.Name.Substring(0, 1), (int)(px * 0.42f),
                info.Team == Team.Good ? Col.Good2 : Col.Evil2, TextAnchor.MiddleCenter);
            t.rectTransform.sizeDelta = Vector2.zero;
        }

        /* ================= 屏幕构建 ================= */
        void Rebuild()
        {
            _overlay.gameObject.SetActive(false);
            Clear(_content);
            _selected.Clear();
            switch (_ses.StepNow)
            {
                case Step.Setup: ScreenSetup(); break;
                case Step.HandoffReveal:
                case Step.HandoffPropose:
                case Step.HandoffVote:
                case Step.HandoffMission:
                case Step.HandoffAssassinate: ScreenHandoff(); break;
                case Step.Reveal: ScreenReveal(); break;
                case Step.Propose: ScreenPropose(); break;
                case Step.Vote: ScreenVote(); break;
                case Step.VoteResult: ScreenVoteResult(); break;
                case Step.Mission: ScreenMission(); break;
                case Step.MissionResult: ScreenMissionResult(); break;
                case Step.Assassinate: ScreenAssassinate(); break;
                case Step.Verdict: ScreenVerdict(); break;
                case Step.GameOver: ScreenGameOver(); break;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        void Title(string sub)
        {
            var card = AddCard(_content);
            AddText(card, "阿瓦隆", 46, Col.Gold2, TextAnchor.MiddleCenter)
                .gameObject.AddComponent<LayoutElement>().minHeight = 58;
            AddText(card, sub, 13, Col.Muted, TextAnchor.MiddleCenter);
        }

        void ScreenSetup()
        {
            Title("UNITY 安卓版 · 本地热座预览");
            var card = AddCard(_content, true);
            AddText(card, "选择人数（5–10 人，一台设备轮流传递）", 13, Col.Muted, TextAnchor.MiddleCenter);

            var row = NewRect(card, "Stepper");
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 18; hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            AddButton(row, "−", 20, Col.Panel2, Col.Gold2, () => { _count = Math.Max(5, _count - 1); Rebuild(); });
            var num = AddText(row, _count.ToString(), 34, Col.Gold2, TextAnchor.MiddleCenter);
            num.gameObject.AddComponent<LayoutElement>().minWidth = 60;
            AddButton(row, "＋", 20, Col.Panel2, Col.Gold2, () => { _count = Math.Min(10, _count + 1); Rebuild(); });

            var sum = Rules.Setup[_count].GroupBy(r => r)
                .Select(g => Rules.Roles[g.Key].Name + (g.Count() > 1 ? "×" + g.Count() : ""));
            var good = Rules.Setup[_count].Where(r => Rules.IsGood(r)).GroupBy(r => r)
                .Select(g => Rules.Roles[g.Key].Name + (g.Count() > 1 ? "×" + g.Count() : ""));
            var evil = Rules.Setup[_count].Where(r => Rules.IsEvil(r)).GroupBy(r => r)
                .Select(g => Rules.Roles[g.Key].Name + (g.Count() > 1 ? "×" + g.Count() : ""));
            AddText(card, "蓝方（好人）：" + string.Join("　", good), 13, Col.Good2, TextAnchor.MiddleCenter);
            AddText(card, "红方（坏人）：" + string.Join("　", evil), 13, Col.Evil2, TextAnchor.MiddleCenter);
            var quests = Rules.Quests[_count].Select((q, i) => "第" + (i + 1) + "轮 " + q.Size + "人" + (q.TwoFails ? "（需2失败票）" : ""));
            AddText(card, "任务人数：" + string.Join(" · ", quests), 12, Col.Gold2, TextAnchor.MiddleCenter);

            var nameCard = AddCard(_content);
            AddText(nameCard, "玩家昵称（可留空使用默认）", 13, Col.Muted);
            var grid = NewRect(nameCard, "Names");
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(270, 46); gl.spacing = new Vector2(8, 8);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = 2;
            for (int i = 0; i < _count; i++)
            {
                var inp = NewRect(grid, "N" + i).gameObject.AddComponent<InputField>();
                var iimg = inp.gameObject.AddComponent<Image>(); iimg.color = Col.Panel2;
                var io = inp.gameObject.AddComponent<Outline>(); io.effectColor = Col.Line; io.effectDistance = new Vector2(1, -1);
                var it = Stretch(NewRect(inp.transform, "IT")).gameObject.AddComponent<Text>();
                it.font = _font; it.fontSize = 15; it.color = Col.Text; it.alignment = TextAnchor.MiddleLeft;
                var ph = Stretch(NewRect(inp.transform, "PH")).gameObject.AddComponent<Text>();
                ph.font = _font; ph.fontSize = 15; ph.color = Col.Muted; ph.alignment = TextAnchor.MiddleLeft;
                ph.text = "玩家" + (i + 1);
                inp.text = _names[i] ?? "";
                inp.placeholder = ph; inp.textComponent = it;
                inp.onValueChanged.AddListener(v => _names[i] = v);
                var rt = (RectTransform)inp.transform;
                rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(1, 0.5f);
                rt.sizeDelta = new Vector2(0, 46); rt.anchoredPosition = Vector2.zero;
            }

            AddButton(_content, "开始游戏", 17, Col.Gold, new Color(0.11f, 0.1f, 0.06f), StartGame);
            var btnRow = NewRect(_content, "BtnRow");
            var h2 = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>(); h2.spacing = 10;
            h2.childControlWidth = true; h2.childForceExpandWidth = true;
            AddButton(btnRow, "规则速查", 15, Col.Text * 0f + Col.Panel2, Col.Muted, () => OpenModal(RulesText(), "规则速查"), true);
            AddButton(btnRow, "关于", 15, Col.Panel2, Col.Muted, () => OpenModal(AboutText(), "关于"), true);
        }

        void StartGame()
        {
            var names = new List<string>();
            for (int i = 0; i < _count; i++) names.Add(string.IsNullOrWhiteSpace(_names[i]) ? "玩家" + (i + 1) : _names[i].Trim());
            _ses.StartGame(names);
        }

        void ScreenHandoff()
        {
            Title("请把设备交给");
            var card = AddCard(_content, true);
            var p = _ses.S.Players[_ses.HandSeat];
            AddText(card, p.Name, 32, Col.Gold2, TextAnchor.MiddleCenter);
            AddText(card, _ses.HandHint, 14, Col.Muted, TextAnchor.MiddleCenter);
            AddButton(_content, _ses.HandLabel, 17, Col.Gold, new Color(0.11f, 0.1f, 0.06f), () => _ses.ConfirmHandoff());
            AddText(_content, "其他人请稍作回避，保护隐私信息", 12, Col.Muted, TextAnchor.MiddleCenter);
        }

        void ScreenReveal()
        {
            var S = _ses.S; var idx = _ses.HandSeat;
            var p = S.Players[idx]; var ro = Rules.Roles[p.Role];
            Title("你的身份");
            var card = AddCard(_content, true);
            var inner = AddCard(card, false);
            var ivlg = inner.GetComponent<VerticalLayoutGroup>();
            ivlg.childAlignment = TextAnchor.MiddleCenter;
            var img = inner.GetComponent<Image>();
            img.color = ro.Team == Team.Good ? Col.Hex("#22335f") : Col.Hex("#471f1f");
            inner.GetComponent<Outline>().effectColor = ro.Team == Team.Good ? Col.Good2 : Col.Evil2;
            AddRoleBadge(inner, p.Role, 96);
            inner.GetComponent<LayoutElement>();
            AddText(inner, ro.Name, 26, Col.Text, TextAnchor.MiddleCenter);
            AddText(inner, ro.Team == Team.Good ? "蓝方 · 亚瑟的忠臣" : "红方 · 莫德雷德的爪牙", 13,
                ro.Team == Team.Good ? Col.Good2 : Col.Evil2, TextAnchor.MiddleCenter);
            AddText(inner, ro.Desc, 14, Col.Text, TextAnchor.MiddleCenter);

            var vis = Engine.KnownInfo(S, idx);
            if (p.Role == RoleId.Oberon)
                AddText(card, "👁 你是孤狼：看不到任何同伴，同伴也看不到你", 14, Col.Muted);
            else if (vis.Count > 0)
            {
                AddText(card, "你的视野（仅你可见）", 13, Col.Gold2);
                foreach (var k in vis)
                {
                    string tag = k.Kind == "evil" ? "坏人" : k.Kind == "ally" ? "同伴" : "梅林？莫甘娜？";
                    AddText(card, "· " + tag + "：" + k.Name, 15, Col.Text);
                }
                if (p.Role == RoleId.Percival)
                    AddText(card, "以上两位中，一位是真正的梅林，一位是莫甘娜", 12, Col.Muted);
            }
            AddButton(_content, "我记住了，传给下一位", 17, Col.Gold, new Color(0.11f, 0.1f, 0.06f), () => _ses.AckRole());
        }

        void AddTracks(RectTransform parent)
        {
            var S = _ses.S;
            var row = NewRect(parent, "Track");
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12; hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false; hlg.childControlHeight = false;
            for (int i = 0; i < 5; i++)
            {
                var q = Rules.Quests[S.N][i];
                var node = NewRect(row, "M" + i);
                node.sizeDelta = new Vector2(50, 50);
                var img = node.gameObject.AddComponent<Image>();
                string inner = q.Size.ToString();
                if (S.Results[i] == "success") { img.color = Col.Good; inner = "✓"; }
                else if (S.Results[i] == "fail") { img.color = Col.Evil; inner = "✗"; }
                else { img.color = Col.Panel; inner = q.Size.ToString(); }
                var o = node.gameObject.AddComponent<Outline>();
                o.effectColor = (i == S.Round && S.Results[i] == null) ? Col.Gold : Col.Line;
                o.effectDistance = new Vector2(1.5f, -1.5f);
                var t = AddText(Stretch(node), inner, 15, Col.Text, TextAnchor.MiddleCenter);
                t.rectTransform.sizeDelta = Vector2.zero;
                if (q.TwoFails)
                {
                    var star = AddText(node, "2票", 9, Col.Bg, TextAnchor.MiddleCenter);
                    var srt = star.rectTransform;
                    srt.anchorMin = new Vector2(1, 1); srt.anchorMax = new Vector2(1, 1);
                    srt.pivot = new Vector2(0.5f, 0.5f);
                    srt.anchoredPosition = new Vector2(6, 6); srt.sizeDelta = new Vector2(26, 14);
                    var sim = srt.gameObject.AddComponent<Image>(); sim.color = Col.Gold;
                }
            }
            var vrow = NewRect(parent, "VTrack");
            var vh = vrow.gameObject.AddComponent<HorizontalLayoutGroup>();
            vh.spacing = 8; vh.childAlignment = TextAnchor.MiddleCenter;
            vh.childControlWidth = false; vh.childControlHeight = false;
            for (int j = 0; j < Rules.MaxRejects; j++)
            {
                var d = NewRect(vrow, "V" + j);
                d.sizeDelta = new Vector2(16, 16);
                d.gameObject.AddComponent<Image>().color = j < S.VoteTrack ? Col.Evil : Col.Panel;
                d.gameObject.AddComponent<Outline>().effectColor = Col.Line;
            }
            AddText(parent, "组队被否决 " + S.VoteTrack + "/" + Rules.MaxRejects + "（满 " + Rules.MaxRejects + " 坏人直接获胜）",
                12, Col.Muted, TextAnchor.MiddleCenter);
        }

        void TopBar()
        {
            var S = _ses.S;
            var bar = NewRect(_content, "TopBar");
            var hlg = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft; hlg.spacing = 10;
            hlg.childControlWidth = true; hlg.childForceExpandWidth = false;
            AddText(bar, "第 " + (S.Round + 1) + " / 5 轮任务", 15, Col.Text).gameObject
                .AddComponent<LayoutElement>().flexibleWidth = 1;
            AddButton(bar, "日志", 13, Col.Panel2, Col.Text, () => OpenModal(string.Join("\n", S.Log), "对局日志"), true);
            AddButton(bar, "规则", 13, Col.Panel2, Col.Text, () => OpenModal(RulesText(), "规则速查"), true);
            AddText(_content, "队长：" + S.Players[S.Leader].Name + "　需选出 " + S.QuestOf(-1).Size + " 人",
                14, Col.Gold2);
        }

        void ScreenPropose()
        {
            var S = _ses.S;
            TopBar();
            var card = AddCard(_content, true);
            AddTracks(card);
            var q = S.QuestOf(-1);
            if (q.TwoFails) AddText(card, "⚠ 本轮为双失败票任务：需要 2 张失败票才算任务失败", 13, Col.Gold2);
            AddText(card, "请点选 " + q.Size + " 名队员（队长可不参加）", 16, Col.Text, TextAnchor.MiddleCenter);

            var grid = NewRect(card, "Grid");
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(170, 84); gl.spacing = new Vector2(10, 10);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = S.N > 6 ? 4 : 3;
            for (int i = 0; i < S.N; i++)
            {
                int seat = i;
                var chip = NewRect(grid, "P" + i);
                var img = chip.gameObject.AddComponent<Image>();
                img.color = _selected.Contains(i) ? Col.Hex("#3a3417") : Col.Panel2;
                var co = chip.gameObject.AddComponent<Outline>();
                co.effectColor = _selected.Contains(i) ? Col.Gold : Col.Line;
                co.effectDistance = new Vector2(1.5f, -1.5f);
                var btn = chip.gameObject.AddComponent<Button>();
                btn.onClick.AddListener(() => { ToggleSeat(seat); });
                var t = AddText(chip, (i == S.Leader ? "♛" : "") + S.Players[i].Name, 14, Col.Text, TextAnchor.MiddleCenter);
                t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
                t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            }
            AddButton(card, "确认提案（已选 " + _selected.Count + "/" + q.Size + "）", 17,
                Col.Gold, new Color(0.11f, 0.1f, 0.06f),
                () => { if (_selected.Count == q.Size) _ses.Propose(_selected); else _ses.Say("需要选择 " + q.Size + " 名队员"); });
        }

        void ToggleSeat(int seat)
        {
            var q = _ses.S.QuestOf(-1);
            if (_selected.Contains(seat)) _selected.Remove(seat);
            else
            {
                if (_selected.Count >= q.Size) { _ses.Say("最多选择 " + q.Size + " 人"); return; }
                _selected.Add(seat);
            }
            Rebuild();
        }

        void ScreenVote()
        {
            var S = _ses.S;
            Title("组队投票");
            var card = AddCard(_content, true);
            AddText(card, S.Players[S.Leader].Name + " 提名：", 15, Col.Gold2, TextAnchor.MiddleCenter);
            AddText(card, string.Join("、", S.Proposal.Select(i => S.Players[i].Name)), 18, Col.Text, TextAnchor.MiddleCenter);
            var sep = NewRect(card, "Sep"); sep.sizeDelta = new Vector2(0, 1);
            sep.gameObject.AddComponent<LayoutElement>().minHeight = 1;
            sep.gameObject.AddComponent<Image>().color = Col.Line;
            AddText(card, "你的一票（" + S.Players[S.VoteOrder[S.Votes.Count]].Name + "，其他人请勿观看）", 13, Col.Muted, TextAnchor.MiddleCenter);
            var row = NewRect(card, "VoteBtns");
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>(); hlg.spacing = 10;
            hlg.childControlWidth = true; hlg.childForceExpandWidth = true;
            AddButton(row, "👍 同意", 17, Col.Good, Color.white, () => _ses.CastVote(true));
            AddButton(row, "👎 反对", 17, Col.Evil, Color.white, () => _ses.CastVote(false));
            AddText(card, "同意票过半即通过；平局视为否决", 12, Col.Muted, TextAnchor.MiddleCenter);
        }

        void ScreenVoteResult()
        {
            var S = _ses.S;
            var last = S.History[S.History.Count - 1];
            Title(last.Approved ? "✓ 队伍通过" : "✗ 队伍被否决");
            var card = AddCard(_content, true);
            AddText(card, "同意 " + last.Yes + " : 反对 " + last.No, 20,
                last.Approved ? Col.Good2 : Col.Evil2, TextAnchor.MiddleCenter);
            foreach (var seat in Enumerable.Range(0, S.N))
            {
                bool approve = last.Votes[seat];
                string mark = (seat == last.Leader ? " ♛" : "") + (last.Members.Contains(seat) ? "（队员）" : "");
                AddText(card, S.Players[seat].Name + mark + "　" + (approve ? "同意" : "反对"), 15,
                    approve ? Col.Good2 : Col.Evil2);
            }
            AddButton(_content, "继续", 17, Col.Gold, new Color(0.11f, 0.1f, 0.06f), () => _ses.ContinueAfterVoteResult());
        }

        void ScreenMission()
        {
            var S = _ses.S;
            int seat = S.MissionOrder[S.MissionVotes.Count];
            var p = S.Players[seat]; var ro = Rules.Roles[p.Role];
            Title("第 " + (S.Round + 1) + " 轮任务 · 你的任务票");
            var card = AddCard(_content, true);
            AddText(card, p.Name, 26, Col.Text, TextAnchor.MiddleCenter);
            AddRoleBadge(card, p.Role, 64);
            AddText(card, ro.Name + "（" + (ro.Team == Team.Good ? "好人" : "坏人") + "）", 14, Col.Muted, TextAnchor.MiddleCenter);
            if (ro.Team == Team.Evil)
            {
                var row = NewRect(card, "MBtns");
                var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>(); hlg.spacing = 10;
                hlg.childControlWidth = true; hlg.childForceExpandWidth = true;
                AddButton(row, "任务成功", 17, Col.Good, Color.white, () => _ses.CastMission(true));
                AddButton(row, "任务失败", 17, Col.Evil, Color.white, () => _ses.CastMission(false));
                AddText(card, "作为坏人，你可以选择打出失败票；任务票不会公开是谁打的", 12, Col.Muted, TextAnchor.MiddleCenter);
            }
            else
            {
                AddButton(card, "任务成功", 17, Col.Good, Color.white, () => _ses.CastMission(true));
                AddText(card, "好人只能打出任务成功", 12, Col.Muted, TextAnchor.MiddleCenter);
            }
        }

        void ScreenMissionResult()
        {
            var S = _ses.S;
            int round = Math.Min(S.Round, 4);
            var outcome = S.Results[round];
            var last = S.History[S.History.Count - 1];
            bool isWin = outcome == "success";
            Title(isWin ? "任务成功" : "任务失败");
            var card = AddCard(_content, isWin);
            var img = card.GetComponent<Image>();
            img.color = isWin ? Col.Hex("#22335f") : Col.Hex("#471f1f");
            card.GetComponent<Outline>().effectColor = isWin ? Col.Good2 : Col.Evil2;
            AddText(card, "第 " + (round + 1) + " 轮 · 出现 " + S.FailCards[round] + " 张失败票", 16, Col.Text, TextAnchor.MiddleCenter);
            AddText(card, "本轮队员：" + string.Join("、", last.Members.Select(i => S.Players[i].Name)), 14, Col.Muted, TextAnchor.MiddleCenter);
            AddTracks(card);
            AddButton(_content, "继续", 17, Col.Gold, new Color(0.11f, 0.1f, 0.06f), () => _ses.ContinueAfterMissionResult());
        }

        void ScreenAssassinate()
        {
            var S = _ses.S;
            Title("刺杀梅林");
            var card = AddCard(_content, true);
            AddText(card, "指认正确 → 坏人获胜；指认错误 → 好人获胜", 13, Col.Muted, TextAnchor.MiddleCenter);
            var grid = NewRect(card, "Grid");
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(170, 84); gl.spacing = new Vector2(10, 10);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = S.N > 6 ? 4 : 3;
            for (int i = 0; i < S.N; i++)
            {
                int seat = i;
                var chip = NewRect(grid, "A" + i);
                var img = chip.gameObject.AddComponent<Image>();
                img.color = _assassinTarget == i ? Col.Hex("#4a2020") : Col.Panel2;
                chip.gameObject.AddComponent<Outline>().effectColor = _assassinTarget == i ? Col.Evil2 : Col.Line;
                var btn = chip.gameObject.AddComponent<Button>();
                btn.onClick.AddListener(() => { _assassinTarget = seat; Rebuild(); });
                var t = AddText(chip, S.Players[i].Name, 14, Col.Text, TextAnchor.MiddleCenter);
                t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
                t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            }
            AddButton(card, "确认指认", 17, Col.Evil, Color.white, () =>
            {
                if (_assassinTarget < 0) { _ses.Say("请先选择要指认的玩家"); return; }
                OpenModal(S.Players[_assassinTarget].Name + " 为梅林？确认后立即揭晓结果。", "确认刺杀",
                    () => _ses.Assassinate(_assassinTarget), "确定指认");
            });
        }

        void ScreenVerdict()
        {
            var S = _ses.S;
            var t = S.Players[S.Assassinated.Value];
            var ro = Rules.Roles[t.Role];
            bool correct = t.Role == RoleId.Merlin;
            Title("刺客指认了 " + t.Name);
            var card = AddCard(_content, true);
            AddRoleBadge(card, t.Role, 84);
            AddText(card, ro.Name, 24, Col.Text, TextAnchor.MiddleCenter);
            AddText(card, correct ? "✗ 指认正确 —— 梅林被刺杀！" : "✓ 指认错误 —— 梅林安然无恙！", 18,
                correct ? Col.Evil2 : Col.Good2, TextAnchor.MiddleCenter);
            AddButton(_content, "查看结算", 17, Col.Gold, new Color(0.11f, 0.1f, 0.06f), () => _ses.ContinueAfterVerdict());
        }

        void ScreenGameOver()
        {
            var S = _ses.S;
            bool goodWin = S.Winner == Team.Good;
            Title(goodWin ? "蓝方胜利" : "红方胜利");
            var card = AddCard(_content, goodWin);
            var img = card.GetComponent<Image>();
            img.color = goodWin ? Col.Hex("#22335f") : Col.Hex("#471f1f");
            card.GetComponent<Outline>().effectColor = goodWin ? Col.Good2 : Col.Evil2;
            AddText(card, S.WinReason, 15, Col.Text, TextAnchor.MiddleCenter);
            AddText(card, "身份揭晓", 14, Col.Gold2, TextAnchor.MiddleCenter);
            foreach (var p in S.Players)
            {
                var ro = Rules.Roles[p.Role];
                AddText(card, p.Name + "　" + ro.Name + " · " + (ro.Team == Team.Good ? "好人" : "坏人"), 15,
                    ro.Team == Team.Good ? Col.Good2 : Col.Evil2);
            }
            AddTracks(card);
            AddButton(_content, "同样玩家再来一局", 17, Col.Gold, new Color(0.11f, 0.1f, 0.06f), () => _ses.RestartSame());
            AddButton(_content, "返回设置", 15, Col.Panel2, Col.Muted, () => _ses.BackToSetup(), true);
        }

        /* ================= 弹窗 ================= */
        void OpenModal(string body, string title, UnityAction confirm = null, string confirmLabel = null)
        {
            Clear(_overlay);
            var center = NewRect(_overlay, "Center");
            center.anchorMin = new Vector2(0.5f, 0.5f); center.anchorMax = new Vector2(0.5f, 0.5f);
            center.sizeDelta = new Vector2(540, 0);
            var vlg = center.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8; vlg.padding = new RectOffset(18, 18, 16, 18);
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            var csf = center.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var cimg = center.gameObject.AddComponent<Image>(); cimg.color = Col.Panel;
            center.gameObject.AddComponent<Outline>().effectColor = Col.Line;

            AddText(center, title, 20, Col.Gold2, TextAnchor.MiddleCenter);
            var svGo = NewRect(center, "SV");
            svGo.gameObject.AddComponent<LayoutElement>().minHeight = 560;
            var sv = svGo.gameObject.AddComponent<ScrollRect>();
            svGo.gameObject.AddComponent<Image>().color = Col.Bg;
            var vp = Stretch(NewRect(svGo, "VP"));
            var vpImg = vp.gameObject.AddComponent<Image>(); vpImg.color = Color.clear;
            vp.gameObject.AddComponent<RectMask2D>();
            sv.viewport = vp;
            var ct = Stretch(NewRect(vp, "CT"));
            ct.anchorMin = new Vector2(0, 1); ct.anchorMax = new Vector2(1, 1);
            ct.pivot = new Vector2(0.5f, 1); ct.sizeDelta = new Vector2(0, 0);
            var cvlg = ct.gameObject.AddComponent<VerticalLayoutGroup>();
            cvlg.padding = new RectOffset(10, 10, 10, 10); cvlg.spacing = 4;
            cvlg.childControlWidth = true; cvlg.childForceExpandWidth = true;
            ct.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sv.content = ct;
            var bodyTxt = AddText(ct, body, 14, Col.Text);
            bodyTxt.resizeTextForBestFit = false;
            if (confirm != null)
                AddButton(center, confirmLabel, 16, Col.Evil, Color.white, () => { _overlay.gameObject.SetActive(false); confirm(); });
            AddButton(center, "关闭", 15, Col.Panel2, Col.Muted, () => _overlay.gameObject.SetActive(false), true);
            _overlay.gameObject.SetActive(true);
        }

        string RulesText()
        {
            var rows = new List<string>();
            foreach (var n in new[] { 5, 6, 7, 8, 9, 10 })
                rows.Add(n + "人：" + string.Join(" / ", Rules.Quests[n].Select(q => q.Size + (q.TwoFails ? "★" : ""))));
            return "对局流程\n" +
                "1. 组队：队长选人，可以不包含自己（本版房规；官方规则要求队长在队内）\n" +
                "2. 全员投票：同意票严格过半通过（平局=否决）；否决后队长顺延\n" +
                "3. 连续否决 5 次：坏人直接获胜\n" +
                "4. 任务：队员秘密出票，好人只能出成功，坏人可出失败；1 张失败票即失败（★轮需 2 张）\n" +
                "5. 胜负：好人赢 3 轮 → 刺杀梅林，刺客指对则坏人胜；坏人赢 3 轮直接胜\n\n" +
                "角色视野\n" +
                "梅林：看到所有坏人（莫德雷德除外）\n" +
                "派西维尔：看到梅林与莫甘娜（无法分辨）\n" +
                "坏人：互相认识（奥伯伦除外，孤狼无视野）\n\n" +
                "任务人数表（★=需2失败票）\n" + string.Join("\n", rows) +
                "\n\n规则依据：《The Resistance: Avalon》官方规则书。";
        }

        string AboutText()
        {
            return "阿瓦隆 · Unity（团结引擎）安卓版\n" +
                "对应项目路线图 Phase 1（C# 规则引擎 Avalon.Core）+ Phase 2（本地热座界面）。\n" +
                "规则逻辑与界面严格分层（Core 零引擎依赖）。\n" +
                "学习与个人使用用途；商业发布前需完成 IP 合规改造。";
        }
    }
}
