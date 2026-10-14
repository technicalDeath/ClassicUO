using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class FriendlyHealthTrackerTests
    {
        private const uint Friend = 0x1234;

        [Theory]
        [InlineData((byte) NotorietyFlag.Innocent, true, false, true)]       // a blue townsperson or player
        [InlineData((byte) NotorietyFlag.Ally, false, false, true)]          // a guildmate, or a pet
        [InlineData((byte) NotorietyFlag.Invulnerable, true, false, true)]   // a shopkeeper
        [InlineData((byte) NotorietyFlag.Gray, false, false, true)]          // an animal
        [InlineData((byte) NotorietyFlag.Gray, true, false, false)]          // grey in a human body: maybe a player with Criminal Intent on
        [InlineData((byte) NotorietyFlag.Criminal, true, false, false)]
        [InlineData((byte) NotorietyFlag.Criminal, false, false, false)]
        [InlineData((byte) NotorietyFlag.Enemy, false, false, false)]
        [InlineData((byte) NotorietyFlag.Murderer, false, false, false)]     // a hostile monster
        [InlineData((byte) NotorietyFlag.Unknown, false, false, false)]
        [InlineData((byte) NotorietyFlag.Murderer, true, true, true)]        // your own character, whatever colour you are
        public void Who_is_left_out_at_full_health(byte flag, bool humanBody, bool isSelf, bool quiet)
        {
            FriendlyHealthTracker.IsQuiet((NotorietyFlag) flag, humanBody, isSelf).Should().Be(quiet);
        }

        [Fact]
        public void A_friend_at_full_health_that_was_never_hurt_has_no_bar()
        {
            new FriendlyHealthTracker().ShouldHide(Friend, true, 100, 100, 1000).Should().BeTrue();
        }

        [Fact]
        public void A_friend_below_full_health_keeps_the_bar()
        {
            new FriendlyHealthTracker().ShouldHide(Friend, true, 60, 100, 1000).Should().BeFalse();
        }

        [Fact]
        public void A_bar_that_has_just_filled_stays_for_five_seconds_and_then_goes()
        {
            var tracker = new FriendlyHealthTracker();
            tracker.ShouldHide(Friend, true, 60, 100, 1000);

            tracker.ShouldHide(Friend, true, 100, 100, 1001).Should().BeFalse();
            tracker.ShouldHide(Friend, true, 100, 100, 5999).Should().BeFalse();
            tracker.ShouldHide(Friend, true, 100, 100, 6000).Should().BeTrue();
            tracker.ShouldHide(Friend, true, 100, 100, 6001).Should().BeTrue();
        }

        [Fact]
        public void Losing_health_while_the_bar_lingers_brings_it_back_and_the_five_seconds_start_again_when_it_fills()
        {
            var tracker = new FriendlyHealthTracker();
            tracker.ShouldHide(Friend, true, 60, 100, 1000);
            tracker.ShouldHide(Friend, true, 100, 100, 3000).Should().BeFalse();

            tracker.ShouldHide(Friend, true, 90, 100, 4000).Should().BeFalse();
            tracker.ShouldHide(Friend, true, 100, 100, 5000).Should().BeFalse();
            tracker.ShouldHide(Friend, true, 100, 100, 8999).Should().BeFalse();
            tracker.ShouldHide(Friend, true, 100, 100, 9500).Should().BeTrue();
        }

        [Fact]
        public void Someone_who_is_not_a_friend_always_keeps_the_bar()
        {
            var tracker = new FriendlyHealthTracker();

            tracker.ShouldHide(0x99, false, 100, 100, 1000).Should().BeFalse();
            tracker.ShouldHide(0x99, false, 100, 100, 99000).Should().BeFalse();
        }

        [Fact]
        public void Each_character_has_its_own_five_seconds()
        {
            var tracker = new FriendlyHealthTracker();
            tracker.ShouldHide(1, true, 50, 100, 1000);
            tracker.ShouldHide(2, true, 50, 100, 4000);

            tracker.ShouldHide(1, true, 100, 100, 6500).Should().BeTrue();
            tracker.ShouldHide(2, true, 100, 100, 6500).Should().BeFalse();
        }

        [Fact]
        public void The_clock_may_wrap_around_and_the_bar_still_goes_after_five_seconds()
        {
            var tracker = new FriendlyHealthTracker();
            uint nearEnd = uint.MaxValue - 1000;
            tracker.ShouldHide(Friend, true, 50, 100, nearEnd);

            tracker.ShouldHide(Friend, true, 100, 100, nearEnd + 3000).Should().BeFalse();
            tracker.ShouldHide(Friend, true, 100, 100, nearEnd + 6000).Should().BeTrue();
        }

        [Fact]
        public void Pruning_forgets_long_gone_characters_and_keeps_a_recent_one()
        {
            var tracker = new FriendlyHealthTracker();
            tracker.ShouldHide(1, true, 50, 100, 1000);
            tracker.ShouldHide(2, true, 50, 100, 100000);

            tracker.Prune(100100);

            tracker.Tracked.Should().Be(1);
        }
    }
}
