// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.IO;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// The numbers that float over a head. Damage arrives in the stock damage packet; a heal arrives in a shard command (extended command 0xAB: the serial of the mobile healed and the
    /// hit points restored), because the protocol has no heal packet. One profile switch (Options, Combat-Spells) shows or hides both. The shard's stamina numbers
    /// (extended command 0xAD: the serial, the kind, 0 lost to a hit or 1 restored by a refresh potion, and the amount) are drawn in blue and have a switch each.
    /// </summary>
    internal static class OverheadNumbers
    {
        public const int HealCommandId = 0xAB;
        public const int StaminaCommandId = 0xAD;
        public const ushort StaminaHue = 0x005A;

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

        public static void OnStamina(World world, ref StackDataReader p)
        {
            if (world.Player == null || p.Remaining < 7)
            {
                return;
            }

            Entity entity = world.Get(p.ReadUInt32BE());
            byte kind = p.ReadUInt8();
            ushort amount = p.ReadUInt16BE();

            if (entity == null || amount == 0)
            {
                return;
            }

            if (kind == 0 && ProfileManager.CurrentProfile?.ShowStaminaLostNumbers != false)
            {
                world.WorldTextManager.AddStamina(entity, -amount);
            }
            else if (kind == 1 && ProfileManager.CurrentProfile?.ShowStaminaRestoredNumbers != false)
            {
                world.WorldTextManager.AddStamina(entity, amount);
            }
        }
    }
}
