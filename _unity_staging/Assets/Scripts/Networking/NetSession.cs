using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Avalon.Core;
using Avalon.Game;

namespace Avalon.Networking
{
    /// <summary>
    /// 联机会话（docs/NETPLAY.md §6）：ClientWebSocket 直连权威服务器，
    /// 把逐座位过滤视图重建为 GameState，步进/权限完全由服务端视图驱动。
    /// 收发在后台线程，UI 每帧调用 Tick() 在主线程排空队列并触发事件。
    /// 零 UnityEngine 依赖。
    /// </summary>
    public sealed class NetSession : ISession
    {
        /* ---------------- ISession ---------------- */

        public GameState S { get; private set; }
        public Step StepNow { get; private set; } = Step.Setup;
        public int HandSeat { get; private set; } = -1;
        public string HandHint { get; private set; } = "";
        public string HandLabel { get; private set; } = "";
        public IReadOnlyList<VisionItem> VisionOfCurrent => _vision;

        public event Action Changed;
        public event Action<string> Toast;
        public void Raise() => Changed?.Invoke();
        public void Say(string msg) => Toast?.Invoke(msg);

        /* ---------------- 联机专有状态（UI 读取） ---------------- */

        public int Seat { get; private set; } = -1;
        public string RoomCode { get; private set; } = "";
        public bool IsHost => Seat == 0;
        public bool InRoom => Seat >= 0;
        public int WaitingSeat { get; private set; } = -1;   // 断线等待中的座位（-1 无）
        public string LinkState { get; private set; } = "";
        public List<string> LobbyNames { get; } = new List<string>();
        public string MyName { get; private set; } = "";

        /* ---------------- 传输 ---------------- */

        string _url, _token;
        ClientWebSocket _ws;
        readonly BlockingCollection<string> _in = new BlockingCollection<string>();
        readonly BlockingCollection<string> _out = new BlockingCollection<string>();
        int _alive;                                // 0=套接字循环应停止
        int _gen;                                  // 连接世代：旧线程不得干扰新连接
        DateTime _nextRetry = DateTime.MinValue;
        readonly List<VisionItem> _vision = new List<VisionItem>();

        public void Create(string url, string name)
        {
            _token = null;
            StartLink(url, name, new Dictionary<string, object> { { "t", "create" }, { "name", name } });
        }

        public void Join(string url, string code, string name)
        {
            _token = null;
            StartLink(url, name, new Dictionary<string, object>
            {
                { "t", "join" }, { "code", (code ?? "").Trim().ToUpperInvariant() }, { "name", name },
            });
        }

        void StartLink(string url, string name, Dictionary<string, object> first)
        {
            StopSocket();
            DrainOutbox();
            _url = url.Trim();
            MyName = name;
            LinkState = "连接中…";
            StepNow = Step.Setup;   // 连接结果由 welcome/toast 驱动后续界面
            _alive = 1;
            int gen = ++_gen;
            var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            _ws = ws;
            _out.Add(NetJson.Write(first));
            new Thread(() => ReaderLoop(ws, gen)) { IsBackground = true }.Start();
            new Thread(() => SenderLoop(ws, gen)) { IsBackground = true }.Start();
            Raise();
        }

        void DrainOutbox() { while (_out.TryTake(out _, 0)) { } }

        void ReaderLoop(ClientWebSocket ws, int gen)
        {
            var buf = new byte[8192];
            try
            {
                ws.ConnectAsync(new Uri(_url), CancellationToken.None).Wait();
                while (ws.State == WebSocketState.Open && _alive == 1 && gen == _gen)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult r;
                    bool closing = false;
                    do
                    {
                        r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                        if (r.MessageType == WebSocketMessageType.Close) { closing = true; break; }
                        sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    } while (!r.EndOfMessage);
                    if (closing) break;
                    _in.Add(sb.ToString());
                }
            }
            catch { /* 走 __closed 分支 */ }
            if (_alive == 1 && gen == _gen) _in.Add("{\"t\":\"__closed\"}");
        }

