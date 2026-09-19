using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Avalon.Core
{
    public enum Phase { Propose, Vote, Mission, Assassinate, Over }

    public sealed class PlayerState
    {
        public string Name { get; }
        public RoleId Role { get; set; }   // Networking 组装联机视图时需要赋值
        public PlayerState(string name) { Name = name; }
    }

    public sealed class VisionItem
    {
        public int Idx { get; }
        public string Name { get; }
        /// <summary>evil（确认坏人）/ merlinOrMorgana（二选一）/ ally（坏同伴）</summary>
        public string Kind { get; }
        public VisionItem(int idx, string name, string kind) { Idx = idx; Name = name; Kind = kind; }
    }

    public sealed class VoteHistoryEntry
    {
        public int Round, Attempt, Leader;
        public int[] Members;
        public bool Approved;
        public int Yes, No;
        public bool[] Votes;
    }

    /// <summary>动作结果：Ok=false 时 Error 说明原因（与 JS 版 Result 约定一致）。</summary>
    public sealed class Result
    {
        public bool Ok { get; }
        public string Error { get; }
        public bool Done { get; }
        public bool? Approved { get; }
        public bool GameOver { get; }
        public string Outcome { get; }

        public static Result Pending => new Result(true, null, false, null, false, null);
        public static Result Step(bool gameOver = false, string outcome = null)
            => new Result(true, null, true, null, gameOver, outcome);
        public static Result VoteDone(bool approved, bool gameOver)
            => new Result(true, null, true, approved, gameOver, null);
        public static Result Fail(string error) => new Result(false, error, false, null, false, null);

        private Result(bool ok, string error, bool done, bool? approved, bool gameOver, string outcome)
        { Ok = ok; Error = error; Done = done; Approved = approved; GameOver = gameOver; Outcome = outcome; }
    }

    /// <summary>
    /// 阿瓦隆规则状态机（纯 C#，零引擎依赖；与 avalon-preview/core.js 同构，行为对齐）。
    /// 房规（2026-09-19 用户指定）：队长提案可不包含自己。
    /// </summary>
    public sealed class GameState
    {
        public int N;
        public List<PlayerState> Players = new List<PlayerState>();
        public int Leader;
        public int Round;                 // 0..4
        public int VoteTrack;             // 本轮连续否决次数
        public Phase Phase = Phase.Propose;
        public int[] Proposal;            // 当前提案成员（座位号）
        public List<int> VoteOrder = new List<int>();
        public List<bool> Votes = new List<bool>();
        public List<int> MissionOrder = new List<int>();
        public List<KeyValuePair<int, bool>> MissionVotes = new List<KeyValuePair<int, bool>>();
        public string[] Results = new string[5];     // "success" | "fail"
        public int?[] FailCards = new int?[5];
        public List<VoteHistoryEntry> History = new List<VoteHistoryEntry>();
        public Team? Winner;
        public string WinReason;
        public int? Assassinated;
        public List<string> Log = new List<string>();

        public QuestSpec QuestOf(int round) => Rules.Quests[N][round < 0 ? Round : round];
    }

    public static class Engine
    {
        public static GameState CreateGame(IList<string> names, int? seed = null)
        {
            int n = names.Count;
            if (!Rules.Setup.ContainsKey(n))
                throw new ArgumentException("人数必须在 5–10 人，当前：" + n);

            var rng = new Rules.Rng(seed ?? Environment.TickCount);
            var pool = Rules.Setup[n].ToList();
            Rules.Shuffle(pool, rng);

            var st = new GameState { N = n };
            for (int i = 0; i < n; i++)
                st.Players.Add(new PlayerState(string.IsNullOrWhiteSpace(names[i]) ? "玩家" + (i + 1) : names[i].Trim()) { Role = pool[i] });

            st.Leader = rng.NextInt(n);
            st.Log.Add($"游戏开始：{n} 人局，起始队长：{st.Players[st.Leader].Name}");
            return st;
        }

        /// <summary>私密视角：该玩家能看到的其他玩家信息。</summary>
        public static List<VisionItem> KnownInfo(GameState st, int idx)
        {
            var me = st.Players[idx].Role;
            var outList = new List<VisionItem>();
            for (int i = 0; i < st.N; i++)
            {
                if (i == idx) continue;
                var role = st.Players[i].Role;
                if (me == RoleId.Merlin)
                {
                    if (Rules.IsEvil(role) && role != RoleId.Mordred)
                        outList.Add(new VisionItem(i, st.Players[i].Name, "evil"));
                }
                else if (me == RoleId.Percival)
                {
                    if (role == RoleId.Merlin || role == RoleId.Morgana)
                        outList.Add(new VisionItem(i, st.Players[i].Name, "merlinOrMorgana"));
                }
                else if (Rules.IsEvil(me) && me != RoleId.Oberon)
                {
                    if (Rules.IsEvil(role) && role != RoleId.Oberon)
                        outList.Add(new VisionItem(i, st.Players[i].Name, "ally"));
                }
            }
            return outList;
        }

        /// <summary>组队提案（房规：队长可以不在队内）。</summary>
        public static Result ProposeTeam(GameState st, IList<int> members)
        {
            if (st.Phase != Phase.Propose) return Result.Fail("当前阶段不能组队");
            var q = st.QuestOf(-1);
            if (members == null || members.Count != q.Size)
                return Result.Fail("需要选择 " + q.Size + " 名队员");
            var seen = new HashSet<int>();
            foreach (var m in members)
            {
                if (m < 0 || m >= st.N) return Result.Fail("队员座位号无效");
                if (!seen.Add(m)) return Result.Fail("队员重复");
            }
            st.Proposal = members.OrderBy(x => x).ToArray();
            st.Phase = Phase.Vote;
            st.Votes.Clear();
            st.VoteOrder.Clear();
            for (int k = 0; k < st.N; k++) st.VoteOrder.Add((st.Leader + k) % st.N);
            st.Log.Add($"第{st.Round + 1}轮 第{st.VoteTrack + 1}次组队：{st.Players[st.Leader].Name} 提名 " +
                       string.Join("、", st.Proposal.Select(i => st.Players[i].Name)));
            return Result.Pending;
        }

        /// <summary>全员投票（同意/反对）。同意票严格过半通过，平局即否决。</summary>
        public static Result Vote(GameState st, bool approve)
        {
            if (st.Phase != Phase.Vote) return Result.Fail("当前不是投票阶段");
            st.Votes.Add(approve);
            if (st.Votes.Count < st.N) return Result.Pending;

            int yes = st.Votes.Count(v => v);
            int no = st.N - yes;
            bool approved = yes * 2 > st.N;
            st.History.Add(new VoteHistoryEntry
            {
                Round = st.Round, Attempt = st.VoteTrack, Leader = st.Leader,
                Members = st.Proposal.ToArray(), Approved = approved, Yes = yes, No = no,
                Votes = st.Votes.ToArray()
            });
            st.Log.Add($"投票：同意 {yes} / 反对 {no} → " + (approved ? "通过" : "被否决"));

            if (approved)
            {
                st.Phase = Phase.Mission;
                st.MissionVotes.Clear();
                st.MissionOrder = st.Proposal.OrderBy(x => x).ToList();
                return Result.VoteDone(true, false);
            }

            st.VoteTrack++;
            if (st.VoteTrack >= Rules.MaxRejects)
            {
                st.Winner = Team.Evil;
                st.WinReason = $"连续 {Rules.MaxRejects} 次组队被否决，坏人直接获胜";
                st.Phase = Phase.Over;
                st.Log.Add(st.WinReason);
                return Result.VoteDone(false, true);
            }
            st.Leader = (st.Leader + 1) % st.N;
            st.Proposal = null;
            st.Phase = Phase.Propose;
            return Result.VoteDone(false, false);
        }

        /// <summary>任务票：好人只能投成功；坏人可自选。</summary>
        public static Result PlayMission(GameState st, bool success)
        {
            if (st.Phase != Phase.Mission) return Result.Fail("当前不是任务阶段");
            int actor = st.MissionOrder[st.MissionVotes.Count];
            if (Rules.IsGood(st.Players[actor].Role) && !success)
                return Result.Fail("好人只能打出任务成功");
            st.MissionVotes.Add(new KeyValuePair<int, bool>(actor, success));
            if (st.MissionVotes.Count < st.MissionOrder.Count) return Result.Pending;

            var q = st.QuestOf(-1);
            int fails = st.MissionVotes.Count(m => !m.Value);
            int needed = q.TwoFails ? 2 : 1;
            string outcome = fails >= needed ? "fail" : "success";
            st.Results[st.Round] = outcome;
            st.FailCards[st.Round] = fails;
            st.Log.Add($"第{st.Round + 1}轮任务：" + (outcome == "success" ? "成功 ✓" : "失败 ✗") +
                       $"（失败票 {fails} 张）");

            int succ = st.Results.Count(r => r == "success");
            int fail = st.Results.Count(r => r == "fail");
            if (succ >= 3) { st.Phase = Phase.Assassinate; return Result.Step(false, outcome); }
            if (fail >= 3)
            {
                st.Winner = Team.Evil;
                st.WinReason = "三项任务失败，坏人获胜";
                st.Phase = Phase.Over;
                st.Log.Add(st.WinReason);
                return Result.Step(true, outcome);
            }
            st.Round++;
            st.VoteTrack = 0;
            st.Proposal = null;
            st.Leader = (st.Leader + 1) % st.N;
            st.Phase = Phase.Propose;
            return Result.Step(false, outcome);
        }

        public static int AssassinOf(GameState st)
        {
            for (int i = 0; i < st.N; i++)
                if (st.Players[i].Role == RoleId.Assassin) return i;
            return -1;
        }

        public static Result Assassinate(GameState st, int targetIdx, out bool correct, out RoleId targetRole)
        {
            correct = false; targetRole = default;
            if (st.Phase != Phase.Assassinate) return Result.Fail("当前不是刺杀阶段");
            if (targetIdx < 0 || targetIdx >= st.N) return Result.Fail("刺杀目标无效");
            var target = st.Players[targetIdx];
            st.Assassinated = targetIdx;
            correct = target.Role == RoleId.Merlin;
            targetRole = target.Role;
            st.Winner = correct ? Team.Evil : Team.Good;
            st.WinReason = correct
                ? "刺客指认梅林正确，坏人获胜"
                : $"刺客指认错误（{target.Name} 并非梅林），好人获胜";
            st.Phase = Phase.Over;
            st.Log.Add($"刺客指认 {target.Name} —— " + (correct ? "指认正确" : "指认错误") +
                       "，真实身份：" + Rules.Roles[target.Role].Name);
            return Result.Step(true, null);
        }
    }
}
