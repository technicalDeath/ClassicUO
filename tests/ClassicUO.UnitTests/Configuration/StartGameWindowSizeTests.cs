using ClassicUO.Configuration;
using FluentAssertions;
using Microsoft.Xna.Framework;
using Xunit;

namespace ClassicUO.UnitTests.Configuration
{
    public class StartGameWindowSizeTests
    {
        private static readonly Point Classic = new Point(600, 480);

        [Fact]
        public void A_share_takes_that_part_of_each_side()
        {
            ProfileManager.StartGameWindowSize(0.7, 1280, 800, Classic).Should().Be(new Point(896, 560));
        }

        [Fact]
        public void No_share_leaves_the_size_alone()
        {
            ProfileManager.StartGameWindowSize(0, 1280, 800, Classic).Should().Be(Classic);
        }

        [Theory]
        [InlineData(-0.5)]
        [InlineData(1.5)]
        public void A_share_outside_zero_to_one_is_ignored(double fraction)
        {
            ProfileManager.StartGameWindowSize(fraction, 1280, 800, Classic).Should().Be(Classic);
        }

        [Fact]
        public void An_unknown_window_leaves_the_size_alone()
        {
            ProfileManager.StartGameWindowSize(0.7, 0, 0, Classic).Should().Be(Classic);
        }

        [Fact]
        public void It_is_never_smaller_than_the_smallest_game_window_the_client_allows()
        {
            ProfileManager.StartGameWindowSize(0.7, 800, 600, Classic).Should().Be(new Point(640, 480));
        }

        [Fact]
        public void It_is_never_larger_than_the_window()
        {
            ProfileManager.StartGameWindowSize(1.0, 1920, 1009, Classic).Should().Be(new Point(1920, 1009));
            ProfileManager.StartGameWindowSize(0.7, 500, 400, Classic).Should().Be(new Point(500, 400));
        }
    }
}
