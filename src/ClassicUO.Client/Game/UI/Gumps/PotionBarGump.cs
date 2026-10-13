// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.IO;
using ClassicUO.Network;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// The shard's potion bar: a strip of potion cells just below the game window, each with the potion's art, a count, and a size that tells lesser from greater (they share
    /// art). A left click drinks one; when both hands are busy the weapon is taken off first and put back after. The server says which strength a potion is (the client cannot
    /// tell: they share graphic and hue): see ShardContent PotionBarService, extended command 0xA7.
    /// </summary>
    internal sealed class PotionBarGump : Gump
    {
        /// <summary>The shard's extended command for potion strengths, both ways.</summary>
        public const ushort Command = 0xA7;

        /// <summary>One cell of the bar. The code is 10 x the kind (1 heal, 2 cure, 3 refresh, 4 agility, 5 strength) plus the strength (0 lesser, 1 regular, 2 greater).</summary>
        public readonly record struct PotionCell(byte Code, ushort Graphic, string Name, int Size);

        public static readonly PotionCell[] Cells =
        {
            new(10, 0x0F0C, "Lesser heal potion", 26), new(11, 0x0F0C, "Heal potion", 32), new(12, 0x0F0C, "Greater heal potion", 39),
            new(20, 0x0F07, "Lesser cure potion", 26), new(21, 0x0F07, "Cure potion", 32), new(22, 0x0F07, "Greater cure potion", 39),
            new(31, 0x0F0B, "Refresh potion", 32), new(32, 0x0F0B, "Total refresh potion", 39),
            new(41, 0x0F08, "Agility potion", 32), new(42, 0x0F08, "Greater agility potion", 39),
            new(51, 0x0F09, "Strength potion", 32), new(52, 0x0F09, "Greater strength potion", 39)
        };

        private static readonly HashSet<ushort> PotionGraphics = new() { 0x0F0C, 0x0F07, 0x0F0B, 0x0F08, 0x0F09 };

        /// <summary>Item serial to potion code, as the server last said.</summary>
        public static readonly Dictionary<uint, byte> Strengths = new();

        private static readonly Dictionary<uint, long> Asked = new();

        private const ushort LargerIcon = 62050;  // the shard's brass diamonds, see ShardContent/tools/gump-art/make_pack_icons.py
        private const ushort SmallerIcon = 62051;
        private const int ArtHeight = 39;       // the tallest bottle; every bottle stands on the same line
        private const int LabelRow = 13;        // the count sits centred under its bottle
        private const int CellHeight = ArtHeight + LabelRow;
        private const int Pad = 4;
        private const int Gap = 8;
        private const int IconSize = 19;        // the brass diamonds, drawn from 23 px art
        private const int BarHeight = CellHeight + 2 * Pad;
        private const int DockedHeight = IconSize + 2 * Pad + 4;

        private const int Full = 0;
        private const int Shrunk = 1;
        private const int Docked = 2;
        private const int ScanMilliseconds = 250;
        private const int ActionDelay = 700;      // the server limits how fast actions may follow one another
        private const int StepTimeout = 2500;

        private readonly Dictionary<byte, int> _counts = new();
        private readonly List<Item> _potions = new();
        private readonly List<Cell> _cellControls = new();
        private AlphaBlendControl _background;
        private long _nextScan;
        private long _nextAsk;
        private bool _moved;
        private bool _restored;

        /// <summary>The player has put this bar where they want it, so the bars that stack under it do not follow it.</summary>
        public bool IsMoved => _moved;
        private int _mode;
        private bool _built;
        private bool _gear;

        // drinking
        private enum Stage { Idle, HandFree, Drinking, ReEquip }

        private Stage _stage;
        private uint _potionSerial;
        private uint _weaponSerial;
        private Layer _weaponLayer;
        private long _deadline;
        private long _nextAt;
        private long _lockUntil;

        public PotionBarGump(World world) : base(world, 0, 0)
        {
            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;
            _mode = ProfileManager.CurrentProfile == null ? Full : Math.Clamp(ProfileManager.CurrentProfile.PotionBarMode, Full, Docked);

            if (ProfileManager.CurrentProfile is { PotionBarX: >= 0, PotionBarY: >= 0 } saved)
            {
                X = saved.PotionBarX;
                Y = saved.PotionBarY;
                _moved = true;
                _restored = true;
            }
            Rebuild();
        }

        // ---- the server's answer

        /// <summary>The server says which strength each of the asked potions is.</summary>
        public static void OnStrengthReply(ref StackDataReader p)
        {
            int count = p.ReadUInt8();

            for (int i = 0; i < count && p.Remaining >= 5; i++)
            {
                uint serial = p.ReadUInt32BE();
                byte code = p.ReadUInt8();

                if (code != 0)
                {
                    Strengths[serial] = code;
                }

                Asked.Remove(serial);
            }
        }

        // ---- layout

        private bool Shows(PotionCell cell) =>
            _mode == Full || (_mode == Shrunk && _counts.TryGetValue(cell.Code, out int n) && n > 0);

        private void Rebuild()
        {
            Clear();
            _cellControls.Clear();

            _background = new AlphaBlendControl(0.6f);
            Add(_background);

            int x = Pad;

            if (_mode == Docked)
            {
                // the whole bar folded away: a small strip with an empty bottle, which opens it again
                int bottleY = (DockedHeight - IconSize) / 2;

                Add(new ArtIcon(0x0F0E, IconSize) { X = x, Y = bottleY });
                AddButton(x, bottleY, IconSize, IconSize, "Show the potion bar", () => SetMode(Shrunk));
                Width = x + IconSize + Pad;
                Height = DockedHeight;
                _background.Width = Width;
                _background.Height = Height;
                _built = true;

                return;
            }

            int iconY = Pad + (ArtHeight - IconSize) / 2;     // centred on the bottles, not on the counts under them

            if (_mode == Full)
            {
                AddDiamond(ref x, iconY, SmallerIcon, "Show only the potions you have", () => SetMode(Shrunk));
            }
            else
            {
                AddDiamond(ref x, iconY, SmallerIcon, "Fold the bar away", () => SetMode(Docked));
                AddDiamond(ref x, iconY, LargerIcon, "Show every potion", () => SetMode(Full));
            }

            foreach (PotionCell cell in Cells)
            {
                if (!Shows(cell))
                {
                    continue;
                }

                var control = new Cell(this, cell) { X = x, Y = Pad };
                _cellControls.Add(control);
                Add(control);
                x += control.Width + Gap;
            }

            // the options button, when the shard allows auto-use
            _gear = AutoUse.Allowed;

            if (_gear)
            {
                x += GroupGap - Gap;
                int gearY = Pad + (ArtHeight - IconSize) / 2;
                var cog = new GumpPic(x, gearY, GearIcon, 0) { AcceptMouseInput = false };

                if (cog.Width > 0)
                {
                    cog.Width = cog.Height = IconSize;
                }

                Add(cog);
                AddButton(x, gearY, IconSize, IconSize, "Options: drink potions for you at set levels", OpenOptions);
                x += IconSize + Gap;
            }

            Width = x - Gap + Pad;
            Height = BarHeight;
            _background.Width = Width;
            _background.Height = Height;
            _built = true;
            Refresh();
        }

        private const ushort GearIcon = 62052;
        private const int GroupGap = 13;

        private void OpenOptions()
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null)
            {
                return;
            }

            BarOptionsGump.Toggle(
                World, this, "Potion bar options",
                new[]
                {
                    AutoUse.OptionFor(profile, AutoUseOption.HealPotion),
                    AutoUse.OptionFor(profile, AutoUseOption.RefreshPotion),
                    AutoUse.OptionFor(profile, AutoUseOption.Cure)
                }
            );
        }

        private void AddDiamond(ref int x, int y, ushort graphic, string tooltip, Action onClick)
        {
            var icon = new GumpPic(x, y, graphic, 0) { AcceptMouseInput = false };

            if (icon.Width > 0)
            {
                icon.Width = icon.Height = IconSize;
            }

            Add(icon);
            AddButton(x, y, IconSize, IconSize, tooltip, onClick);
            x += IconSize + Gap - 3;
        }

        private void AddButton(int x, int y, int width, int height, string tooltip, Action onClick)
        {
            var hit = new HitBox(x, y, width, height) { CanMove = true };   // a drag that starts on a button moves the bar; only a click that stays put presses it

            hit.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtonType.Left && Math.Abs(Mouse.LDragOffset.X) <= 3 && Math.Abs(Mouse.LDragOffset.Y) <= 3)
                {
                    onClick();
                }
            };

            hit.SetTooltip(tooltip);
            Add(hit);
        }

        private void SetMode(int mode)
        {
            _mode = mode;

            if (ProfileManager.CurrentProfile != null)
            {
                ProfileManager.CurrentProfile.PotionBarMode = mode;
                ProfileManager.CurrentProfile.Save(World, ProfileManager.ProfilePath);
            }

            Rebuild();
        }

        private void Refresh()
        {
            foreach (Cell cell in _cellControls)
            {
                cell.SetCount(_counts.TryGetValue(cell.Info.Code, out int n) ? n : 0);
            }
        }

        // ---- the watch

        public override void Update()
        {
            base.Update();

            if (IsDisposed || World.Player == null)
            {
                return;
            }

            if (Time.Ticks >= _nextScan)
            {
                _nextScan = Time.Ticks + ScanMilliseconds;
                Scan();
            }

            Place();
            StepDrink();
            AutoUse.Tick(World);
        }

        private void Scan()
        {
            Item backpack = World.Player.FindItemByLayer(Layer.Backpack);
            _potions.Clear();

            if (backpack != null)
            {
                PackPrimer.Tick(World);
                Collect(backpack);
            }

            var counts = new Dictionary<byte, int>();
            var unknown = new List<uint>();

            foreach (Item potion in _potions)
            {
                if (Strengths.TryGetValue(potion.Serial, out byte code))
                {
                    counts[code] = counts.GetValueOrDefault(code) + Math.Max(1, (int)potion.Amount);
                }
                else if (!Asked.TryGetValue(potion.Serial, out long at) || Time.Ticks - at > 4000)
                {
                    unknown.Add(potion.Serial);
                }
            }

            if (unknown.Count > 0 && Time.Ticks >= _nextAsk)
            {
                _nextAsk = Time.Ticks + 500;

                if (unknown.Count > 64)
                {
                    unknown.RemoveRange(64, unknown.Count - 64);
                }

                foreach (uint serial in unknown)
                {
                    Asked[serial] = Time.Ticks;
                }

                NetClient.Socket.Send_PotionStrengthRequest(unknown);
            }

            bool layoutChanged = false;

            if (_mode == Shrunk)
            {
                foreach (PotionCell cell in Cells)
                {
                    bool had = _counts.TryGetValue(cell.Code, out int before) && before > 0;
                    bool has = counts.TryGetValue(cell.Code, out int now) && now > 0;
                    layoutChanged |= had != has;
                }
            }

            _counts.Clear();

            foreach (KeyValuePair<byte, int> pair in counts)
            {
                _counts[pair.Key] = pair.Value;
            }

            if (layoutChanged || !_built || _gear != AutoUse.Allowed)
            {
                Rebuild();
            }
            else
            {
                Refresh();
            }
        }

        private void Collect(Item container)
        {
            for (LinkedObject i = container.Items; i != null; i = i.Next)
            {
                var item = (Item)i;

                if (PotionGraphics.Contains(item.Graphic))
                {
                    _potions.Add(item);
                }
                else if (item.ItemData.IsContainer && !item.IsEmpty)
                {
                    Collect(item);
                }
            }
        }

        /// <summary>Just below the game window, in the black, when there is room; hidden when there is not. Once the player drags it, it stays where they put it.</summary>
        private void Place()
        {
            if (_moved)
            {
                if (_restored)
                {
                    // the saved position may not fit a smaller window now
                    _restored = false;
                    SetInScreen();
                }

                IsVisible = true;

                return;
            }

            GameScene scene = Client.Game.GetScene<GameScene>();

            if (scene == null)
            {
                return;
            }

            Rectangle view = scene.Camera.Bounds;
            int room = Client.Game.ClientBounds.Height - (view.Y + view.Height);

            if (room < Height + 4)
            {
                IsVisible = false;

                return;
            }

            IsVisible = true;
            X = view.X;
            Y = view.Y + view.Height + 4;
        }

        /// <summary>The bar places itself every frame until it has been moved; this must be known when the drag begins, or it would snap back under the mouse.</summary>
        protected override void OnDragBegin(int x, int y)
        {
            _moved = true;
            base.OnDragBegin(x, y);
        }

        protected override void OnDragEnd(int x, int y)
        {
            _moved = true;
            base.OnDragEnd(x, y);

            if (ProfileManager.CurrentProfile is { } profile)
            {
                profile.PotionBarX = X;
                profile.PotionBarY = Y;
                profile.Save(World, ProfileManager.ProfilePath);
            }
        }

        /// <summary>Back to the bar's own place under the game window (Options, the shard bars section, Default).</summary>
        public void ResetPosition()
        {
            _moved = false;
            _restored = false;

            if (ProfileManager.CurrentProfile is { } profile)
            {
                profile.PotionBarX = -1;
                profile.PotionBarY = -1;
            }
        }

        // ---- drinking

        private Item FindPotion(byte code)
        {
            foreach (Item potion in _potions)
            {
                if (Strengths.TryGetValue(potion.Serial, out byte c) && c == code)
                {
                    return potion;
                }
            }

            return null;
        }

        /// <summary>The server's rule (BasePotion.HasFreeHand): a hand is free unless both are in use, and a two-handed weapon uses both. Gives the item to take off when it is not.</summary>
        public static bool HasFreeHand(Mobile player, out Item toRemove)
        {
            Item one = player.FindItemByLayer(Layer.OneHanded);
            Item two = player.FindItemByLayer(Layer.TwoHanded);
            toRemove = null;

            if (two != null && two.ItemData.IsWeapon)
            {
                toRemove = two;
                one = two;
            }

            if (one == null || two == null)
            {
                return true;
            }

            toRemove ??= one;

            return false;
        }

        /// <summary>Whether a drink could start now: nothing half-done, the server's pace respected, no item in the cursor's hand.</summary>
        public bool CanAct => _stage == Stage.Idle && Time.Ticks >= _lockUntil && !Client.Game.UO.GameCursor.ItemHold.Enabled;

        /// <summary>Drinks the first potion of these codes (strongest first) that is in the pack. Returns false when there is none or a drink cannot start.</summary>
        public bool DrinkStrongest(byte[] codesStrongestFirst)
        {
            if (!CanAct)
            {
                return false;
            }

            foreach (byte code in codesStrongestFirst)
            {
                if (FindPotion(code) is { } potion)
                {
                    return DrinkItem(potion);
                }
            }

            return false;
        }

        /// <summary>Drinks this potion, taking the weapon off first when both hands are busy (the same steps as a click on the bar).</summary>
        public bool DrinkItem(Item potion)
        {
            if (!CanAct || potion == null)
            {
                return false;
            }

            StartDrink(potion);

            return true;
        }

        private void Drink(PotionCell cell)
        {
            if (_stage != Stage.Idle || Time.Ticks < _lockUntil || Client.Game.UO.GameCursor.ItemHold.Enabled)
            {
                return;
            }

            Item potion = FindPotion(cell.Code);

            if (potion == null)
            {
                GameActions.Print(World, $"You have no {cell.Name.ToLowerInvariant()}.");

                return;
            }

            StartDrink(potion);
        }

        private void StartDrink(Item potion)
        {
            _potionSerial = potion.Serial;
            _lockUntil = Time.Ticks + ActionDelay;

            if (HasFreeHand(World.Player, out Item weapon))
            {
                GameActions.DoubleClick(World, potion.Serial);

                return;
            }

            Item backpack = World.Player.FindItemByLayer(Layer.Backpack);

            if (backpack == null)
            {
                return;
            }

            _weaponSerial = weapon.Serial;
            _weaponLayer = weapon.Layer;
            GameActions.PickUp(World, weapon, 0, 0, 1);
            GameActions.DropItem(Client.Game.UO.GameCursor.ItemHold.Serial, 0xFFFF, 0xFFFF, 0, backpack.Serial);
            _stage = Stage.HandFree;
            _nextAt = Time.Ticks + ActionDelay;
            _deadline = Time.Ticks + StepTimeout;
        }

        private void StepDrink()
        {
            if (_stage == Stage.Idle)
            {
                return;
            }

            long now = Time.Ticks;

            switch (_stage)
            {
                case Stage.HandFree:
                    if (now < _nextAt)
                    {
                        break;
                    }

                    if (HasFreeHand(World.Player, out _))
                    {
                        GameActions.DoubleClick(World, _potionSerial);
                        _stage = Stage.Drinking;
                        _nextAt = now + ActionDelay;
                        _deadline = now + StepTimeout;
                    }
                    else if (now > _deadline)
                    {
                        _stage = Stage.Idle; // the weapon never came off: nothing to put back
                    }

                    break;

                case Stage.Drinking:
                    if (now >= _nextAt && (World.Items.Get(_potionSerial) == null || now > _deadline))
                    {
                        ReEquip(now);
                    }

                    break;

                case Stage.ReEquip:
                    if (World.Player.FindItemByLayer(_weaponLayer) != null || now > _deadline)
                    {
                        _stage = Stage.Idle;
                        _lockUntil = now + ActionDelay;
                    }

                    break;
            }
        }

        private void ReEquip(long now)
        {
            Item weapon = World.Items.Get(_weaponSerial);

            if (weapon != null && !Client.Game.UO.GameCursor.ItemHold.Enabled)
            {
                GameActions.PickUp(World, weapon.Serial, 0, 0, 1);
                GameActions.Equip(World);
            }

            _stage = Stage.ReEquip;
            _deadline = now + StepTimeout;
        }

        // ---- the docked bar's empty bottle

        private sealed class ArtIcon : Control
        {
            private readonly ushort _graphic;

            public ArtIcon(ushort graphic, int height)
            {
                _graphic = graphic;
                Width = Height = height;
                AcceptMouseInput = false;
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float layerDepth = layerDepthRef;
                ref readonly var art = ref Client.Game.UO.Arts.GetArt(_graphic);

                if (art.Texture != null)
                {
                    var texture = art.Texture;
                    Rectangle real = Client.Game.UO.Arts.GetRealArtBounds(_graphic);
                    Rectangle source = new Rectangle(art.UV.X + real.X, art.UV.Y + real.Y, real.Width, real.Height);
                    float scale = Height / (float)Math.Max(1, real.Height);
                    int w = Math.Max(1, (int)(real.Width * scale));
                    Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, 1f);
                    int left = x + (Width - w) / 2;

                    renderLists.AddGumpWithAtlas(
                        batcher =>
                        {
                            batcher.Draw(texture, new Rectangle(left, y, w, Height), source, hue, layerDepth);

                            return true;
                        }
                    );
                }

                return base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            }
        }

        // ---- one cell

        private sealed class Cell : Control
        {
            private readonly PotionBarGump _bar;
            private readonly Label _count;
            private int _shown = -1;

            public Cell(PotionBarGump bar, PotionCell info)
            {
                _bar = bar;
                Info = info;
                Width = Math.Max(22, BottleWidth(info));
                Height = CellHeight;
                AcceptMouseInput = true;
                CanMove = true;
                WantUpdateSize = false;

                _count = new Label("0", true, 0x0481, 0, 1, FontStyle.BlackBorder) { AcceptMouseInput = false };
                Add(_count);
            }

            /// <summary>The bottle's width when its height is the cell's size (the art has transparent margins, so the visible pixels decide).</summary>
            private static int BottleWidth(PotionCell info)
            {
                Rectangle real = Client.Game.UO.Arts.GetRealArtBounds(info.Graphic);

                return real.Height <= 0 ? info.Size : (int)Math.Round(real.Width * info.Size / (float)real.Height);
            }

            public PotionCell Info { get; }

            public void SetCount(int count)
            {
                if (count == _shown)
                {
                    return;
                }

                _shown = count;
                _count.Text = count.ToString();
                _count.X = (Width - _count.Width) / 2;
                _count.Y = ArtHeight + 1;
                Alpha = count > 0 ? 1f : 0.35f;
                SetTooltip(count > 0 ? $"{Info.Name} ({count}). Click to drink one." : $"{Info.Name}: you have none.");
            }

            protected override void OnMouseUp(int x, int y, MouseButtonType button)
            {
                if (button == MouseButtonType.Left && Math.Abs(Mouse.LDragOffset.X) < 4 && Math.Abs(Mouse.LDragOffset.Y) < 4 && _shown > 0)
                {
                    _bar.Drink(Info);

                    return;
                }

                base.OnMouseUp(x, y, button);
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float layerDepth = layerDepthRef;
                ref readonly var art = ref Client.Game.UO.Arts.GetArt(Info.Graphic);

                if (art.Texture != null)
                {
                    var texture = art.Texture;
                    Rectangle real = Client.Game.UO.Arts.GetRealArtBounds(Info.Graphic);
                    Rectangle source = new Rectangle(art.UV.X + real.X, art.UV.Y + real.Y, real.Width, real.Height);

                    // the art has transparent margins: size the bottle itself, as tall as the cell says
                    float scale = Info.Size / (float)Math.Max(1, real.Height);
                    int w = Math.Max(1, (int)(real.Width * scale));
                    int h = Math.Max(1, (int)(real.Height * scale));
                    Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, Alpha);
                    int left = x + (Width - w) / 2;
                    int top = y + ArtHeight - h;

                    renderLists.AddGumpWithAtlas(
                        batcher =>
                        {
                            batcher.Draw(texture, new Rectangle(left, top, w, h), source, hue, layerDepth);

                            return true;
                        }
                    );
                }

                return base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            }
        }
    }
}