        void SenderLoop(ClientWebSocket ws, int gen)
        {
            try
            {
                while (_alive == 1 && gen == _gen)
                {
                    if (ws.State != WebSocketState.Open)
                    {
                        Thread.Sleep(80);   // 等连接就绪；连接失败由 reader 报 __closed
                        continue;
                    }
                    if (!_out.TryTake(out var raw, 250)) continue;
                    ws.SendAsync(Encoding.UTF8.GetBytes(raw), WebSocketMessageType.Text, true,
                        CancellationToken.None).Wait();
                }
            }
            catch { /* 忽略：reader 会报断开 */ }
        }

        void StopSocket()
        {
            _alive = 0;
            var ws = _ws;
            _ws = null;
            if (ws == null) return;
            try { ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(800); }
            catch { }
            ws.Dispose();
        }

        void Send(Dictionary<string, object> msg)
        {
            if (_alive == 1) _out.Add(NetJson.Write(msg));
        }

        /// <summary>主线程每帧调用：排空收件队列、处理断线重连。</summary>
        public void Tick()
        {
            bool dirty = false;
            while (_in.TryTake(out var raw, 0))
                if (OnRaw(raw)) dirty = true;

            if (_alive == 0 && _token != null && _nextRetry != DateTime.MinValue
                && DateTime.UtcNow >= _nextRetry)
            {
                _nextRetry = DateTime.MinValue;
                RetryLink();
                return; // RetryLink 内部已 Raise
            }
            if (dirty) Raise();
        }

        void RetryLink()
        {
            DrainOutbox();
            _alive = 1;
            int gen = ++_gen;
            LinkState = "重连中…";
            var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            _ws = ws;
            _out.Add(NetJson.Write(new Dictionary<string, object>
            {
                { "t", "reconnect" }, { "code", RoomCode }, { "seat", Seat }, { "token", _token },
            }));
            new Thread(() => ReaderLoop(ws, gen)) { IsBackground = true }.Start();
            new Thread(() => SenderLoop(ws, gen)) { IsBackground = true }.Start();
            Raise();
        }

        bool OnRaw(string raw)
        {
            Dictionary<string, object> m;
            try { m = NetJson.Obj(NetJson.Parse(raw)); }
            catch { return false; }
            if (m == null) return false;
            switch (NetJson.Strg(m, "t"))
            {
                case "welcome":
                    RoomCode = NetJson.Strg(m, "code");
                    Seat = NetJson.Int(m, "seat", -1);
                    _token = NetJson.Strg(m, "token");
                    LinkState = "已连接 " + _url;
                    _nextRetry = DateTime.MinValue;
                    return true;
                case "lobby":
                    LobbyNames.Clear();
                    foreach (var po in NetJson.Arr(NetJson.Get(m, "players")) ?? new List<object>())
                    {
                        var p = NetJson.Obj(po);
                        LobbyNames.Add(p == null ? null : NetJson.Strg(p, "name"));
                    }
                    if (!NetJson.Bool(m, "started")) StepNow = Step.Lobby;
                    return true;
                case "state":
                    ApplyView(m);
                    return true;
                case "toast":
                    var err = NetJson.Strg(m, "error");
                    if (err != null)
                    {
                        Say(err);
                        if (_alive == 1 && (err == "房间不存在" || err == "重连凭证无效"))
                        { _token = null; StopSocket(); StepNow = Step.Setup; Seat = -1; RoomCode = ""; }
                    }
                    return true;
                case "__closed":
                    _alive = 0;
                    if (_token != null)
                    {
                        LinkState = "连接断开，3 秒后重连…";
                        _nextRetry = DateTime.UtcNow.AddSeconds(3);
                    }
                    else
                    {
                        LinkState = "连接失败";
                        Say("无法连接服务器：" + _url);
                        StepNow = Step.Setup;
                        Seat = -1;
                    }
                    return true;
            }
            return false;
        }

        /* ---------------- 视图 → GameState + 步进 ---------------- */

