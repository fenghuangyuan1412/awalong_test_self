using System;
using System.Collections.Generic;
using System.Linq;
using Avalon.Core;
using NUnit.Framework;

namespace Avalon.Tests
{
    /// <summary>配置表测试：角色配置与任务人数表与官方规则书一致。</summary>
    [TestFixture]
    public class ConfigTests
    {
        private static readonly Dictionary<int, int> ExpectedEvil = new()
        { [5] = 2, [6] = 2, [7] = 3, [8] = 3, [9] = 3, [10] = 4 };

        private static readonly Dictionary<int, int[]> ExpectedQuests = new()
        {
            [5] = new[] { 2, 3, 2, 3, 3 }, [6] = new[] { 2, 3, 4, 3, 4 },
            [7] = new[] { 2, 3, 3, 4, 4 }, [8] = new[] { 3, 4, 4, 5, 5 },
            [9] = new[] { 3, 4, 4, 5, 5 }, [10] = new[] { 3, 4, 4, 5, 5 },
        };

        [TestCase(5), TestCase(6), TestCase(7), TestCase(8), TestCase(9), TestCase(10)]
        public void Setup_MatchesOfficial(int n)
        {
            var setup = Rules.Setup[n];
            Assert.AreEqual(n, setup.Length, $"{n}人局角色数");
            Assert.AreEqual(ExpectedEvil[n], setup.Count(Rules.IsEvil), $"{n}人局坏人数");
            Assert.AreEqual(1, setup.Count(r => r == RoleId.Merlin), "梅林恰好1名");
            Assert.AreEqual(1, setup.Count(r => r == RoleId.Percival), "派西维尔恰好1名");
            Assert.AreEqual(1, setup.Count(r => r == RoleId.Assassin), "刺客恰好1名");
        }

