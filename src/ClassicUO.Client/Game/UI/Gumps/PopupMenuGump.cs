// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Assets;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    internal class PopupMenuGump : Gump
    {
        private ushort _selectedItem;
        private readonly PopupMenuData _data;
        private readonly HashSet<ushort> _disabled = new HashSet<ushort>();

        public PopupMenuGump(World world, PopupMenuData data) : base(world, 0, 0)
        {
            CanMove = false;
            CanCloseWithRightClick = true;
            _data = data;

            ResizePic pic = new ResizePic(0x0A3C)
            {
                Alpha = 0.75f
            };

            Add(pic);
            int offsetY = 10;
            bool arrowAdded = false;
            int width = 0, height = 20;

            // the shard's actions: their real labels, greyed ones below the rest with the reason as a tooltip, and any explanation the shard sends as a tooltip
            // on an entry that can be chosen (WorldActions, command 0xB2)
            var texts = WorldActions.TakeItemTexts(data.Serial);
            var order = new List<int>(data.Items.Length);

            for (int i = 0; i < data.Items.Length; i++)
            {
                if ((data.Items[i].Flags & 0x01) == 0)
                {
                    order.Add(i);
                }
            }

            for (int i = 0; i < data.Items.Length; i++)
            {
                if ((data.Items[i].Flags & 0x01) != 0)
                {
                    order.Add(i);
                }
            }

            foreach (int i in order)
            {
                ref PopupMenuItem item = ref data.Items[i];

                string text = Client.Game.UO.FileManager.Clilocs.GetString(item.Cliloc);
                string tooltip = null;

                if (texts != null && texts.TryGetValue(item.Index, out var shard))
                {
                    text = shard.Label;
                    tooltip = shard.Reason;
                }

                ushort hue = item.Hue;

                if (item.ReplacedHue != 0)
                {
                    uint h = (HuesHelper.Color16To32(item.ReplacedHue) << 8) | 0xFF;

                    Client.Game.UO.FileManager.Fonts.SetUseHTML(true, h);
                }

                Label label = new Label(text, true, hue, font: 1)
                {
                    X = 10,
                    Y = offsetY
                };

                Client.Game.UO.FileManager.Fonts.SetUseHTML(false);

                // a greyed entry is not lit on hover: its grey text would vanish under the highlight, and it cannot be chosen
                HitBox box = new HitBox(10, offsetY, label.Width, label.Height, alpha: (item.Flags & 0x01) != 0 ? 0f : 0.25f)
                {
                    Tag = item.Index
                };

                box.MouseEnter += (sender, e) =>
                {
                    _selectedItem = (ushort)(sender as HitBox).Tag;
                };

                if ((item.Flags & 0x01) != 0)
                {
                    _disabled.Add(item.Index);
                }

                // a greyed entry's tooltip says why; an entry that can be chosen may carry one that says what it does (the pet friend entries)
                if (!string.IsNullOrEmpty(tooltip))
                {
                    box.SetTooltip(tooltip);
                }

                Add(box);
                Add(label);

                if ((item.Flags & 0x02) != 0 && !arrowAdded)
                {
                    arrowAdded = true;

                    // TODO: wat?
                    Add
                    (
                        new Button(0, 0x15E6, 0x15E2, 0x15E2)
                        {
                            X = 20,
                            Y = offsetY
                        }
                    );

                    height += 20;
                }

                offsetY += label.Height;

                if (!arrowAdded)
                {
                    height += label.Height;

                    if (width < label.Width)
                    {
                        width = label.Width;
                    }
                }
            }

            width += 20;

            if (height <= 10 || width <= 20)
            {
                Dispose();
            }
            else
            {
                pic.Width = width;
                pic.Height = height;

                foreach (HitBox box in FindControls<HitBox>())
                {
                    box.Width = width - 20;
                }
            }
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Left)
            {
                // a greyed entry does nothing; its tooltip says why
                if (_disabled.Contains(_selectedItem))
                {
                    return;
                }

                GameActions.ResponsePopupMenu(_data.Serial, _selectedItem);
                Dispose();
            }
        }
    }
}