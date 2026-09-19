using System;
using System.Collections.Generic;

namespace Avalon.Core
{
    public enum RoleId { Merlin, Percival, Servant, Assassin, Morgana, Mordred, Oberon, Minion }

    public enum Team { Good, Evil }

    public sealed class RoleInfo
    {
        public RoleId Id { get; }
        public string Name { get; }
        public string Icon { get; }
        public Team Team { get; }
        public string Desc { get; }

        public RoleInfo(RoleId id, string name, string icon, Team team, string desc)
        {
            Id = id; Name = name; Icon = icon; Team = team; Desc = desc;
        }
    }

    /// <summary>单轮任务规格（数据驱动，禁止在流程代码硬编码）。</summary>
    public sealed class QuestSpec
    {
        public int Size { get; }
        /// <summary>7 人及以上第 4 轮需 2 张失败票才算任务失败。</summary>
        public bool TwoFails { get; }

        public QuestSpec(int size, bool twoFails = false)
        {
            Size = size; TwoFails = twoFails;
        }
    }

    /// <summary>规则数据表（官方规则书核实，与 avalon-preview/core.js 同源）。</summary>
    public static class Rules
    {
        public const int MaxRejects = 5;

        public static readonly IReadOnlyDictionary<RoleId, RoleInfo> Roles = new Dictionary<RoleId, RoleInfo>
        {
            [RoleId.Merlin]   = new RoleInfo(RoleId.Merlin,   "梅林",           "\U0001F52E", Team.Good, "能看到除莫德雷德以外的所有坏人。"),
            [RoleId.Percival] = new RoleInfo(RoleId.Percival, "派西维尔",       "\U0001F6E1", Team.Good, "看到梅林与莫甘娜两位候选，无法分辨。"),
            [RoleId.Servant]  = new RoleInfo(RoleId.Servant,  "亚瑟的忠臣",     "\u2694",     Team.Good, "忠诚的圆桌骑士。"),
            [RoleId.Assassin] = new RoleInfo(RoleId.Assassin, "刺客",           "\U0001F5E1", Team.Evil, "好人三胜后指认梅林：指对则坏人胜。"),
            [RoleId.Morgana]  = new RoleInfo(RoleId.Morgana,  "莫甘娜",         "\U0001F319", Team.Evil, "在派西维尔眼中与梅林一模一样。"),
            [RoleId.Mordred]  = new RoleInfo(RoleId.Mordred,  "莫德雷德",       "\u2620",     Team.Evil, "对梅林隐身。"),
            [RoleId.Oberon]   = new RoleInfo(RoleId.Oberon,   "奥伯伦",         "\U0001F441", Team.Evil, "孤狼：双向不可见。"),
            [RoleId.Minion]   = new RoleInfo(RoleId.Minion,   "莫德雷德的爪牙", "\U0001FA78", Team.Evil, "普通坏人，与同伴互相认识（奥伯伦除外）。"),
        };

        /// <summary>各人数角色配置（官方规则书）。</summary>
        public static readonly IReadOnlyDictionary<int, RoleId[]> Setup = new Dictionary<int, RoleId[]>
        {
            [5]  = new[] { RoleId.Merlin, RoleId.Percival, RoleId.Servant, RoleId.Assassin, RoleId.Morgana },
            [6]  = new[] { RoleId.Merlin, RoleId.Percival, RoleId.Servant, RoleId.Servant, RoleId.Assassin, RoleId.Morgana },
            [7]  = new[] { RoleId.Merlin, RoleId.Percival, RoleId.Servant, RoleId.Servant, RoleId.Assassin, RoleId.Morgana, RoleId.Minion },
            [8]  = new[] { RoleId.Merlin, RoleId.Percival, RoleId.Servant, RoleId.Servant, RoleId.Servant, RoleId.Assassin, RoleId.Morgana, RoleId.Minion },
            [9]  = new[] { RoleId.Merlin, RoleId.Percival, RoleId.Servant, RoleId.Servant, RoleId.Servant, RoleId.Servant, RoleId.Assassin, RoleId.Morgana, RoleId.Minion },
            [10] = new[] { RoleId.Merlin, RoleId.Percival, RoleId.Servant, RoleId.Servant, RoleId.Servant, RoleId.Servant, RoleId.Assassin, RoleId.Morgana, RoleId.Mordred, RoleId.Oberon },
        };

        /// <summary>任务人数表（官方规则书；TwoFails = ★轮）。</summary>
        public static readonly IReadOnlyDictionary<int, QuestSpec[]> Quests = new Dictionary<int, QuestSpec[]>
        {
            [5]  = new[] { new QuestSpec(2), new QuestSpec(3), new QuestSpec(2), new QuestSpec(3), new QuestSpec(3) },
            [6]  = new[] { new QuestSpec(2), new QuestSpec(3), new QuestSpec(4), new QuestSpec(3), new QuestSpec(4) },
            [7]  = new[] { new QuestSpec(2), new QuestSpec(3), new QuestSpec(3), new QuestSpec(4, true), new QuestSpec(4) },
            [8]  = new[] { new QuestSpec(3), new QuestSpec(4), new QuestSpec(4), new QuestSpec(5, true), new QuestSpec(5) },
            [9]  = new[] { new QuestSpec(3), new QuestSpec(4), new QuestSpec(4), new QuestSpec(5, true), new QuestSpec(5) },
            [10] = new[] { new QuestSpec(3), new QuestSpec(4), new QuestSpec(4), new QuestSpec(5, true), new QuestSpec(5) },
        };

        public static bool IsEvil(RoleId r) => Roles[r].Team == Team.Evil;
        public static bool IsGood(RoleId r) => Roles[r].Team == Team.Good;

        /// <summary>mulberry32：与 JS 版同算法（含符号溢出语义），保证同种子下行为可复现。</summary>
        public sealed class Rng
        {
            private int _a;
            public Rng(int seed) { _a = seed; }
            public double Next()
            {
                unchecked
                {
                    int t = _a += 0x6D2B79F5;
                    t = (t ^ (t >> 15)) * (t | 1);                        // = Math.imul(t ^ t>>>15, t|1)
                    t ^= t + (t ^ (t >> 7)) * (t | 61);                  // = t ^ (t + Math.imul(t ^ t>>>7, t|61))
                    uint u = (uint)(t ^ (int)((uint)t >> 14));           // = (t ^ t>>>14) >>> 0
                    return u / 4294967296.0;
                }
            }
            public int NextInt(int max) => (int)(Next() * max);
        }

        public static void Shuffle<T>(IList<T> list, Rng rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