        [TestCase(5), TestCase(6), TestCase(7), TestCase(8), TestCase(9), TestCase(10)]
        public void Quests_MatchOfficial(int n)
        {
            var quests = Rules.Quests[n];
            Assert.AreEqual(5, quests.Length, "任务轮数");
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(ExpectedQuests[n][i], quests[i].Size, $"{n}人局第{i + 1}轮人数");
                Assert.AreEqual(n >= 7 && i == 3, quests[i].TwoFails, $"{n}人局第{i + 1}轮双失败票标记");
            }
        }
    }

    /// <summary>角色视野测试（10 人局：含莫德雷德与奥伯伦）。</summary>
    [TestFixture]
    public class VisionTests
    {
        private static GameState FindGameWithAllRoles(int seedStart)
        {
            for (int s = seedStart; s < seedStart + 500; s++)
            {
                var st = Engine.CreateGame(Names(10), s);
                if (st.Players.Any(p => p.Role == RoleId.Mordred) &&
                    st.Players.Any(p => p.Role == RoleId.Oberon))
                    return st;
            }
            throw new InvalidOperationException("未找到含全部角色的对局");
        }

        private static List<string> Names(int n)
            => Enumerable.Range(1, n).Select(i => "P" + i).ToList();

        [Test]
        public void Merlin_SeesAllEvilExceptMordred()
        {
            var st = FindGameWithAllRoles(1);
            int merlin = st.Players.FindIndex(p => p.Role == RoleId.Merlin);
            var sees = Engine.KnownInfo(st, merlin);
            Assert.AreEqual(3, sees.Count, "10人局梅林看到3名坏人（刺客+莫甘娜+奥伯伦，不含莫德雷德）");
            Assert.IsTrue(sees.All(v => v.Kind == "evil"));
            Assert.IsFalse(sees.Any(v => st.Players[v.Idx].Role == RoleId.Mordred), "莫德雷德对梅林隐身");
        }

        [Test]
        public void Percival_SeesExactlyMerlinAndMorgana()
        {
            var st = FindGameWithAllRoles(1);
            int percival = st.Players.FindIndex(p => p.Role == RoleId.Percival);
            var sees = Engine.KnownInfo(st, percival);
            Assert.AreEqual(2, sees.Count);
            Assert.IsTrue(sees.All(v => v.Kind == "merlinOrMorgana"));
            CollectionAssert.AreEquivalent(
                new[] { RoleId.Merlin, RoleId.Morgana },
                sees.Select(v => st.Players[v.Idx].Role));
        }

        [Test]
        public void EvilSeesAllies_ExceptOberon()
        {
            var st = FindGameWithAllRoles(1);
            int mordred = st.Players.FindIndex(p => p.Role == RoleId.Mordred);
            var sees = Engine.KnownInfo(st, mordred);
            Assert.AreEqual(2, sees.Count, "莫德雷德看到刺客+莫甘娜（不含奥伯伦）");
            Assert.IsTrue(sees.All(v => v.Kind == "ally"));
        }

        [Test]
        public void OberonAndServant_SeeNothing()
        {
            var st = FindGameWithAllRoles(1);
            int oberon = st.Players.FindIndex(p => p.Role == RoleId.Oberon);
            int servant = st.Players.FindIndex(p => p.Role == RoleId.Servant);
            Assert.IsEmpty(Engine.KnownInfo(st, oberon), "奥伯伦无视野");
            Assert.IsEmpty(Engine.KnownInfo(st, servant), "忠臣无视野");
        }
    }

    /// <summary>强制场景与房规测试。</summary>
    [TestFixture]
    public class ScenarioTests
    {
        private static List<string> Names(int n)
            => Enumerable.Range(1, n).Select(i => "P" + i).ToList();

        [Test]
        public void HouseRule_LeaderMayBeExcludedFromTeam()
        {
            var st = Engine.CreateGame(Names(5), 100);
            var q = st.QuestOf(-1);
            var members = new List<int>();
            for (int i = 0; i < q.Size; i++) members.Add((st.Leader + 1 + i) % st.N);
            CollectionAssert.DoesNotContain(members, st.Leader, "测试构造：队伍不含队长");
            var r = Engine.ProposeTeam(st, members);
            Assert.IsTrue(r.Ok, "房规：队长不在队内的组队应被接受，错误：" + r.Error);
            Assert.AreEqual(Phase.Vote, st.Phase);
        }

        [Test]
        public void FiveRejections_EvilWins()
        {
            var st = Engine.CreateGame(Names(5), 42);
            int guard = 0;
            while (st.Phase != Phase.Over && guard++ < 20)
            {
                var q = st.QuestOf(-1);
                var members = new List<int> { st.Leader };
                for (int i = 1; i < q.Size; i++) members.Add((st.Leader + i) % st.N);
                Assert.IsTrue(Engine.ProposeTeam(st, members).Ok);
                for (int v = 0; v < st.N; v++) Engine.Vote(st, false);
            }
            Assert.AreEqual(Phase.Over, st.Phase);
            Assert.AreEqual(Team.Evil, st.Winner);
            Assert.AreEqual(5, st.VoteTrack);
        }

        [Test]
        public void GoodPlayer_CannotPlayFail()
        {
            var st = Engine.CreateGame(Names(5), 7);
            int guard = 0;
            while (st.Phase != Phase.Mission && guard++ < 50)
            {
                if (st.Phase == Phase.Propose)
                {
                    var q = st.QuestOf(-1);
                    var members = new List<int> { st.Leader };
                    for (int i = 1; i < q.Size; i++) members.Add((st.Leader + i) % st.N);
                    Engine.ProposeTeam(st, members);
                }
                else if (st.Phase == Phase.Vote) Engine.Vote(st, true);
            }
            Assert.AreEqual(Phase.Mission, st.Phase);
            var goodMember = st.MissionOrder.First(i => Rules.IsGood(st.Players[i].Role));
            int guard2 = 0;
            while (st.MissionOrder[st.MissionVotes.Count] != goodMember && guard2++ < 10)
                Engine.PlayMission(st, true);
            var r = Engine.PlayMission(st, false);
            Assert.IsFalse(r.Ok, "好人投失败票必须被拒绝");
            StringAssert.Contains("好人只能", r.Error);
        }

        [Test]
        public void Assassin_WrongGuess_GoodWins()
        {
            var st = Engine.CreateGame(Names(5), 3);
            // 直接驱动到三胜：全票通过 + 全成功
            DriveToThreeSuccesses(st);
            Assert.AreEqual(Phase.Assassinate, st.Phase);
            int wrongTarget = Enumerable.Range(0, st.N)
                .First(i => st.Players[i].Role != RoleId.Merlin);
            var r = Engine.Assassinate(st, wrongTarget, out var correct, out _);
            Assert.IsTrue(r.Ok);
            Assert.IsFalse(correct);
            Assert.AreEqual(Team.Good, st.Winner);
        }

        [Test]
        public void Assassin_CorrectGuess_EvilWins()
        {
            var st = Engine.CreateGame(Names(5), 3);
            DriveToThreeSuccesses(st);
            int merlin = st.Players.FindIndex(p => p.Role == RoleId.Merlin);
            Engine.Assassinate(st, merlin, out var correct, out _);
            Assert.IsTrue(correct);
            Assert.AreEqual(Team.Evil, st.Winner);
        }

        private static void DriveToThreeSuccesses(GameState st)
        {
            int guard = 0;
            while (st.Phase != Phase.Assassinate && guard++ < 200)
            {
                if (st.Phase == Phase.Propose)
                {
                    var q = st.QuestOf(-1);
                    var members = new List<int> { st.Leader };
                    for (int i = 1; i < q.Size; i++) members.Add((st.Leader + i) % st.N);
                    Engine.ProposeTeam(st, members);
                }
                else if (st.Phase == Phase.Vote) Engine.Vote(st, true);
                else if (st.Phase == Phase.Mission) Engine.PlayMission(st, true);
            }
        }
    }

    /// <summary>随机整局模拟：路径不变量审计（与 JS simulate.js 同策略）。</summary>
    [TestFixture]
    public class SimulationTests
    {
        private static List<string> Names(int n)
            => Enumerable.Range(1, n).Select(i => "P" + i).ToList();

        public sealed class SimResult
        {
            public GameState St;
            public List<(int actor, bool success, bool isGood)> MissionActions = new();
        }

        private static SimResult SimulateOnce(int n, int seed, double approveP, double evilFailP)
        {
            var st = Engine.CreateGame(Names(n), seed);
            var rng = new Rules.Rng(unchecked((int)((uint)(seed * 2654435761) ^ 0x9E3779B9)));
            var res = new SimResult { St = st };
            int guard = 0;
            while (st.Phase != Phase.Over)
            {
                if (++guard > 5000) throw new InvalidOperationException($"游戏未收敛 seed={seed} n={n}");
                switch (st.Phase)
                {
                    case Phase.Propose:
                    {
                        var q = st.QuestOf(-1);
                        var others = Enumerable.Range(0, n).Where(i => i != st.Leader).ToList();
                        Rules.Shuffle(others, rng);
                        // 房规：30% 的提案不包含队长本人
                        bool includeLeader = rng.Next() < 0.7;
                        int need = includeLeader ? q.Size - 1 : q.Size;
                        var members = (includeLeader ? new List<int> { st.Leader } : new List<int>())
                            .Concat(others.Take(need)).ToList();
                        var r = Engine.ProposeTeam(st, members);
                        Assert.IsTrue(r.Ok, $"组队失败 seed={seed}: {r.Error}");
                        break;
                    }
                    case Phase.Vote:
                        Engine.Vote(st, rng.Next() < approveP);
                        break;
                    case Phase.Mission:
                    {
                        int actor = st.MissionOrder[st.MissionVotes.Count];
                        bool isGood = Rules.IsGood(st.Players[actor].Role);
                        bool success = isGood || rng.Next() >= evilFailP;
                        res.MissionActions.Add((actor, success, isGood));
                        Engine.PlayMission(st, success);
                        break;
                    }
                    case Phase.Assassinate:
                    {
                        var candidates = Enumerable.Range(0, n).Where(i => i != Engine.AssassinOf(st)).ToList();
                        int target = candidates[rng.NextInt(candidates.Count)];
                        Engine.Assassinate(st, target, out _, out _);
                        break;
                    }
                }
            }
            return res;
        }

        private static void Audit(SimResult res, int n, int seed)
        {
            var st = res.St;
            Assert.AreEqual(Phase.Over, st.Phase, $"seed={seed} n={n} 应正常结束");
            Assert.IsNotNull(st.Winner, "应有胜方");
            Assert.IsNotEmpty(st.WinReason);
            int succ = st.Results.Count(r => r == "success");
            int fail = st.Results.Count(r => r == "fail");
            Assert.LessOrEqual(succ + fail, 5, "已完成任务轮数 ≤ 5");
            // 好人从未打出失败票
            foreach (var (actor, success, isGood) in res.MissionActions)
                Assert.IsFalse(isGood && !success, $"seed={seed} 好人打出失败票，规则被违反");
            // 胜负路径一致
            if (st.Winner == Team.Good)
            {
                Assert.AreEqual(3, succ, $"seed={seed} 好人胜应3胜");
                Assert.IsNotNull(st.Assassinated, "好人胜利路径应发生过刺杀");
            }
            else
            {
                bool byFails = fail == 3;
                bool byRejects = st.VoteTrack == Rules.MaxRejects;
                bool byAssassin = succ == 3 && st.Assassinated != null;
                Assert.IsTrue(byFails || byRejects || byAssassin,
                    $"seed={seed} 坏人胜利路径不明确 succ={succ} fail={fail} track={st.VoteTrack}");
            }
            // failCards 与 results 一致（成功轮失败票 < 阈值）
            for (int i = 0; i < 5; i++)
            {
                if (st.Results[i] == null) continue;
                int need = st.QuestOf(i).TwoFails ? 2 : 1;
                if (st.Results[i] == "success")
                    Assert.Less(st.FailCards[i].Value, need, $"seed={seed} 成功轮失败票 < {need}");
                else
                    Assert.GreaterOrEqual(st.FailCards[i].Value, need, $"seed={seed} 失败轮失败票 ≥ {need}");
            }
        }

        [TestCase(5), TestCase(6), TestCase(7), TestCase(8), TestCase(9), TestCase(10)]
        public void RandomGames_InvariantsHold(int n)
        {
            for (int s = 1; s <= 200; s++)
                Audit(SimulateOnce(n, n * 100000 + s, 0.75, 0.5), n, n * 100000 + s);
        }

        [TestCase(5), TestCase(6), TestCase(7), TestCase(8), TestCase(9), TestCase(10)]
        public void LowApprovalGames_InvariantsHold(int n)
        {
            for (int s = 1; s <= 100; s++)
            {
                int seed = n * 100000 + 5000 + s;
                Audit(SimulateOnce(n, seed, 0.45, 0.5), n, seed);
            }
        }
    }
}
