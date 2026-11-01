// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.IO;
using ClassicUO.Network;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// The shard's left-click actions menu (WorldActions on the server, command 0xB1). A left click on a map static or the ground asks the shard what can be
    /// done there (a tree: Chop wood, Cut kindling; rock: Mine; water: Fish); the answer lists each action and, for one that cannot be done now, why. The menu
    /// shows the actions that can be done first and the rest greyed out at the bottom with the reason as their tooltip. It opens only once the double-click
    /// time has passed, so a double click (walking to the spot) never opens it. Only when the shard's mask allows it (TopBarGump.WorldActionsAllowed).
    /// </summary>
    internal static class WorldActions
    {
        public const int CommandId = 0xB1;

        /// <summary>The labels and reasons of the shard's entries in an item's stock context menu, sent just before the menu itself.</summary>
        public const int ItemTextsCommandId = 0xB2;

        private static uint _itemSerial;
        private static Dictionary<ushort, (string Label, string Reason)> _itemTexts;

        private static ushort _x, _y, _graphic;
        private static sbyte _z;
        private static bool _land;
        private static bool _pending;
        private static uint _showAfter;
        private static List<ContextMenuItemEntry> _ready;

        public static void Ask(World world, int x, int y, sbyte z, ushort graphic, bool land)
        {
            if (!TopBarGump.WorldActionsAllowed || world.Player == null || world.Player.IsDead)
            {
                return;
            }

            _x = (ushort) x;
            _y = (ushort) y;
            _z = z;
            _graphic = graphic;
            _land = land;
            _pending = true;
            _ready = null;
            _showAfter = Time.Ticks + Mouse.MOUSE_DELAY_DOUBLE_CLICK;

            NetClient.Socket.Send_WorldAction(_x, _y, _z, _graphic, 0, 0, land);
        }

        /// <summary>A double click means walking or using, not a menu.</summary>
        public static void Cancel()
        {
            _pending = false;
            _ready = null;
        }

        /// <summary>Opens a menu whose answer came in while the double-click time was still running.</summary>
        public static void Update(World world)
        {
            if (_ready != null && Time.Ticks >= _showAfter)
            {
                var menu = _ready;
                _ready = null;
                UIManager.ShowContextMenu(new ContextMenuShowMenu(world, menu));
            }
        }

        public static void OnItemTexts(ref StackDataReader p)
        {
            if (p.Remaining < 5)
            {
                return;
            }

            _itemSerial = p.ReadUInt32BE();
            int count = p.ReadUInt8();
            _itemTexts = new Dictionary<ushort, (string, string)>();

            for (int i = 0; i < count && p.Remaining >= 4; i++)
            {
                ushort index = p.ReadUInt16BE();
                string label = p.ReadUTF8(p.ReadUInt8());
                string reason = p.Remaining > 0 ? p.ReadUTF8(p.ReadUInt8()) : string.Empty;
                _itemTexts[index] = (label, reason);
            }
        }

        /// <summary>The shard's texts for the menu now arriving for this item, once; null when it sent none.</summary>
        public static Dictionary<ushort, (string Label, string Reason)> TakeItemTexts(uint serial)
        {
            var texts = _itemSerial == serial ? _itemTexts : null;
            _itemTexts = null;
            _itemSerial = 0;

            return texts;
        }

        public static void OnReply(World world, ref StackDataReader p)
        {
            if (p.Remaining < 7)
            {
                return;
            }

            ushort x = p.ReadUInt16BE();
            ushort y = p.ReadUInt16BE();
            ushort graphic = p.ReadUInt16BE();
            int count = p.ReadUInt8();

            // only the answer to the last click opens a menu; a late answer to an earlier click is dropped
            if (!_pending || x != _x || y != _y || graphic != _graphic)
            {
                return;
            }

            _pending = false;

            var can = new List<ContextMenuItemEntry>();
            var cannot = new List<ContextMenuItemEntry>();

            for (int i = 0; i < count && p.Remaining >= 4; i++)
            {
                byte id = p.ReadUInt8();
                bool available = p.ReadUInt8() != 0;
                string label = p.ReadUTF8(p.ReadUInt8());
                string reason = p.Remaining > 0 ? p.ReadUTF8(p.ReadUInt8()) : string.Empty;

                if (available)
                {
                    ushort tx = x, ty = y, tg = graphic;
                    sbyte tz = _z;
                    bool tl = _land;
                    can.Add(new ContextMenuItemEntry(label, () => NetClient.Socket.Send_WorldAction(tx, ty, tz, tg, 1, id, tl)));
                }
                else
                {
                    cannot.Add(new ContextMenuItemEntry(label) { Disabled = true, Tooltip = reason });
                }
            }

            can.AddRange(cannot);

            if (can.Count != 0)
            {
                _ready = can;
                Update(world);
            }
        }
    }
}
