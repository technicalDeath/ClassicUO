// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Configuration;
using System.Collections.Generic;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Assets;
using ClassicUO.Game.Data;

namespace ClassicUO.Game.UI.Gumps.CharCreation
{
    internal class CreateCharProfessionGump : Gump
    {
        private readonly ProfessionInfo _Parent;

        public CreateCharProfessionGump(World world, ProfessionInfo parent = null) : base(world, 0, 0)
        {
            _Parent = parent;

            if (parent == null || !Client.Game.UO.FileManager.Professions.Professions.TryGetValue(parent, out List<ProfessionInfo> professions) || professions == null)
            {
                // The first screen lists only the top-level entries; folders inside folders are reached through their parent.
                professions = new List<ProfessionInfo>(Client.Game.UO.FileManager.Professions.Professions.Keys).FindAll(p => p.TopLevel);
            }

            // Offer only templates whose skills the server's era has: no later-era templates on a UOR shard.
            LockedFeatureFlags offeredFlags = World.ClientLockedFeatures.Flags;
            professions = professions.FindAll(p => CharCreationEra.IsProfessionOffered(p, Client.Game.UO.FileManager.Professions.Professions, offeredFlags));

            /* Build the gump */
            Add
            (
                new ResizePic(2600)
                {
                    X = 100,
                    Y = 80,
                    Width = 470,
                    Height = 372
                }
            );

            Add(new GumpPic(291, 42, 0x0589, 0));
            Add(new GumpPic(214, 58, 0x058B, 0));
            Add(new GumpPic(300, 51, 0x15A9, 0));

            ClilocLoader localization = Client.Game.UO.FileManager.Clilocs;

            bool isAsianLang = string.Compare(Settings.GlobalSettings.Language, "CHT", StringComparison.InvariantCultureIgnoreCase) == 0 ||
                string.Compare(Settings.GlobalSettings.Language, "KOR", StringComparison.InvariantCultureIgnoreCase) == 0 ||
                string.Compare(Settings.GlobalSettings.Language, "JPN", StringComparison.InvariantCultureIgnoreCase) == 0;

            bool unicode = isAsianLang;
            byte font = (byte)(isAsianLang ? 1 : 2);
            ushort hue = (ushort)(isAsianLang ? 0xFFFF : 0x0386);

            Add
            (
                new Label(localization.GetString(3000326, "Choose a Trade for Your Character"), unicode, hue, font: font)
                {
                    X = 158,
                    Y = 132
                }
            );

            for (int i = 0; i < professions.Count; i++)
            {
                int cx = i % 2;
                int cy = i >> 1;

                Add
                (
                    new ProfessionInfoGump(professions[i])
                    {
                        X = 145 + cx * 195,
                        Y = 168 + cy * 70,

                        Selected = SelectProfession
                    }
                );
            }

            Add
            (
                new Button((int) Buttons.Prev, 0x15A1, 0x15A3, 0x15A2)
                {
                    X = 586,
                    Y = 445,
                    ButtonAction = ButtonAction.Activate
                }
            );
        }

        public void SelectProfession(ProfessionInfo info)
        {
            if (info.Type == ProfessionLoader.PROF_TYPE.CATEGORY && Client.Game.UO.FileManager.Professions.Professions.TryGetValue(info, out List<ProfessionInfo> list) && list != null)
            {
                Parent.Add(new CreateCharProfessionGump(World, info));
                Parent.Remove(this);
            }
            else
            {
                CharCreationGump charCreationGump = UIManager.GetGump<CharCreationGump>();

                charCreationGump?.SetProfession(info);
            }
        }

        public override void OnButtonClick(int buttonID)
        {
            switch ((Buttons) buttonID)
            {
                case Buttons.Prev:

                {
                    if (_Parent != null)
                    {
                        // Up one level: the folder this one sits in, or the first screen for a top-level folder.
                        Parent.Add(new CreateCharProfessionGump(World, _Parent.ParentCategory));
                        Parent.Remove(this);
                    }
                    else
                    {
                        Parent.Remove(this);
                        CharCreationGump charCreationGump = UIManager.GetGump<CharCreationGump>();
                        charCreationGump?.StepBack();
                    }

                    break;
                }
            }

            base.OnButtonClick(buttonID);
        }

        private enum Buttons
        {
            Prev
        }
    }

    internal class ProfessionInfoGump : Control
    {
        private readonly ProfessionInfo _info;

        public ProfessionInfoGump(ProfessionInfo info)
        {
            _info = info;

            ClilocLoader localization = Client.Game.UO.FileManager.Clilocs;

            ResizePic background = new ResizePic(3000)
            {
                Width = 175,
                Height = 34
            };

            // A template the shard defines itself carries its own text; the others use the client's string table.
            string description = info.Description != 0 ? localization.GetString(info.Description) : info.DescriptionText;
            background.SetTooltip(description, 250);

            Add(background);

            Add
            (
                new Label(info.Localization != 0 ? localization.GetString(info.Localization) : info.Name, true, 0x00, font: 1)
                {
                    X = 7,
                    Y = 8
                }
            );

            Add(new GumpPic(121, -12, info.Graphic, 0));
        }

        public Action<ProfessionInfo> Selected;

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            base.OnMouseUp(x, y, button);

            if (button == MouseButtonType.Left)
            {
                Selected?.Invoke(_info);
            }
        }
    }
}