        void ApplyView(Dictionary<string, object> v)
        {
            var st = new GameState();
            st.N = NetJson.Int(v, "n");
            foreach (var po in NetJson.Arr(NetJson.Get(v, "players")) ?? new List<object>())
            {
                var p = NetJson.Obj(po);
                if (p == null) continue;
                var ps = new PlayerState(NetJson.Strg(p, "name") ?? "?");
                var rid = NetJson.Strg(p, "role");
                if (rid != null && Enum.TryParse<RoleId>(rid, true, out var rr)) ps.Role = rr;
                st.Players.Add(ps);
            }
            if (st.N <= 0 || st.Players.Count < st.N) return;

            st.Leader = NetJson.Int(v, "leader");
            st.Round = NetJson.Int(v, "round");
            st.VoteTrack = NetJson.Int(v, "voteTrack");
            st.Phase = ParsePhase(NetJson.Strg(v, "phase"));

            var res = NetJson.Arr(NetJson.Get(v, "results"));
            if (res != null)
                for (int i = 0; i < res.Count && i < 5; i++) st.Results[i] = res[i] as string;
            var fcs = NetJson.Arr(NetJson.Get(v, "failCards"));
            if (fcs != null)
                for (int i = 0; i < fcs.Count && i < 5; i++) st.FailCards[i] = fcs[i] is double ? (int?)(int)(double)fcs[i] : null;

            foreach (var ho in NetJson.Arr(NetJson.Get(v, "history")) ?? new List<object>())
            {
                var h = NetJson.Obj(ho);
                if (h == null) continue;
                st.History.Add(new VoteHistoryEntry
                {
                    Round = NetJson.Int(h, "round"), Attempt = NetJson.Int(h, "attempt"),
                    Leader = NetJson.Int(h, "leader"),
                    Members = ToInts(NetJson.Arr(NetJson.Get(h, "members"))),
                    Approved = NetJson.Bool(h, "approved"),
                    Yes = NetJson.Int(h, "yes"), No = NetJson.Int(h, "no"),
                    Votes = ToBoolArray(NetJson.Arr(NetJson.Get(h, "votes"))),
                });
            }
            FillList(st.Log, NetJson.Arr(NetJson.Get(v, "log")));

            var prop = NetJson.Obj(NetJson.Get(v, "proposal"));
            if (prop != null) st.Proposal = ToInts(NetJson.Arr(NetJson.Get(prop, "members")));

            // 私密字段用占位计数补齐：UI 只依赖 Count（真实内容留在服务端）
            foreach (var s in NetJson.Arr(NetJson.Get(v, "voteOrder")) ?? new List<object>())
                if (s is double d) st.VoteOrder.Add((int)d);
            int vp = NetJson.Int(v, "voteProgress");
            for (int i = 0; i < vp; i++) st.Votes.Add(false);
            foreach (var s in NetJson.Arr(NetJson.Get(v, "missionOrder")) ?? new List<object>())
                if (s is double d) st.MissionOrder.Add((int)d);
            int mp = NetJson.Int(v, "missionProgress");
            for (int i = 0; i < mp; i++) st.MissionVotes.Add(new KeyValuePair<int, bool>(0, true));

            if (st.Phase == Phase.Over)
            {
                var w = NetJson.Strg(v, "winner");
                st.Winner = w == "good" ? Team.Good : w == "evil" ? Team.Evil : (Team?)null;
                st.WinReason = NetJson.Strg(v, "winReason");
                st.Assassinated = NetJson.NullableInt(v, "assassinated");
            }

            var me = NetJson.Obj(NetJson.Get(v, "me"));
            int mySeat = me != null ? NetJson.Int(me, "seat", Seat) : Seat;
            Seat = mySeat;
            var myRole = me != null ? NetJson.Strg(me, "role") : null;
            if (myRole != null && Enum.TryParse<RoleId>(myRole, true, out var mr) && mySeat < st.Players.Count)
                st.Players[mySeat].Role = mr;
            _vision.Clear();
            foreach (var ko in (me != null ? NetJson.Arr(NetJson.Get(me, "known")) : null) ?? new List<object>())
            {
                var k = NetJson.Obj(ko);
                if (k != null) _vision.Add(new VisionItem(NetJson.Int(k, "idx"), NetJson.Strg(k, "name"), NetJson.Strg(k, "kind")));
            }
            var wait = NetJson.NullableInt(v, "waiting");
            WaitingSeat = wait ?? -1;

            S = st;
            LinkState = "已连接 " + _url;
            MapStep(NetJson.Int(v, "actorSeat", -1));
        }

