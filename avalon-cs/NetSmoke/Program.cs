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
    /// <summary>真实 NetSession × 本地 node 权威服：五客户端整局烟雾测试（gate 2 前置）。</summary>
    internal static class Program
    {
        static List<NetSession> _cs = new List<NetSession>();

        static int Main(string[] args)
        {
            string url = args.Length > 0 && (args[0].StartsWith("ws://") || args[0].StartsWith("wss://"))
                ? args[0]
                : "ws://127.0.0.1:" + (args.Length > 0 ? args[0] : "8790");
            var names = new[] { "阿", "贝", "西", "德", "伊" };
            for (int i = 0; i < 5; i++) _cs.Add(new NetSession());
            foreach (var c in _cs) c.Toast += m => Console.WriteLine("[toast] " + m);

            _cs[0].Create(url, names[0]);
            if (!Until(() => !string.IsNullOrEmpty(_cs[0].RoomCode))) return Fail("建房未收到房间码");
            string code = _cs[0].RoomCode;
            for (int i = 1; i < 5; i++) _cs[i].Join(url, code, names[i]);
            if (!Until(() => _cs.All(c => c.StepNow == Step.Lobby && c.LobbyNames.Count(n => n != null) == 5)))
                return Fail("大厅花名册未齐");
            Console.WriteLine("大厅 OK 房间码=" + code);

            _cs[0].StartGame(null);
            if (!Until(() => _cs.All(c => c.S != null))) return Fail("开局视图未到");
            Console.WriteLine("开局 OK 身份已下发：" + string.Join(" ", _cs.Select(c => c.S.Players[c.Seat].Role)));

            int guard = 0;
            while (_cs[0].StepNow != Step.GameOver && guard++ < 600)
            {
                Pump();
                for (int i = 0; i < 5; i++)
                {
                    var c = _cs[i];
                    var s = c.S;
                    if (s == null) continue;
                    switch (c.StepNow)
                    {
                        case Step.Propose: c.Propose(Enumerable.Range(0, s.QuestOf(-1).Size).ToList()); break;
                        case Step.Vote: c.CastVote(true); break;
                        case Step.Mission: c.CastMission(true); break;
                        case Step.Assassinate: c.Assassinate((i + 1) % s.N); break;
                    }
                }
            }
            if (!Until(() => _cs.All(c => c.StepNow == Step.GameOver))) return Fail("未走到终局（step=" + _cs[0].StepNow + "）");

            var w = _cs[0].S.Winner;
            if (w == null || _cs.Any(c => c.S.Winner != w)) return Fail("各座位胜方不一致");
            if (_cs.Any(c => c.S.Phase != Phase.Over)) return Fail("有座位未收到终局视图");
            var reveal = string.Join("，", _cs[0].S.Players.Select(p => p.Name + "=" + p.Role));
            Console.WriteLine("整局 OK " + guard + " 步 winner=" + w + "（" + _cs[0].S.WinReason + "）身份揭晓：" + reveal);
            foreach (var c in _cs) c.BackToSetup();
            Pump();
            Console.WriteLine("SMOKE PASS");
            return 0;
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
