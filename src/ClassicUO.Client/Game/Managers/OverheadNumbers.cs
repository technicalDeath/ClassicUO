// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.IO;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// The numbers that float over a head. Damage arrives in the stock damage packet; a heal arrives in a shard command (extended command 0xAB: the serial of the mobile healed and the
    /// hit points restored), because the protocol has no heal packet. One profile switch (Options, Combat-Spells) shows or hides both.
    /// </summary>
    internal static class OverheadNumbers
    {
        public const int HealCommandId = 0xAB;

        public static bool Wanted => ProfileManager.CurrentProfile?.ShowOverheadNumbers != false;

        public static void OnHeal(World world, ref StackDataReader p)
        {
            if (world.Player == null || p.Remaining < 6)
            {
                return;
            }

            Entity entity = world.Get(p.ReadUInt32BE());
            ushort restored = p.ReadUInt16BE();

            if (entity != null && restored > 0 && Wanted)
            {
                world.WorldTextManager.AddHeal(entity, restored);
            }
        }
    }
}