        static void FillList(List<string> dst, List<object> src)
        {
            if (src == null) return;
            dst.Clear();
            foreach (var s in src) dst.Add(s as string ?? "");
        }

        static int[] ToInts(List<object> src)
        {
            if (src == null) return new int[0];
            var a = new List<int>();
            foreach (var s in src) if (s is double d) a.Add((int)d);
            return a.ToArray();
        }

        static bool[] ToBoolArray(List<object> src)
        {
            if (src == null) return new bool[0];
            var a = new List<bool>();
            foreach (var s in src) a.Add(s is true);
            return a.ToArray();
        }

        void MapStep(int actorSeat)
        {
            string Name(int i) => i >= 0 && i < S.Players.Count ? S.Players[i].Name : "?";
            int me = Seat;
            switch (S.Phase)
            {
                case Phase.Propose:
                    Turn(actorSeat == me ? Step.Propose : Step.Waiting, actorSeat,
                        "第 " + (S.Round + 1) + " 轮 · 队长 " + Name(S.Leader) + " 正在组队（需 " + S.QuestOf(-1).Size + " 人）");
                    break;
                case Phase.Vote:
                    Turn(actorSeat == me ? Step.Vote : Step.Waiting, actorSeat,
                        "组队投票进行中（" + S.Votes.Count + "/" + S.N + "）· 等待 " + Name(actorSeat));
                    break;
                case Phase.Mission:
                    Turn(actorSeat == me ? Step.Mission : Step.Waiting, actorSeat,
                        "任务执行中（" + S.MissionVotes.Count + "/" + S.MissionOrder.Count + "）· 等待 " + Name(actorSeat));
                    break;
                case Phase.Assassinate:
                    Turn(actorSeat == me ? Step.Assassinate : Step.Waiting, actorSeat,
                        "好人三胜 · 等待刺客 " + Name(actorSeat) + " 指认梅林");
                    break;
                default:
                    StepNow = Step.GameOver;
                    break;
            }
        }

        void Turn(Step step, int actor, string hint)
        {
            StepNow = step;
            HandSeat = actor;
            HandHint = hint;
            HandLabel = "等待对方操作";
        }

        /* ---------------- ISession 动作（联机：一律发服务端） ---------------- */

        public void StartGame(IList<string> names, int? seed = null) =>
            Send(new Dictionary<string, object> { { "t", "start" } });

        public void ConfirmHandoff() { }
        public void AckRole() { }
        public void ContinueAfterVoteResult() { }
        public void ContinueAfterMissionResult() { }
        public void ContinueAfterVerdict() { }

        public void Propose(IList<int> members)
        {
            var list = new List<object>();
            foreach (var m in members) list.Add(m);
            Send(new Dictionary<string, object> { { "t", "propose" }, { "members", list } });
        }

        public void CastVote(bool approve) =>
            Send(new Dictionary<string, object> { { "t", "vote" }, { "approve", approve } });

        public void CastMission(bool success) =>
            Send(new Dictionary<string, object> { { "t", "mission" }, { "success", success } });

        public void Assassinate(int target) =>
            Send(new Dictionary<string, object> { { "t", "assassinate" }, { "target", target } });

        public void RestartSame() => Send(new Dictionary<string, object> { { "t", "restart" } });

        public void BackToSetup()
        {
            if (InRoom) Send(new Dictionary<string, object> { { "t", "leave" } });
            _token = null;
            StopSocket();
            Seat = -1; RoomCode = ""; WaitingSeat = -1;
            LobbyNames.Clear();
            S = null;
            StepNow = Step.Setup;
            LinkState = "";
            Raise();
        }

        static Phase ParsePhase(string p)
        {
            switch (p)
            {
                case "vote": return Phase.Vote;
                case "mission": return Phase.Mission;
                case "assassinate": return Phase.Assassinate;
                case "over": return Phase.Over;
                default: return Phase.Propose;
            }
        }
    }
}
