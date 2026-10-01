// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Assets;
using ClassicUO.Game.Data;

namespace ClassicUO.Game.UI.Gumps.CharCreation
{
    /// <summary>
    /// Which skills and character templates the creation screens offer for the server's locked-feature flags. One place
    /// decides it, so the Advanced skill list and the template list cannot disagree: a later-era skill needs the flag
    /// of the expansion that introduced it, and a template is offered only when every skill it grants is available.
    /// </summary>
    internal static class CharCreationEra
    {
        /// <summary>The shard's Advanced stat rule: 120 points, at least 30 and at most 60 in each stat (mirrors the server's Alpha 3 starting-stats rule).</summary>
        public const int AdvancedStatTotal = 120;

        public const int AdvancedStatMinimum = 30;

        public const int AdvancedStatMaximum = 60;

        /// <summary>The starting split of <see cref="AdvancedStatTotal"/> on the Advanced screen.</summary>
        public static readonly int[] AdvancedStatDefaults = { 40, 40, 40 };

        private const int Necromancy = 49;
        private const int Focus = 50;
        private const int Chivalry = 51;
        private const int Bushido = 52;
        private const int Ninjitsu = 53;
        private const int Mysticism = 55;
        private const int Imbuing = 56;

        /// <summary>The slot value a template leaves in <see cref="ProfessionInfo.SkillDefVal"/> when it grants fewer than four skills.</summary>
        private const int UnusedSkill = 0xFF;

        /// <summary>True when the expansion that introduced this skill is on, or the skill is not tied to one.</summary>
        public static bool IsSkillInEra(int skillIndex, LockedFeatureFlags flags)
        {
            switch (skillIndex)
            {
                case Necromancy:
                case Focus:
                case Chivalry:
                    return flags.HasFlag(LockedFeatureFlags.AOS);

                case Bushido:
                case Ninjitsu:
                    return flags.HasFlag(LockedFeatureFlags.SE);

                case Mysticism:
                case Imbuing:
                    return flags.HasFlag(LockedFeatureFlags.SA);

                default:
                    return true;
            }
        }

        /// <summary>
        /// The Advanced skill list: era-gated skills plus the entries every client version leaves out (Stealth, Remove
        /// Trap, Spellweaving, and Throwing for anything but a Gargoyle).
        /// </summary>
        public static bool IsAdvancedChoice(int skillIndex, LockedFeatureFlags flags, RaceType race)
        {
            if (skillIndex == 47 || skillIndex == 48 || skillIndex == 54)
            {
                return false;
            }

            if (skillIndex == 57 && race != RaceType.GARGOYLE)
            {
                return false;
            }

            return IsSkillInEra(skillIndex, flags);
        }

        /// <summary>
        /// A template is offered when it grants no later-era skill the server has not enabled. Advanced (no fixed skills)
        /// is always offered. A category is offered when at least one of its templates is.
        /// </summary>
        public static bool IsProfessionOffered(
            ProfessionInfo info,
            IReadOnlyDictionary<ProfessionInfo, List<ProfessionInfo>> all,
            LockedFeatureFlags flags
        )
        {
            if (info.Type == ProfessionLoader.PROF_TYPE.CATEGORY)
            {
                if (all.TryGetValue(info, out List<ProfessionInfo> children) && children != null)
                {
                    foreach (ProfessionInfo child in children)
                    {
                        if (IsProfessionOffered(child, all, flags))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            if (info.SkillDefVal == null)
            {
                return true;
            }

            for (int i = 0; i < info.SkillDefVal.GetLength(0); i++)
            {
                int skill = info.SkillDefVal[i, 0];

                if (skill != UnusedSkill && !IsSkillInEra(skill, flags))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
