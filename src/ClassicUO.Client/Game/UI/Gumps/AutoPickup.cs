// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Network;

namespace ClassicUO.Game.UI.Gumps
{
    internal enum AutoPickupOption { Arrows, Bolts, Reagents }

    /// <summary>
    /// The shard's automatic pickup of arrows, bolts and reagents (docs/Ammo-Reagent-Auto-Pickup-Plan.md). The server does the picking up, for the kinds a character's skills use;
    /// the client only says which of the three kinds this player wants (extended command 0xAC, one byte, bit 0 arrows, bit 1 bolts, bit 2 reagents), at login and whenever a switch
    /// changes. The switches live in the profile, are shown on the combat bar's gear panel and on their own Options page, and are offered only while the shard allows the feature.
    /// </summary>
    internal static class AutoPickup
    {
        public const ushort CommandId = 0xAC;

        public static bool Allowed => TopBarGump.AutoPickupAllowed;

        public static bool Get(Profile p, AutoPickupOption option) => p != null && option switch
        {
            AutoPickupOption.Arrows => p.AutoPickupArrows,
            AutoPickupOption.Bolts => p.AutoPickupBolts,
            _ => p.AutoPickupReagents
        };

        public static void Set(Profile p, AutoPickupOption option, bool value)
        {
            if (p == null || Get(p, option) == value)
            {
                return;
            }

            switch (option)
            {
                case AutoPickupOption.Arrows: p.AutoPickupArrows = value; break;
                case AutoPickupOption.Bolts: p.AutoPickupBolts = value; break;
                default: p.AutoPickupReagents = value; break;
            }

            AutoUse.NotifyChanged();
            SendMask();
        }

        public static byte MaskOf(Profile p) =>
            (byte)((p != null && p.AutoPickupArrows ? 1 : 0) | (p != null && p.AutoPickupBolts ? 2 : 0) | (p != null && p.AutoPickupReagents ? 4 : 0));

        /// <summary>Tells the shard which kinds this player wants picked up. Nothing is sent while the shard does not offer the feature or before a profile is loaded.</summary>
        public static void SendMask()
        {
            if (!Allowed || ProfileManager.CurrentProfile == null || !NetClient.Socket.IsConnected)
            {
                return;
            }

            NetClient.Socket.Send_AutoPickupMask(MaskOf(ProfileManager.CurrentProfile));
        }

        public static string LabelOf(AutoPickupOption option) => option switch
        {
            AutoPickupOption.Arrows => "Pick up my arrows",
            AutoPickupOption.Bolts => "Pick up my bolts",
            _ => "Pick up my reagents"
        };

        public static string DetailOf(AutoPickupOption option) => option switch
        {
            AutoPickupOption.Arrows => "Arrows lying within one step of you, if your Archery is 30 or more.",
            AutoPickupOption.Bolts => "Bolts lying within one step of you, if your Archery is 30 or more.",
            _ => "Reagents lying within one step of you, if your Magery, Alchemy or Inscription is 30 or more."
        };

        public static BarOptionsGump.Option OptionFor(Profile p, AutoPickupOption option) =>
            new(LabelOf(option), DetailOf(option), () => Get(p, option), v => Set(p, option, v));
    }
}
