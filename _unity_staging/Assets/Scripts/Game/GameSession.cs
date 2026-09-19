using System;
using System.Collections.Generic;
using Avalon.Core;

namespace Avalon.Game
{
    public enum Step { Setup, HandoffReveal, Reveal, HandoffPropose, Propose, HandoffVote, Vote, VoteResult, HandoffMission, Mission, MissionResult, HandoffAssassinate, Assassinate, Verdict, GameOver }

    /// <summary>
    /// 热座会话粘合层：持有 Core 状态机，推进“传递设备”节奏，向 UI 发事件。
    /// 规则一律委托 Avalon.Core，本层不含任何规则判断。
    /// </summary>
    public sealed class GameSession
    {
        public GameState S { get; private set; }
        public Step StepNow { get; private set; } = Step.Setup;
        public int HandSeat { get; private set; }          // 当前持设备座位
        public string HandHint { get; private set; } = "";
        public string HandLabel { get; private set; } = "";

        public event Action Changed;
        public event Action<string> Toast;
        public void Raise() => Changed?.Invoke();
        public void Say(string msg) => Toast?.Invoke(msg);

        public IReadOnlyList<VisionItem> VisionOfCurrent =>
            S == null ? null : Engine.KnownInfo(S, HandSeat);

        public void StartGame(IList<string> names, int? seed = null)
        {
            S = Engine.CreateGame(names, seed);
            HandSeat = 0;
            EnterHandoff(0, "身份保密 · 请确认身边无人偷看",
                "我是 " + S.Players[0].Name + "，查看身份", Step.HandoffReveal);
        }

        void EnterHandoff(int seat, string hint, string label, Step step)
        {
            HandSeat = seat; HandHint = hint; HandLabel = label; StepNow = step; Raise();
        }

        /* ---- 各步骤的“继续/确认”入口（UI 只调这些方法） ---- */

        public void ConfirmHandoff()
        {
            switch (StepNow)
            {
                case Step.HandoffReveal: StepNow = Step.Reveal; Raise(); break;
                case Step.HandoffPropose: StepNow = Step.Propose; Raise(); break;
                case Step.HandoffVote: StepNow = Step.Vote; Raise(); break;
                case Step.HandoffMission: StepNow = Step.Mission; Raise(); break;
                case Step.HandoffAssassinate: StepNow = Step.Assassinate; Raise(); break;
            }
        }

        public void AckRole()
        {
            int next = HandSeat + 1;
            if (next >= S.N) { StartProposeCycle(); return; }
            EnterHandoff(next, "身份保密 · 请确认身边无人偷看",
                "我是 " + S.Players[next].Name + "，查看身份", Step.HandoffReveal);
        }

        void StartProposeCycle()
        {
            var l = S.Players[S.Leader];
            EnterHandoff(S.Leader, "第 " + (S.Round + 1) + " 轮任务 · 请队长组建 " + S.QuestOf(-1).Size + " 人队伍",
                "我是 " + l.Name + "（队长），开始组队", Step.HandoffPropose);
        }

        public void Propose(IList<int> members)
        {
            var r = Engine.ProposeTeam(S, members);
            if (!r.Ok) { Say(r.Error); return; }
            NextVoter();
        }

        void NextVoter()
        {
            if (S.Votes.Count >= S.N) { StepNow = Step.VoteResult; Raise(); return; }
            int seat = S.VoteOrder[S.Votes.Count];
            EnterHandoff(seat, "第 " + (S.Round + 1) + " 轮 · 第 " + (S.VoteTrack + 1) + " 次组队投票（"
                + (S.Votes.Count + 1) + "/" + S.N + "）",
                "我是 " + S.Players[seat].Name + "，开始投票", Step.HandoffVote);
        }

        public void CastVote(bool approve)
        {
            var r = Engine.Vote(S, approve);
            if (!r.Ok) { Say(r.Error); return; }
            if (S.Phase == Phase.Vote) NextVoter();
            else { StepNow = Step.VoteResult; Raise(); }
        }

        public void ContinueAfterVoteResult()
        {
            switch (S.Phase)
            {
                case Phase.Over: StepNow = Step.GameOver; Raise(); break;
                case Phase.Mission: NextMissioner(); break;
                default: StartProposeCycle(); break;
            }
        }

        void NextMissioner()
        {
            if (S.MissionVotes.Count >= S.MissionOrder.Count) { StepNow = Step.MissionResult; Raise(); return; }
            int seat = S.MissionOrder[S.MissionVotes.Count];
            EnterHandoff(seat, "任务执行 · 仅队员依次操作（" + (S.MissionVotes.Count + 1) + "/" + S.MissionOrder.Count + "）",
                "我是 " + S.Players[seat].Name + "，执行任务", Step.HandoffMission);
        }

        public void CastMission(bool success)
        {
            var r = Engine.PlayMission(S, success);
            if (!r.Ok) { Say(r.Error); return; }
            if (S.Phase == Phase.Mission) NextMissioner();
            else { StepNow = Step.MissionResult; Raise(); }
        }

        public void ContinueAfterMissionResult()
        {
            switch (S.Phase)
            {
                case Phase.Over: StepNow = Step.GameOver; Raise(); break;
                case Phase.Assassinate:
                    int a = Engine.AssassinOf(S);
                    EnterHandoff(a, "好人三胜 · 刺客的最后机会",
                        "我是 " + S.Players[a].Name + "（刺客），开始指认", Step.HandoffAssassinate);
                    break;
                default: StartProposeCycle(); break;
            }
        }

        public void Assassinate(int target)
        {
            var r = Engine.Assassinate(S, target, out _, out _);
            if (!r.Ok) { Say(r.Error); return; }
            StepNow = Step.Verdict; Raise();
        }

        public void ContinueAfterVerdict() { StepNow = Step.GameOver; Raise(); }

        public void RestartSame()
        {
            var names = new List<string>();
            foreach (var p in S.Players) names.Add(p.Name);
            StartGame(names);
        }

        public void BackToSetup() { S = null; StepNow = Step.Setup; Raise(); }
    }
}
