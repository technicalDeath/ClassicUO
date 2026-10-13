// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// The client only learns what is in a container when it is opened. The shard's item bars (potion bar, combat bar) need to know what is in the pack and in its bags, so once
    /// at login this opens the pack and then each plain bag in it, one at a time, with no window at all: the open packet for a container listed in <see cref="Silent"/> makes no
    /// gump, takes no remembered position and plays no sound (PacketHandlers.OpenContainer), but the contents still arrive. Both bars call <see cref="Tick"/>; it is safe to call
    /// from either and when only one bar is on.
    /// </summary>
    internal static class PackPrimer
    {
        private static readonly HashSet<uint> Silent = new();
        private static readonly HashSet<uint> BagsTried = new();

        private const int BagSpacing = 1500;

        private static World _world;
        private static bool _primed;
        private static bool _primeClosed;
        private static long _primeDeadline;
        private static uint _bagOpening;
        private static long _nextBag;
        private static long _bagDeadline;

        /// <summary>The plain bags, pouches, boxes and chests a pack may hold: ones that only open when double-clicked. A keg, a book or a runebook does something else, so it is left out.</summary>
        private static readonly HashSet<ushort> PlainContainers = new()
        {
            0x0E76, 0x0E79, 0x0E7D, 0x0E80, 0x09AA, 0x09A8, 0x09B0, 0x0E3C, 0x0E3D, 0x0E3E, 0x0E3F, 0x0E40, 0x0E41, 0x0E42, 0x0E43
        };

        public static bool IsSilent(uint serial) => Silent.Contains(serial);

        public static void Tick(World world)
        {
            if (world?.Player == null)
            {
                return;
            }

            if (!ReferenceEquals(world, _world))
            {
                Reset(world);
            }

            Item backpack = world.Player.FindItemByLayer(Layer.Backpack);

            if (backpack == null)
            {
                return;
            }

            Prime(world, backpack);

            if (_primeClosed)
            {
                PrimeBags(world, backpack);
            }
        }

        private static void Reset(World world)
        {
            _world = world;
            Silent.Clear();
            BagsTried.Clear();
            _primed = false;
            _primeClosed = false;
            _bagOpening = 0;
        }

        private static void BeginSilent(World world, Item container)
        {
            Silent.Add(container.Serial);
            GameActions.DoubleClick(world, container.Serial);
        }

        /// <summary>The contents have arrived (or the server never answered): stop swallowing the open packet and clear the "opened" mark the packet left, as closing a window would.</summary>
        private static void EndSilent(World world, uint serial)
        {
            Silent.Remove(serial);

            Item container = world.Items.Get(serial);

            if (container != null)
            {
                container.Opened = false;
            }
        }

        private static void Prime(World world, Item backpack)
        {
            if (!_primed)
            {
                if (backpack.Items == null && !Client.Game.UO.GameCursor.ItemHold.Enabled)
                {
                    _primed = true;
                    _primeDeadline = Time.Ticks + 3000;
                    _nextBag = Time.Ticks + BagSpacing; // the server limits how fast opens may follow one another
                    BeginSilent(world, backpack);
                }
                else if (backpack.Items != null)
                {
                    _primed = _primeClosed = true; // the player already opened it
                }
            }
            else if (!_primeClosed && (backpack.Items != null || Time.Ticks > _primeDeadline))
            {
                EndSilent(world, backpack.Serial);
                _primeClosed = true;
            }
        }

        private static void PrimeBags(World world, Item backpack)
        {
            if (_bagOpening != 0)
            {
                Item opening = world.Items.Get(_bagOpening);

                if (opening == null || opening.Items != null || Time.Ticks > _bagDeadline)
                {
                    EndSilent(world, _bagOpening);
                    _bagOpening = 0;
                }

                return;
            }

            if (Time.Ticks < _nextBag || Client.Game.UO.GameCursor.ItemHold.Enabled)
            {
                return;
            }

            Item bag = FindUnopenedBag(backpack);

            if (bag == null)
            {
                return;
            }

            BagsTried.Add(bag.Serial);
            _bagOpening = bag.Serial;
            _nextBag = Time.Ticks + BagSpacing;
            _bagDeadline = Time.Ticks + 1500;
            BeginSilent(world, bag);
        }

        private static Item FindUnopenedBag(Item container)
        {
            for (LinkedObject i = container.Items; i != null; i = i.Next)
            {
                var item = (Item)i;

                if (!PlainContainers.Contains(item.Graphic) || !item.ItemData.IsContainer)
                {
                    continue;
                }

                if (item.Items == null && !BagsTried.Contains(item.Serial))
                {
                    return item;
                }

                if (item.Items != null && FindUnopenedBag(item) is { } inner)
                {
                    return inner;
                }
            }

            return null;
        }
    }
}
