using ClassicUO.Game.GameObjects;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.GameObjects
{
    public class LyingPoseTests
    {
        private const ushort HumanMale = 0x0190;
        private const ushort GhostMale = 0x0192;
        private const ushort Orc = 0x0011;

        [Theory]
        [InlineData(0x0190)]
        [InlineData(0x0191)]
        [InlineData(0x025D)]
        [InlineData(0x025E)]
        public void Living_human_and_elf_bodies_can_lie(ushort graphic)
        {
            LyingPose.IsLyingBody(graphic).Should().BeTrue();
        }

        [Theory]
        [InlineData(0x0192)]
        [InlineData(0x0193)]
        [InlineData(0x0011)]
        [InlineData(0x029A)]
        public void Ghosts_creatures_and_gargoyles_do_not(ushort graphic)
        {
            LyingPose.IsLyingBody(graphic).Should().BeFalse();
        }

        [Fact]
        public void The_flag_bit_marks_a_living_human_as_lying()
        {
            LyingPose.ShouldLie(0x20, HumanMale, 0x1234, false).Should().BeTrue();
            LyingPose.ShouldLie(0x20 | 0x40 | 0x80, HumanMale, 0x1234, false).Should().BeTrue();
        }

        [Fact]
        public void Without_the_bit_nobody_lies()
        {
            LyingPose.ShouldLie(0x00, HumanMale, 0x1234, false).Should().BeFalse();
            LyingPose.ShouldLie(0x01 | 0x02 | 0x04 | 0x08 | 0x10 | 0x40 | 0x80, HumanMale, 0x1234, false).Should().BeFalse();
        }

        [Fact]
        public void The_dead_the_dying_copy_and_other_bodies_are_never_held()
        {
            LyingPose.ShouldLie(0x20, GhostMale, 0x1234, false).Should().BeFalse();
            LyingPose.ShouldLie(0x20, HumanMale, 0x1234, true).Should().BeFalse();
            LyingPose.ShouldLie(0x20, HumanMale, 0x1234 | 0x80000000, false).Should().BeFalse();
            LyingPose.ShouldLie(0x20, Orc, 0x1234, false).Should().BeFalse();
        }

        [Fact]
        public void The_fall_runs_to_the_last_frame_and_stays_there()
        {
            LyingPose.NextFrame(0, 6).Should().Be(1);
            LyingPose.NextFrame(4, 6).Should().Be(5);
            LyingPose.NextFrame(5, 6).Should().Be(5);
            LyingPose.NextFrame(6, 6).Should().Be(5);
            LyingPose.NextFrame(0, 0).Should().Be(0);
        }

        [Fact]
        public void It_is_the_human_die_group_of_six_frames_on_the_unused_flag_bit()
        {
            LyingPose.Group.Should().Be(21);
            LyingPose.FrameCount.Should().Be(6);
            LyingPose.FlagBit.Should().Be(0x20);
        }
    }
}
