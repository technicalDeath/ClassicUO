// SPDX-License-Identifier: BSD-2-Clause

namespace ClassicUO.Game.GameObjects
{
    /// <summary>
    /// A living human the server has marked as lying down (mobile flag 0x20, unused for mobiles in the protocol; the shard sets it while a
    /// player is Knocked Out) is drawn on the ground like a corpse: the human Die animation, held on its last frame. Nothing else about
    /// the mobile changes, so it stays clickable, targetable and lootable where it is drawn.
    /// </summary>
    internal static class LyingPose
    {
        /// <summary>The human Die animation group: the fall, whose last frame is the body flat on the ground.</summary>
        public const byte Group = 21;

        /// <summary>Frames in that animation for the human and elf bodies (anim.mul).</summary>
        public const byte FrameCount = 6;

        public const byte FlagBit = 0x20;

        /// <summary>Living human and elf bodies, male and female. A creature a player is polymorphed into has no such pose.</summary>
        public static bool IsLyingBody(ushort graphic) => graphic is 0x0190 or 0x0191 or 0x025D or 0x025E;

        /// <summary>
        /// Whether a mobile is drawn lying down. A dead mobile, and the dying copy the death animation renames (the high bit of its
        /// serial), are never held: the corpse takes over from them.
        /// </summary>
        public static bool ShouldLie(byte flags, ushort graphic, uint serial, bool isDead) =>
            (flags & FlagBit) != 0 && IsLyingBody(graphic) && !isDead && (serial & 0x80000000) == 0;

        /// <summary>The frame to show next: on to the last one, then it stays there.</summary>
        public static int NextFrame(int current, int frameCount) =>
            frameCount <= 0 ? 0 : current < frameCount - 1 ? current + 1 : frameCount - 1;
    }
}
