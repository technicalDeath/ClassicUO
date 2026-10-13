// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.IO;
using ClassicUO.Assets;
using ClassicUO.Network;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    internal class TopBarGump : Gump
    {
        private TopBarGump(World world) : base(world, 0, 0)
        {
            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;

            // little
            Add(new ResizePic(0x13BE) { Width = 30, Height = 27 }, 2);

            Add(
                new Button(0, 0x15A1, 0x15A1, 0x15A1)
                {
                    X = 5,
                    Y = 3,
                    ToPage = 1
                },
                2
            );

            // big
            int smallWidth = 50;
            ref readonly var gumpInfo = ref Client.Game.UO.Gumps.GetGump(0x098B);
            if (gumpInfo.Texture != null)
            {
                smallWidth = gumpInfo.UV.Width;
            }

            int largeWidth = 100;

            gumpInfo = ref Client.Game.UO.Gumps.GetGump(0x098D);
            if (gumpInfo.Texture != null)
            {
                largeWidth = gumpInfo.UV.Width;
            }

            var cliloc = Client.Game.UO.FileManager.Clilocs;
            var entries = new List<(Buttons Id, bool Large, string Text)>();

            if ((ShardMask & ShardBar) != 0)
            {
                // the shard's bar: only buttons that do something here, and a feature's button only while the server says the feature is on
                entries.Add((Buttons.Paperdoll, true, cliloc.GetString(3000133, ResGumps.Paperdoll)));
                entries.Add((Buttons.Inventory, true, "Backpack"));
                entries.Add((Buttons.Skills, false, "Skills"));

                entries.Add((Buttons.Journal, true, cliloc.GetString(3000129, ResGumps.Journal)));
                entries.Add((Buttons.Map, false, cliloc.GetString(3000430, ResGumps.Map)));
                entries.Add((Buttons.Names, false, "Names"));

                if ((ShardMask & CampBit) != 0)
                {
                    entries.Add((Buttons.Camp, false, "Camp"));
                }

                if ((ShardMask & GuildChatBit) != 0)
                {
                    entries.Add((Buttons.GuildChat, true, "Guild chat"));
                }

                entries.Add((Buttons.Help, false, (ShardMask & AboutBit) != 0 ? "About" : cliloc.GetString(3000134, ResGumps.Help)));
                entries.Add((Buttons.Options, true, "Options"));
            }
            else
            {
                entries.Add((Buttons.Map, false, cliloc.GetString(3000430, ResGumps.Map)));
                entries.Add((Buttons.Paperdoll, true, cliloc.GetString(3000133, ResGumps.Paperdoll)));
                entries.Add((Buttons.Inventory, true, cliloc.GetString(3000431, ResGumps.Inventory)));
                entries.Add((Buttons.Journal, true, cliloc.GetString(3000129, ResGumps.Journal)));
                entries.Add((Buttons.Chat, false, cliloc.GetString(3000131, ResGumps.Chat)));
                entries.Add((Buttons.Help, false, cliloc.GetString(3000134, ResGumps.Help)));
                entries.Add((Buttons.WorldMap, true, StringHelper.CapitalizeAllWords(cliloc.GetString(1015233, ResGumps.WorldMap))));
                entries.Add((Buttons.Info, false, cliloc.GetString(1079449, ResGumps.Info)));
                entries.Add((Buttons.Debug, false, cliloc.GetString(1042237, ResGumps.Debug)));
                entries.Add((Buttons.NetStats, true, cliloc.GetString(3000169, ResGumps.NetStats)));

                if (Client.Game.UO.Version >= ClientVersion.CV_706400)
                {
                    entries.Add((Buttons.UOStore, true, cliloc.GetString(1158008, ResGumps.UOStore)));
                    entries.Add((Buttons.GlobalChat, true, cliloc.GetString(1158390, ResGumps.GlobalChat)));
                }
            }

            ResizePic background;

            Add(background = new ResizePic(0x13BE) { Height = 27 }, 1);

            Add(
                new Button(0, 0x15A4, 0x15A4, 0x15A4)
                {
                    X = 5,
                    Y = 3,
                    ToPage = 2
                },
                1
            );

            int startX = 30;

            foreach (var (id, large, text) in entries)
            {
                ushort graphic = (ushort)(large ? 0x098D : 0x098B);

                if (id == Buttons.Names)
                {
                    // two buttons on the same spot, one shown at a time (Update): the same word in white while the names are hidden and in green while they are shown
                    _namesOff = MakeButton(id, graphic, text, 0, startX);
                    _namesOn = MakeButton(id, graphic, text, NamesOnHue, startX);
                    _namesOff.SetTooltip(NamesTip);
                    _namesOn.SetTooltip(NamesTip);
                    _namesOn.IsVisible = world.NameOverHeadManager.IsToggled;
                    _namesOff.IsVisible = !_namesOn.IsVisible;
                    Add(_namesOff, 1);
                    Add(_namesOn, 1);
                }
                else
                {
                    Add(MakeButton(id, graphic, text, 0, startX), 1);
                }

                startX += (large ? largeWidth : smallWidth) + 1;
                background.Width = startX;
            }

            background.Width = startX + 1;

            //layer
            LayerOrder = UILayer.Over;
        }

        public bool IsMinimized { get; private set; }

        private const ushort NamesOnHue = 0x0044;
        private const string NamesTip = "Show the name of everyone and everything near you. Click again to hide them. Alt+N does the same, and so does holding Ctrl+Shift.";

        private RighClickableButton _namesOff, _namesOn;

        private static RighClickableButton MakeButton(Buttons id, ushort graphic, string text, ushort normalHue, int x) =>
            new((int)id, graphic, graphic, graphic, text, 1, true, normalHue, 0x0036)
            {
                ButtonAction = ButtonAction.Activate,
                X = x,
                Y = 1,
                FontCenter = true
            };

        public override void Update()
        {
            base.Update();

            // the Names button follows the state, whoever changed it (the button, Alt+N, the macro, the Options window)
            if (!IsDisposed && _namesOn != null && _namesOff != null)
            {
                bool on = World.NameOverHeadManager.IsToggled;

                _namesOn.IsVisible = on;
                _namesOff.IsVisible = !on;
            }
        }

        /// <summary>The shard's top bar is in use (the server sent its menu mask).</summary>
        public static bool IsShardBar => (ShardMask & ShardBar) != 0;

        // The shard's bar (MenuBarService, command 0xA8): until the server's mask arrives, and when it never does, the stock bar is built.
        public const int Command = 0xA8;
        private const ushort ShardBar = 1 << 15;
        private const ushort CampBit = 1 << 1;
        private const ushort GuildChatBit = 1 << 2;
        private const ushort AboutBit = 1 << 3;
        private const ushort AutoUseBit = 1 << 4;

        private static ushort ShardMask;

        public static void ResetShardMask() => ShardMask = 0;

        /// <summary>The shard allows the bars' auto-use options (the gear buttons on the potion and combat bars).</summary>
        public static bool AutoUseAllowed => (ShardMask & ShardBar) != 0 && (ShardMask & AutoUseBit) != 0;

        /// <summary>What the Help button does: the shard's Welcome guide (it has the Skill Guide and Page staff buttons) when the shard says so, the stock page-a-GM request otherwise. The paperdoll's Help button does the same.</summary>
        public static void OpenHelp()
        {
            if ((ShardMask & ShardBar) != 0 && (ShardMask & AboutBit) != 0)
            {
                GameActions.Say("[Welcome");
            }
            else
            {
                GameActions.RequestHelp();
            }
        }

        public static void OnMaskReply(World world, ref StackDataReader p)
        {
            ushort mask = p.Remaining >= 2 ? p.ReadUInt16BE() : (ushort)0;

            if (mask == ShardMask)
            {
                return;
            }

            ShardMask = mask;

            if (UIManager.GetGump<TopBarGump>() is { } bar)
            {
                bar.Dispose();
                Create(world);
            }
        }

        public static void Create(World world)
        {
            TopBarGump gump = UIManager.GetGump<TopBarGump>();

            if (gump == null)
            {
                if (
                    ProfileManager.CurrentProfile.TopbarGumpPosition.X < 0
                    || ProfileManager.CurrentProfile.TopbarGumpPosition.Y < 0
                )
                {
                    ProfileManager.CurrentProfile.TopbarGumpPosition = Point.Zero;
                }

                if (ShardMask == 0 && NetClient.Socket.IsConnected)
                {
                    NetClient.Socket.Send_MenuBarRequest();
                }

                UIManager.Add(
                    gump = new TopBarGump(world)
                    {
                        X = ProfileManager.CurrentProfile.TopbarGumpPosition.X,
                        Y = ProfileManager.CurrentProfile.TopbarGumpPosition.Y
                    }
                );

                if (ProfileManager.CurrentProfile.TopbarGumpIsMinimized)
                {
                    gump.ChangePage(2);
                }
            }
            else
            {
                Log.Error(ResGumps.TopBarGumpAlreadyExists);
            }
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Right && (X != 0 || Y != 0))
            {
                X = 0;
                Y = 0;

                ProfileManager.CurrentProfile.TopbarGumpPosition = Location;
            }
        }

        public override void OnPageChanged()
        {
            ProfileManager.CurrentProfile.TopbarGumpIsMinimized = IsMinimized = ActivePage == 2;
            WantUpdateSize = true;
        }

        protected override void OnDragEnd(int x, int y)
        {
            base.OnDragEnd(x, y);
            ProfileManager.CurrentProfile.TopbarGumpPosition = Location;
        }

        public override void OnButtonClick(int buttonID)
        {
            switch ((Buttons)buttonID)
            {
                case Buttons.Map:
                    if ((ShardMask & ShardBar) != 0 && UIManager.GetGump<MiniMapGump>() is { } mini)
                    {
                        // the shard bar's one Map button: the mini-map first, the big map on the next click
                        mini.Dispose();
                        GameActions.OpenWorldMap(World);
                    }
                    else
                    {
                        GameActions.OpenMiniMap(World);
                    }

                    break;

                case Buttons.Skills:
                    // a second click on the button puts the window away again, as a new player expects of a button that opened it
                    if (UIManager.GetGump<StandardSkillsGump>() is { IsMinimized: false } open)
                    {
                        open.Dispose();
                    }
                    else
                    {
                        GameActions.OpenSkills(World);
                    }

                    break;

                case Buttons.Camp:
                    GameActions.Say("[CampTravel");

                    break;

                case Buttons.Names:
                    World.NameOverHeadManager.ToggleOverheads();
                    GameActions.Print(
                        World,
                        World.NameOverHeadManager.IsToggled ? "Names are shown. Click Names again, or press Alt+N, to hide them." : "Names are hidden.",
                        0x35,
                        MessageType.System
                    );

                    break;

                case Buttons.GuildChat:
                    if (UIManager.SystemChat is { } chat)
                    {
                        chat.IsActive = true;
                        chat.Mode = ChatMode.Guild;
                    }

                    break;

                case Buttons.Options:
                    GameActions.OpenSettings(World);

                    break;

                case Buttons.Paperdoll:
                    GameActions.OpenPaperdoll(World, World.Player);

                    break;

                case Buttons.Inventory:
                    GameActions.OpenBackpack(World);

                    break;

                case Buttons.Journal:
                    GameActions.OpenJournal(World);

                    break;

                case Buttons.Chat:
                    GameActions.OpenChat(World);

                    break;

                case Buttons.GlobalChat:
                    Log.Warn(ResGumps.ChatButtonPushedNotImplementedYet);
                    GameActions.Print(
                        World,
                        ResGumps.GlobalChatNotImplementedYet,
                        0x23,
                        MessageType.System
                    );

                    break;

                case Buttons.UOStore:
                    if (Client.Game.UO.Version >= ClientVersion.CV_706400)
                    {
                        NetClient.Socket.Send_OpenUOStore();
                    }

                    break;

                case Buttons.Help:
                    OpenHelp();

                    break;

                case Buttons.Debug:

                    DebugGump debugGump = UIManager.GetGump<DebugGump>();

                    if (debugGump == null)
                    {
                        debugGump = new DebugGump(World, 100, 100);
                        UIManager.Add(debugGump);
                    }
                    else
                    {
                        debugGump.IsVisible = !debugGump.IsVisible;
                        debugGump.SetInScreen();
                    }

                    break;

                case Buttons.NetStats:
                    NetworkStatsGump netstatsgump = UIManager.GetGump<NetworkStatsGump>();

                    if (netstatsgump == null)
                    {
                        netstatsgump = new NetworkStatsGump(World, 100, 100);
                        UIManager.Add(netstatsgump);
                    }
                    else
                    {
                        netstatsgump.IsVisible = !netstatsgump.IsVisible;
                        netstatsgump.SetInScreen();
                    }

                    break;

                case Buttons.WorldMap:
                    GameActions.OpenWorldMap(World);

                    break;
            }
        }

        private enum Buttons
        {
            Map,
            Paperdoll,
            Inventory,
            Journal,
            Chat,
            Help,
            WorldMap,
            Info,
            Debug,
            NetStats,
            UOStore,
            GlobalChat,
            Skills,
            Camp,
            GuildChat,
            Options,
            Names
        }

        private class RighClickableButton : Button
        {
            public RighClickableButton(
                int buttonID,
                ushort normal,
                ushort pressed,
                ushort over = 0,
                string caption = "",
                byte font = 0,
                bool isunicode = true,
                ushort normalHue = ushort.MaxValue,
                ushort hoverHue = ushort.MaxValue
            ) : base(buttonID, normal, pressed, over, caption, font, isunicode, normalHue, hoverHue)
            { }

            public RighClickableButton(List<string> parts) : base(parts) { }

            protected override void OnMouseUp(int x, int y, MouseButtonType button)
            {
                base.OnMouseUp(x, y, button);
                Parent?.InvokeMouseUp(new Point(x, y), button);
            }
        }
    }
}
