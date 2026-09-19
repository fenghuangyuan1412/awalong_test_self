using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalon.Core;
using Avalon.Game;
using Avalon.Networking;

namespace Avalon.NetSmoke
{
    /// <summary>
    /// 真实 NetSession × 权威服务器：
    ///   自测整局：  dotnet run -- [ws://127.0.0.1:8790]          （5 个机器人建房自战）
    ///   补位模式：  dotnet run -- ws://host:port join 房间码 [N]  （N 个机器人进已有房，默认 4）
    /// 补位策略：投票一律同意；坏人任务票出失败、好人出成功；队长提名最小座位；刺杀随机。
    /// </summary>
    internal static class Program
    {
        static readonly List<NetSession> _cs = new List<NetSession>();
        static string[] _lastSent = Array.Empty<string>();
        static readonly Random _rng = new Random(7);

        static int Main(string[] args)
        {
            string url = args.Length > 0 && (args[0].StartsWith("ws://") || args[0].StartsWith("wss://"))
                ? args[0] : "ws://127.0.0.1:" + (args.Length > 0 ? args[0] : "8790");
            bool joinMode = args.Length > 1 && args[1].Equals("join", StringComparison.OrdinalIgnoreCase);
            string code = joinMode && args.Length > 2 ? args[2].Trim().ToUpperInvariant() : null;
            int bots = joinMode && args.Length > 3 ? int.Parse(args[3]) : 4;

            Console.WriteLine("连接 " + url + (code != null ? " 补位进房 " + code + " ×" + bots : " 自建自战"));
            if (code == null)
            {
                for (int i = 0; i < 5; i++) _cs.Add(new NetSession());
                Wire();
                _cs[0].Create(url, Names()[0]);
                if (!Until(() => !string.IsNullOrEmpty(_cs[0].RoomCode))) return Fail("建房未收到房间码");
                string rc = _cs[0].RoomCode;
                for (int i = 1; i < 5; i++) _cs[i].Join(url, rc, Names()[i]);
                if (!Until(() => _cs.All(c => c.StepNow == Step.Lobby && c.LobbyNames.Count(n => n != null) == 5)))
                    return Fail("大厅花名册未齐");
                Console.WriteLine("大厅 OK 房间码=" + rc);
                _cs[0].StartGame(null);
            }
            else
            {
                for (int i = 0; i < bots; i++) _cs.Add(new NetSession());
                Wire();
                for (int i = 0; i < bots; i++) _cs[i].Join(url, code, "机器人" + (i + 1));
                if (!Until(() => _cs.All(c => c.InRoom), 15000)) return Fail("部分机器人未能进房");
                Console.WriteLine("已进房 " + _cs.Count + " 个机器人，等待房主开局…");
            }

            if (!Until(() => _cs.All(c => c.S != null), 600000)) return Fail("600 秒内未开局/未收到视图");
            Console.WriteLine("开局 OK 机器人身份：" +
                string.Join(" ", _cs.Select(c => c.MyName + "=" + c.S.Players[c.Seat].Role)));

            _lastSent = new string[_cs.Count];
            Step last = Step.Setup;
            int guard = 0;
            // 60000 次 ≈ 30 分钟：真人回合可能想得久，靠服务器超时自动行动兜底
            while (!AllOver() && guard++ < 60000)
            {
                Pump();
                if (_cs[0].StepNow != last) { last = _cs[0].StepNow; Console.WriteLine("[步骤] " + last); }
                for (int i = 0; i < _cs.Count; i++)
                {
                    var c = _cs[i];
                    var s = c.S;
                    if (s == null) continue;
                    // 同一“回合实例”只发一次意图，等服务器回声再更新视图，避免重复刷屏
                    string key = c.StepNow + ":" + s.Round + ":" + s.VoteTrack + ":" + s.Phase;
                    if (key == _lastSent[i]) continue;
                    switch (c.StepNow)
                    {
                        case Step.Propose: c.Propose(Enumerable.Range(0, s.QuestOf(-1).Size).ToList()); break;
                        case Step.Vote: c.CastVote(true); break;
                        case Step.Mission: c.CastMission(Rules.IsGood(s.Players[c.Seat].Role)); break;
                        case Step.Assassinate: c.Assassinate(PickTarget(s, c.Seat)); break;
                        default: continue;
                    }
                    _lastSent[i] = key;
                }
            }
            if (!Until(() => _cs.All(c => c.StepNow == Step.GameOver))) return Fail("未走到终局（step=" + _cs[0].StepNow + "）");

            var w = _cs[0].S.Winner;
            if (w == null || _cs.Any(c => c.S.Winner != w)) return Fail("各座位胜方不一致");
            var reveal = string.Join("，", _cs[0].S.Players.Select(p => p.Name + "=" + p.Role));
            Console.WriteLine("整局 OK " + guard + " 轮询 winner=" + w + "（" + _cs[0].S.WinReason + "）身份揭晓：" + reveal);
            foreach (var c in _cs) c.BackToSetup();
            Pump();
            Console.WriteLine("SMOKE PASS");
            return 0;
        }

        static string[] Names() => new[] { "阿", "贝", "西", "德", "伊", "F", "G", "H", "I", "J" };

        static int PickTarget(GameState s, int excludeSeat)
        {
            var cand = Enumerable.Range(0, s.N).Where(i => i != excludeSeat).ToList();
            return cand.Count == 0 ? 0 : cand[_rng.Next(cand.Count)];
        }

        static bool AllOver() => _cs.All(c => c.S != null && c.S.Phase == Phase.Over);

        static void Wire()
        {
            foreach (var c in _cs) c.Toast += m => Console.WriteLine("[toast] " + m);
        }

        static void Pump()
        {
            for (int i = 0; i < _cs.Count; i++) _cs[i].Tick();
            Thread.Sleep(30);
        }

        static bool Until(Func<bool> pred, int ms = 20000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                Pump();
                if (pred()) return true;
            }
            return false;
        }

        static int Fail(string msg)
        {
            Console.Error.WriteLine("SMOKE FAIL: " + msg);
            return 1;
        }
    }
}
