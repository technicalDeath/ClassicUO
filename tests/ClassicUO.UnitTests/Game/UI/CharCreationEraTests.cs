using System.Collections.Generic;
using System.Linq;
using ClassicUO.Assets;
using ClassicUO.Game.Data;
using ClassicUO.Game.UI.Gumps.CharCreation;
using ClassicUO.Utility;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI
{
    public class CharCreationEraTests
    {
        // Skill indexes: 1 Anatomy, 5 Parrying, 7 Blacksmith, 9 Peacemaking, 15 Enticement, 16 EvalInt, 17 Healing,
        // 21 Hiding, 22 Provocation, 25 Magery, 27 Tactics, 29 Musicianship, 31 Archery, 32 Spirit Speak, 34 Tailoring,
        // 37 Tinkering, 40 Swordsmanship, 41 Mace Fighting, 42 Fencing, 43 Wrestling, 45 Mining, 46 Meditation,
        // 47 Stealth, 49 Necromancy, 50 Focus, 51 Chivalry, 52 Bushido, 53 Ninjitsu.
        private static ProfessionInfo Template(string name, params int[] skills)
        {
            var info = new ProfessionInfo(ClientVersion.CV_70160)
            {
                Name = name,
                Type = ProfessionLoader.PROF_TYPE.PROFESSION,
                TopLevel = true,
                SkillDefVal = new int[4, 2] { { 0xFF, 0 }, { 0xFF, 0 }, { 0xFF, 0 }, { 0xFF, 0 } }
            };

            for (int i = 0; i < skills.Length; i++)
            {
                info.SkillDefVal[i, 0] = skills[i];
                info.SkillDefVal[i, 1] = 50;
            }

            return info;
        }

        private static readonly ProfessionInfo Necromancer = Template("Necromancer", 49, 32, 40, 46);
        private static readonly ProfessionInfo Paladin = Template("Paladin", 51, 27, 50, 40);
        private static readonly ProfessionInfo Samurai = Template("Samurai", 52, 40, 50, 5);
        private static readonly ProfessionInfo Ninja = Template("Ninja", 53, 21, 42, 47);

        private static readonly ProfessionInfo Warrior = Template("Warrior", 27, 17, 40);
        private static readonly ProfessionInfo Mage = Template("Mage", 25, 46, 43);
        private static readonly ProfessionInfo Blacksmith = Template("Blacksmith", 7, 37, 45);

        private static readonly ProfessionInfo Archer = Template("Archer", 31, 27, 17);
        private static readonly ProfessionInfo Fencer = Template("Fencer", 42, 27, 5);
        private static readonly ProfessionInfo Macer = Template("Macer", 41, 27, 17);
        private static readonly ProfessionInfo Bard = Template("Bard", 29, 9, 22);

        private static IReadOnlyDictionary<ProfessionInfo, List<ProfessionInfo>> Catalog(params ProfessionInfo[] all)
        {
            var map = new Dictionary<ProfessionInfo, List<ProfessionInfo>>();

            foreach (var info in all)
            {
                map[info] = null;
            }

            return map;
        }

        private static bool Offered(ProfessionInfo info, LockedFeatureFlags flags) =>
            CharCreationEra.IsProfessionOffered(info, Catalog(info), flags);

        [Theory]
        [InlineData("Necromancer")]
        [InlineData("Paladin")]
        [InlineData("Samurai")]
        [InlineData("Ninja")]
        public void LaterEraTemplatesAreHiddenWhenNoLaterExpansionIsOn(string name)
        {
            var info = name switch
            {
                "Necromancer" => Necromancer, "Paladin" => Paladin, "Samurai" => Samurai, _ => Ninja
            };

            Assert.False(Offered(info, LockedFeatureFlags.None));
            Assert.False(Offered(info, LockedFeatureFlags.ExpansionUOR));
        }

        [Fact]
        public void TheUorTemplatesAndTheShardsOwnTemplatesAreOffered()
        {
            foreach (var info in new[] { Warrior, Mage, Blacksmith, Archer, Fencer, Macer, Bard })
            {
                Assert.True(Offered(info, LockedFeatureFlags.ExpansionUOR), info.Name);
            }
        }

        [Fact]
        public void AdvancedHasNoFixedSkillsAndIsAlwaysOffered()
        {
            var advanced = new ProfessionInfo(ClientVersion.CV_70160)
            {
                Name = "Advanced", Type = ProfessionLoader.PROF_TYPE.PROFESSION
            };

            Assert.True(Offered(advanced, LockedFeatureFlags.None));
        }

        [Fact]
        public void EachLaterTemplateReturnsWithTheExpansionThatDefinesItsSkills()
        {
            Assert.True(Offered(Necromancer, LockedFeatureFlags.AOS));
            Assert.True(Offered(Paladin, LockedFeatureFlags.AOS));

            // Samurai needs Bushido (SE) and Focus (AOS); Ninja needs Ninjitsu (SE).
            Assert.False(Offered(Samurai, LockedFeatureFlags.AOS));
            Assert.True(Offered(Samurai, LockedFeatureFlags.AOS | LockedFeatureFlags.SE));
            Assert.True(Offered(Ninja, LockedFeatureFlags.SE));
        }

        [Fact]
        public void ACategoryIsOfferedOnlyWhenOneOfItsTemplatesIs()
        {
            var category = new ProfessionInfo(ClientVersion.CV_70160) { Type = ProfessionLoader.PROF_TYPE.CATEGORY };
            var all = new Dictionary<ProfessionInfo, List<ProfessionInfo>>
            {
                [category] = new List<ProfessionInfo> { Paladin, Ninja }
            };

            Assert.False(CharCreationEra.IsProfessionOffered(category, all, LockedFeatureFlags.ExpansionUOR));

            all[category].Add(Warrior);
            Assert.True(CharCreationEra.IsProfessionOffered(category, all, LockedFeatureFlags.ExpansionUOR));
        }

        [Fact]
        public void AFolderInsideAFolderIsOfferedOnlyWhenOneOfItsProfessionsIs()
        {
            // The August 1999 tree is two levels deep: Adventurer > Archer > (Archer, Bard, Ranger).
            var adventurer = new ProfessionInfo(ClientVersion.CV_70160) { Type = ProfessionLoader.PROF_TYPE.CATEGORY, TopLevel = true };
            var archers = new ProfessionInfo(ClientVersion.CV_70160) { Type = ProfessionLoader.PROF_TYPE.CATEGORY };
            var oldArcher = Template("Archer", 31, 8, 44);
            var all = new Dictionary<ProfessionInfo, List<ProfessionInfo>>
            {
                [adventurer] = new List<ProfessionInfo> { archers },
                [archers] = new List<ProfessionInfo> { oldArcher }
            };

            Assert.True(CharCreationEra.IsProfessionOffered(adventurer, all, LockedFeatureFlags.ExpansionUOR));

            all[archers] = new List<ProfessionInfo> { Necromancer };
            Assert.False(CharCreationEra.IsProfessionOffered(adventurer, all, LockedFeatureFlags.ExpansionUOR));
            Assert.True(CharCreationEra.IsProfessionOffered(adventurer, all, LockedFeatureFlags.AOS));
        }

        [Theory]
        [InlineData(49, false)] // Necromancy
        [InlineData(50, false)] // Focus
        [InlineData(51, false)] // Chivalry
        [InlineData(52, false)] // Bushido
        [InlineData(53, false)] // Ninjitsu
        [InlineData(55, false)] // Mysticism
        [InlineData(56, false)] // Imbuing
        [InlineData(54, false)] // Spellweaving, hidden by every client version
        [InlineData(47, false)] // Stealth, hidden by every client version
        [InlineData(48, false)] // Remove Trap, hidden by every client version
        [InlineData(57, false)] // Throwing, Gargoyle only
        [InlineData(0, true)]
        [InlineData(25, true)]
        [InlineData(31, true)]
        public void TheAdvancedListOffersOnlyEraSkills(int skill, bool expected)
        {
            Assert.Equal(expected, CharCreationEra.IsAdvancedChoice(skill, LockedFeatureFlags.ExpansionUOR, RaceType.HUMAN));
        }

        [Fact]
        public void ThrowingIsOnlyForGargoyles()
        {
            Assert.True(CharCreationEra.IsAdvancedChoice(57, LockedFeatureFlags.None, RaceType.GARGOYLE));
            Assert.False(CharCreationEra.IsAdvancedChoice(57, LockedFeatureFlags.None, RaceType.HUMAN));
        }

        [Fact]
        public void TheAdvancedStatRuleIsOneHundredAndTwentyPointsWithAThirtyMinimum()
        {
            // Mirrors the server's Alpha 3 starting-stats rule, so the screen lets the player assign exactly what the server keeps.
            Assert.Equal(120, CharCreationEra.AdvancedStatTotal);
            Assert.Equal(30, CharCreationEra.AdvancedStatMinimum);
            Assert.Equal(60, CharCreationEra.AdvancedStatMaximum);
            Assert.Equal(CharCreationEra.AdvancedStatTotal, CharCreationEra.AdvancedStatDefaults.Sum());
            Assert.All(CharCreationEra.AdvancedStatDefaults, d => Assert.InRange(d, CharCreationEra.AdvancedStatMinimum, CharCreationEra.AdvancedStatMaximum));
        }

        [Fact]
        public void ALaterExpansionEnablesItsSkillsInAdvanced()
        {
            Assert.True(CharCreationEra.IsAdvancedChoice(51, LockedFeatureFlags.AOS, RaceType.HUMAN));
            Assert.True(CharCreationEra.IsAdvancedChoice(53, LockedFeatureFlags.SE, RaceType.HUMAN));
            Assert.True(CharCreationEra.IsAdvancedChoice(55, LockedFeatureFlags.SA, RaceType.HUMAN));
        }
    }
}
