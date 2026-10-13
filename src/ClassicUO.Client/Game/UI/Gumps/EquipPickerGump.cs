// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// The combat bar's quick item selector: holding a weapon, shield or tool slot opens this vertical strip of the matching items from the pack and bags, and a click on one
    /// puts it on (the bar does the equipping, taking off whatever is in the way). It is easy to get rid of: Escape, a right or left press anywhere outside it, the mouse wandering
    /// off for a moment, or leaving it alone for a few seconds all close it, and the press that closes it is not also a click on the bar.
    /// </summary>
    internal sealed class EquipPickerGump : Gump
    {
        public const int MaxRows = 7;
        private const int RowWidth = 58;
        private const int RowHeight = 44;
        private const int Pad = 4;
        private const int ArtBox = 36;
        private const int GraceMilliseconds = 150;
        private const int IdleMilliseconds = 9000;
        private const int AwayMilliseconds = 700;
        private const int AwayDistance = 70;

        /// <summary>Until this tick a click on the combat bar is ignored: the press that closed the selector must not also equip or put away.</summary>
        public static long SwallowClickUntil;

        private readonly long _openedAt = Time.Ticks;
        private readonly Rectangle _slot;
        private bool _mouseWasDown = true;
        private long _awaySince;

        public EquipPickerGump(World world, Rectangle slot, IReadOnlyList<Item> items, Action<Item> onPick) : base(world, 0, 0)
        {
            _slot = slot;
            CanMove = false;
            AcceptMouseInput = true;
            CanCloseWithRightClick = true;
            WantUpdateSize = false;
            LayerOrder = UILayer.Over;

            int rows = Math.Min(items.Count, MaxRows);
            int labelRow = items.Count > MaxRows ? 16 : 0;
            Width = RowWidth + 2 * Pad;
            Height = rows * RowHeight + labelRow + 2 * Pad;

            var background = new AlphaBlendControl(0.8f) { Width = Width, Height = Height };
            Add(background);

            for (int i = 0; i < rows; i++)
            {
                Add(new Row(items[i], onPick, this) { X = Pad, Y = Pad + i * RowHeight });
            }

            if (labelRow > 0)
            {
                var more = new Label($"+{items.Count - MaxRows} more", true, 0x0481, 0, 1, FontStyle.BlackBorder) { AcceptMouseInput = false };
                more.X = (Width - more.Width) / 2;
                more.Y = Pad + rows * RowHeight;
                Add(more);
            }

            // above the slot when there is room, since the bar sits at the bottom of the window; below it otherwise
            X = Math.Clamp(slot.X + slot.Width / 2 - Width / 2, 0, Math.Max(0, Client.Game.ClientBounds.Width - Width));
            Y = slot.Y - Height - 4 >= 0 ? slot.Y - Height - 4 : slot.Bottom + 4;
        }

        public override void Update()
        {
            base.Update();

            if (IsDisposed)
            {
                return;
            }

            long now = Time.Ticks;
            bool down = Mouse.LButtonPressed || Mouse.RButtonPressed;
            var strip = new Rectangle(X, Y, Width, Height);
            bool inside = strip.Contains(Mouse.Position);

            // the press that opened the strip is still down when it appears: only a later press, outside the strip, closes it
            if (down && !_mouseWasDown && !inside && now - _openedAt > GraceMilliseconds)
            {
                SwallowClickUntil = now + 400;
                Dispose();

                return;
            }

            _mouseWasDown = down;

            // the mouse has gone off somewhere else: close it after a moment
            Rectangle near = Rectangle.Union(strip, _slot);
            near.Inflate(AwayDistance, AwayDistance);

            if (near.Contains(Mouse.Position))
            {
                _awaySince = 0;
            }
            else if (_awaySince == 0)
            {
                _awaySince = now;
            }
            else if (now - _awaySince > AwayMilliseconds)
            {
                Dispose();

                return;
            }

            if (now - _openedAt > IdleMilliseconds)
            {
                Dispose();
            }
        }

        public void Close() => Dispose();

        private sealed class Row : Control
        {
            private readonly Item _item;
            private readonly Action<Item> _onPick;
            private readonly EquipPickerGump _picker;
            private readonly AlphaBlendControl _highlight;
            private string _tooltip = string.Empty;

            public Row(Item item, Action<Item> onPick, EquipPickerGump picker)
            {
                _item = item;
                _onPick = onPick;
                _picker = picker;
                Width = RowWidth;
                Height = RowHeight - 2;
                AcceptMouseInput = true;
                CanMove = false;
                WantUpdateSize = false;
                _highlight = new AlphaBlendControl(0f) { Width = Width, Height = Height, AcceptMouseInput = false };
                Add(_highlight);
            }

            public override void Update()
            {
                base.Update();
                _highlight.Alpha = MouseIsOver ? 0.45f : 0f;

                string tip = CombatBarGump.TryGetDurability(_item.Serial, out var worn)
                    ? $"{CombatBarGump.NameOf(_item)}. Durability {worn.Hits} of {worn.Max}. Click to hold it."
                    : $"{CombatBarGump.NameOf(_item)}. Click to hold it.";

                if (tip != _tooltip)
                {
                    _tooltip = tip;
                    SetTooltip(tip);
                }
            }

            protected override void OnMouseUp(int x, int y, MouseButtonType button)
            {
                if (button == MouseButtonType.Left)
                {
                    _picker.Close();
                    _onPick(_item);

                    return;
                }

                base.OnMouseUp(x, y, button);
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                CombatBarGump.DrawArt(renderLists, _item.Graphic, _item.Hue, _item.ItemData.IsPartialHue, 1f, x + (Width - ArtBox) / 2, y + 3, ArtBox, ArtBox, layerDepthRef);

                if (CombatBarGump.TryGetDurability(_item.Serial, out var worn))
                {
                    CombatBarGump.DrawDurability(renderLists, worn.Hits, worn.Max, x + Width - 5, y + 6, ArtBox - 6, 1f, layerDepthRef);
                }

                return base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            }
        }
    }
}
