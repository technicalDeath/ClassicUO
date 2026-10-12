// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Text;
using ClassicUO.Configuration;
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
    /// The shard's combat bar, under the potion bar: bandages (a click bandages you, Shift-click lets you choose who), the two kinds of ammunition, the eight spell reagents, and
    /// the weapon or tool you hold now and the one you held before it (a click puts it in your hand, or puts it away when you already hold it). Everything is read from the
    /// pack on the client, so it needs nothing from the server. Same three states as the potion bar: every item, only the ones you have, and docked to one small icon.
    /// </summary>
    internal sealed class CombatBarGump : Gump
    {
        private enum Kind { Bandage, Ammo, Reagent }

        private readonly record struct Supply(ushort Graphic, string Name, Kind Kind);

        private static readonly Supply[] Supplies =
        {
            new(0x0E21, "Bandages", Kind.Bandage),
            new(0x0F3F, "Arrows", Kind.Ammo), new(0x1BFB, "Bolts", Kind.Ammo),
            new(0x0F7A, "Black pearl", Kind.Reagent), new(0x0F7B, "Blood moss", Kind.Reagent), new(0x0F84, "Garlic", Kind.Reagent), new(0x0F85, "Ginseng", Kind.Reagent),
            new(0x0F86, "Mandrake root", Kind.Reagent), new(0x0F88, "Nightshade", Kind.Reagent), new(0x0F8C, "Sulfurous ash", Kind.Reagent), new(0x0F8D, "Spider silk", Kind.Reagent)
        };

        private const ushort BandageGraphic = 0x0E21;
        private const ushort LargerIcon = 62050;  // the shard's brass diamonds, see ShardContent/tools/gump-art/make_pack_icons.py
        private const ushort SmallerIcon = 62051;
        private const int ArtBox = 36;           // every item's art is fitted into a box this big
        private const int LabelRow = 14;
        private const int CellHeight = ArtBox + LabelRow;
        private const int Pad = 5;
        private const int Gap = 9;
        private const int GroupGap = 18;
        private const int IconSize = 21;
        private const int SlotWidth = 40;
        private const int BarHeight = CellHeight + 2 * Pad;
        private const int DockedHeight = IconSize + 2 * Pad + 4;

        private const int Full = 0;
        private const int Shrunk = 1;
        private const int Docked = 2;
        private const int ScanMilliseconds = 250;
        private const int ActionDelay = 700;      // the server limits how fast actions may follow one another
        private const int StepTimeout = 3000;
        private const int SelfTargetWindow = 2500;

        private readonly Dictionary<ushort, int> _counts = new();
        private readonly Dictionary<ushort, uint> _firstOf = new();
        private readonly List<Cell> _cells = new();
        private AlphaBlendControl _background;
        private long _nextScan;
        private bool _moved;
        private bool _built;
        private int _mode;
        private string _signature = string.Empty;
        private bool _usesReagents;

        // what each hand slot held last: the main-hand weapon, the shield and the tool
        private const int MainSlot = 0;
        private const int ShieldSlot = 1;
        private const int ToolSlot = 2;
        private static readonly string[] SlotNames = { "weapon", "shield", "tool" };
        private readonly uint[] _slots = new uint[3];

        // bandaging yourself
        private long _selfTargetUntil;

        // equipping
        private enum Stage { Idle, Clearing, Equipping }

        private Stage _stage;
        private uint _wanted;
        private long _nextAt;
        private long _deadline;
        private long _lockUntil;

        public CombatBarGump(World world) : base(world, 0, 0)
        {
            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;

            Profile profile = ProfileManager.CurrentProfile;
            _mode = profile == null ? Full : Math.Clamp(profile.CombatBarMode, Full, Docked);
            _slots[MainSlot] = profile?.CombatBarMain ?? 0;
            _slots[ShieldSlot] = profile?.CombatBarShield ?? 0;
            _slots[ToolSlot] = profile?.CombatBarTool ?? 0;
            Rebuild();
        }

        // ---- layout

        /// <summary>Reagents are for casting and brewing, so the bar shows them only to a character who has trained Magery, Alchemy or Inscription.</summary>
        private bool UsesReagents()
        {
            foreach (Skill skill in World.Player.Skills)
            {
                if (skill.Base > 0 && (skill.Name.Equals("Magery", StringComparison.OrdinalIgnoreCase) || skill.Name.Equals("Alchemy", StringComparison.OrdinalIgnoreCase)
                    || skill.Name.Equals("Inscription", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShowsSupply(Supply supply) =>
            (supply.Kind != Kind.Reagent || _usesReagents) && (_mode == Full || (_mode == Shrunk && _counts.GetValueOrDefault(supply.Graphic) > 0));

        private bool ShowsSlot(uint serial) => _mode == Full || (_mode == Shrunk && serial != 0 && World.Items.Get(serial) != null);

        private string Signature()
        {
            var text = new StringBuilder();
            text.Append(_mode);

            foreach (Supply supply in Supplies)
            {
                text.Append(ShowsSupply(supply) ? '1' : '0');
            }

            foreach (uint serial in _slots)
            {
                Item item = serial == 0 ? null : World.Items.Get(serial);
                text.Append('|').Append(ShowsSlot(serial) ? 1 : 0).Append(item?.Graphic ?? 0).Append(':').Append(item?.Hue ?? 0).Append(IsHeld(item) ? 'h' : '-');
            }

            return text.ToString();
        }

        private void Rebuild()
        {
            Clear();
            _cells.Clear();

            _background = new AlphaBlendControl(0.6f);
            Add(_background);

            int x = Pad;

            if (_mode == Docked)
            {
                int iconY = (DockedHeight - IconSize) / 2;

                Add(new ArtIcon(BandageGraphic, IconSize) { X = x, Y = iconY });
                AddButton(x, iconY, IconSize, IconSize, "Show the combat bar", () => SetMode(Shrunk));
                Width = x + IconSize + Pad;
                Height = DockedHeight;
                _background.Width = Width;
                _background.Height = Height;
                _built = true;
                _signature = Signature();

                return;
            }

            int diamondY = Pad + (ArtBox - IconSize) / 2;

            if (_mode == Full)
            {
                AddDiamond(ref x, diamondY, SmallerIcon, "Show only the items you have", () => SetMode(Shrunk));
            }
            else
            {
                AddDiamond(ref x, diamondY, SmallerIcon, "Fold the bar away", () => SetMode(Docked));
                AddDiamond(ref x, diamondY, LargerIcon, "Show every item", () => SetMode(Full));
            }

            Kind? previousKind = null;
            bool first = true;

            foreach (Supply supply in Supplies)
            {
                if (!ShowsSupply(supply))
                {
                    continue;
                }

                if (previousKind != null && previousKind != supply.Kind)
                {
                    x += GroupGap - Gap;
                }

                previousKind = supply.Kind;
                var control = new Cell(this, supply) { X = x, Y = Pad };
                _cells.Add(control);
                Add(control);
                x += control.Width + Gap;
                first = false;
            }

            bool slotShown = false;

            for (int slot = 0; slot < _slots.Length; slot++)
            {
                uint serial = _slots[slot];

                if (!ShowsSlot(serial))
                {
                    continue;
                }

                if (!slotShown && !first)
                {
                    x += GroupGap - Gap;
                }

                slotShown = true;
                var control = new Cell(this, slot) { X = x, Y = Pad };
                _cells.Add(control);
                Add(control);
                x += control.Width + Gap;
                first = false;
            }

            Width = Math.Max(x - Gap + Pad, Pad * 2 + IconSize * 2 + Gap);
            Height = BarHeight;
            _background.Width = Width;
            _background.Height = Height;
            _built = true;
            _signature = Signature();
            Refresh();
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
            var hit = new HitBox(x, y, width, height);

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
                ProfileManager.CurrentProfile.CombatBarMode = mode;
                ProfileManager.CurrentProfile.Save(World, ProfileManager.ProfilePath);
            }

            Rebuild();
        }

        private void Refresh()
        {
            foreach (Cell cell in _cells)
            {
                cell.Refresh();
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
            StepSelfTarget();
            StepEquip();
        }

        private void Scan()
        {
            _counts.Clear();
            _firstOf.Clear();

            Item backpack = World.Player.FindItemByLayer(Layer.Backpack);

            if (backpack != null)
            {
                Collect(backpack);
            }

            TrackHeld();
            _usesReagents = UsesReagents();

            if (!_built || Signature() != _signature)
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

                if (Array.Exists(Supplies, s => s.Graphic == item.Graphic))
                {
                    _counts[item.Graphic] = _counts.GetValueOrDefault(item.Graphic) + Math.Max(1, (int)item.Amount);

                    if (!_firstOf.ContainsKey(item.Graphic))
                    {
                        _firstOf[item.Graphic] = item.Serial;
                    }
                }
                else if (item.ItemData.IsContainer && !item.IsEmpty)
                {
                    Collect(item);
                }
            }
        }

        // ---- what the hands hold

        /// <summary>The tile data flags a shield as a weapon too, so shields are told apart by their art: every shield the server makes has a graphic in 0x1B72-0x1B7B or 0x1BC3-0x1BC5.</summary>
        private static bool IsShield(Item item) => item.Graphic is >= 0x1B72 and <= 0x1B7B or >= 0x1BC3 and <= 0x1BC5;

        /// <summary>Things that sit on a hand layer without being something you fight or work with: the spellbook and the runebook (one-handed).</summary>
        private static bool IsBook(Item item) => item.Graphic is 0x0EFA or 0x22C5;

        /// <summary>The tools that go in a hand: pickaxe, hatchet, shovel, fishing pole, smith's hammer, sledge hammer and tongs. A hatchet is an axe too; it goes in the tool slot.</summary>
        private static bool IsTool(Item item) =>
            item.Graphic is 0x0E85 or 0x0E86 or 0x0F43 or 0x0F44 or 0x0F39 or 0x0F3A or 0x0DC0 or 0x0DC1 or 0x13E3 or 0x13E4 or 0x0FB4 or 0x0FB5 or 0x0FBB or 0x0FBC;

        /// <summary>Which slot the item belongs in, or -1 when the bar does not follow it (books, lights, anything that is neither a weapon, a shield nor a tool).</summary>
        private static int SlotOf(Item item)
        {
            if (IsShield(item))
            {
                return ShieldSlot;
            }

            if (IsTool(item))
            {
                return ToolSlot;
            }

            return item.ItemData.IsWeapon && !IsBook(item) ? MainSlot : -1;
        }

        /// <summary>Whether the item sits on the two-handed layer and is not a shield: a two-handed weapon or the fishing pole, which takes both hands.</summary>
        private static bool TakesBothHands(Item item) => (Layer)item.ItemData.Layer == Layer.TwoHanded && !IsShield(item);

        private bool IsHeld(Item item) =>
            item != null && World.Player != null && item.Container == World.Player.Serial && (item.Layer == Layer.OneHanded || item.Layer == Layer.TwoHanded) && SlotOf(item) >= 0;

        private void TrackHeld()
        {
            bool changed = false;

            foreach (Layer layer in new[] { Layer.OneHanded, Layer.TwoHanded })
            {
                Item worn = World.Player.FindItemByLayer(layer);
                int slot = worn == null ? -1 : SlotOf(worn);

                if (slot >= 0 && _slots[slot] != worn.Serial)
                {
                    _slots[slot] = worn.Serial;
                    changed = true;
                }
            }

            if (changed && ProfileManager.CurrentProfile != null)
            {
                ProfileManager.CurrentProfile.CombatBarMain = _slots[MainSlot];
                ProfileManager.CurrentProfile.CombatBarShield = _slots[ShieldSlot];
                ProfileManager.CurrentProfile.CombatBarTool = _slots[ToolSlot];
                ProfileManager.CurrentProfile.Save(World, ProfileManager.ProfilePath);
            }
        }

        // ---- bandages

        private void UseBandage()
        {
            if (Time.Ticks < _lockUntil || !_firstOf.TryGetValue(BandageGraphic, out uint serial))
            {
                GameActions.Print(World, "You have no bandages.");

                return;
            }

            _lockUntil = Time.Ticks + ActionDelay;
            _selfTargetUntil = Keyboard.Shift ? 0 : Time.Ticks + SelfTargetWindow;
            GameActions.DoubleClick(World, serial);
        }

        private void StepSelfTarget()
        {
            if (_selfTargetUntil == 0)
            {
                return;
            }

            if (World.TargetManager.IsTargeting)
            {
                _selfTargetUntil = 0;
                World.TargetManager.Target(World.Player.Serial);
            }
            else if (Time.Ticks > _selfTargetUntil)
            {
                _selfTargetUntil = 0;
            }
        }

        // ---- equipping

        private Item SlotItem(int slot)
        {
            uint serial = _slots[slot];

            return serial == 0 ? null : World.Items.Get(serial);
        }

        private void Equip(int slot)
        {
            Item item = SlotItem(slot);

            if (item == null || _stage != Stage.Idle || Time.Ticks < _lockUntil || Client.Game.UO.GameCursor.ItemHold.Enabled)
            {
                return;
            }

            Item backpack = World.Player.FindItemByLayer(Layer.Backpack);

            if (backpack == null)
            {
                return;
            }

            _lockUntil = Time.Ticks + ActionDelay;

            if (IsHeld(item))
            {
                // already in hand: put it away
                GameActions.PickUp(World, item, 0, 0, 1);
                GameActions.DropItem(Client.Game.UO.GameCursor.ItemHold.Serial, 0xFFFF, 0xFFFF, 0, backpack.Serial);

                return;
            }

            _wanted = item.Serial;
            _stage = Stage.Clearing;
            _nextAt = Time.Ticks;
            _deadline = Time.Ticks + StepTimeout * 2;
        }

        /// <summary>
        /// What must come off first. A two-handed weapon or the fishing pole takes both hands: whatever is in either hand goes. A shield needs the two-handed layer clear.
        /// A one-handed weapon or tool needs the one-handed layer clear, and any two-handed weapon or pole gone, but a shield may stay. A book in the hand is in the way too.
        /// </summary>
        private Item NextConflict(Item wanted)
        {
            Item one = World.Player.FindItemByLayer(Layer.OneHanded);
            Item two = World.Player.FindItemByLayer(Layer.TwoHanded);

            if (IsShield(wanted))
            {
                return two != null && two.Serial != wanted.Serial ? two : null;
            }

            if (one != null && one.Serial != wanted.Serial)
            {
                return one;
            }

            if (two == null || two.Serial == wanted.Serial)
            {
                return null;
            }

            return TakesBothHands(wanted) || !IsShield(two) ? two : null;
        }

        private void StepEquip()
        {
            if (_stage == Stage.Idle)
            {
                return;
            }

            long now = Time.Ticks;
            Item wanted = World.Items.Get(_wanted);
            Item backpack = World.Player.FindItemByLayer(Layer.Backpack);

            if (wanted == null || backpack == null || now > _deadline)
            {
                _stage = Stage.Idle;
                _lockUntil = now + ActionDelay;

                return;
            }

            if (now < _nextAt || Client.Game.UO.GameCursor.ItemHold.Enabled)
            {
                return;
            }

            if (_stage == Stage.Clearing)
            {
                Item conflict = NextConflict(wanted);

                if (conflict != null)
                {
                    GameActions.PickUp(World, conflict, 0, 0, 1);
                    GameActions.DropItem(Client.Game.UO.GameCursor.ItemHold.Serial, 0xFFFF, 0xFFFF, 0, backpack.Serial);
                    _nextAt = now + ActionDelay;

                    return;
                }

                GameActions.PickUp(World, wanted, 0, 0, 1);
                GameActions.Equip(World);
                _stage = Stage.Equipping;
                _nextAt = now + ActionDelay;
                _deadline = now + StepTimeout;

                return;
            }

            if (IsHeld(wanted))
            {
                _stage = Stage.Idle;
                _lockUntil = now + ActionDelay;
            }
        }

        // ---- placement

        /// <summary>Just under the potion bar when that is showing, else just below the game window, in the black, when there is room; hidden when there is not.</summary>
        private void Place()
        {
            if (_moved)
            {
                return;
            }

            GameScene scene = Client.Game.GetScene<GameScene>();

            if (scene == null)
            {
                return;
            }

            Rectangle view = scene.Camera.Bounds;
            PotionBarGump potions = UIManager.GetGump<PotionBarGump>();
            int top = potions != null && potions.IsVisible ? potions.Y + potions.Height + 4 : view.Y + view.Height + 4;

            if (Client.Game.ClientBounds.Height - top < Height + 2)
            {
                IsVisible = false;

                return;
            }

            IsVisible = true;
            X = view.X;
            Y = top;
        }

        protected override void OnDragEnd(int x, int y)
        {
            _moved = true;
            base.OnDragEnd(x, y);
        }

        // ---- drawing

        private static void DrawArt(RenderLists renderLists, ushort graphic, ushort hueId, bool partial, float alpha, int boxX, int boxY, int boxWidth, int boxHeight, float layerDepth)
        {
            ref readonly var art = ref Client.Game.UO.Arts.GetArt(graphic);

            if (art.Texture == null)
            {
                return;
            }

            var texture = art.Texture;
            Rectangle real = Client.Game.UO.Arts.GetRealArtBounds(graphic);
            Rectangle source = new Rectangle(art.UV.X + real.X, art.UV.Y + real.Y, real.Width, real.Height);
            float scale = Math.Min(boxWidth / (float)Math.Max(1, real.Width), boxHeight / (float)Math.Max(1, real.Height));
            scale = Math.Min(scale, 1.6f);
            int w = Math.Max(1, (int)(real.Width * scale));
            int h = Math.Max(1, (int)(real.Height * scale));
            Vector3 hue = ShaderHueTranslator.GetHueVector(hueId, partial, alpha);
            int left = boxX + (boxWidth - w) / 2;
            int top = boxY + boxHeight - h;

            renderLists.AddGumpWithAtlas(
                batcher =>
                {
                    batcher.Draw(texture, new Rectangle(left, top, w, h), source, hue, layerDepth);

                    return true;
                }
            );
        }

        private sealed class ArtIcon : Control
        {
            private readonly ushort _graphic;

            public ArtIcon(ushort graphic, int size)
            {
                _graphic = graphic;
                Width = Height = size;
                AcceptMouseInput = false;
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                DrawArt(renderLists, _graphic, 0, false, 1f, x, y, Width, Height, layerDepthRef);

                return base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            }
        }

        // ---- one cell: a supply, or one of the two equipment slots

        private sealed class Cell : Control
        {
            private readonly CombatBarGump _bar;
            private readonly Label _label;
            private readonly Supply? _supply;
            private readonly int _slot;
            private string _shown;

            public Cell(CombatBarGump bar, Supply supply) : this(bar, supply, -1)
            {
            }

            public Cell(CombatBarGump bar, int slot) : this(bar, null, slot)
            {
            }

            private Cell(CombatBarGump bar, Supply? supply, int slot)
            {
                _bar = bar;
                _supply = supply;
                _slot = slot;
                Height = CellHeight;
                AcceptMouseInput = true;
                CanMove = true;
                WantUpdateSize = false;

                if (supply is { } s)
                {
                    Rectangle real = Client.Game.UO.Arts.GetRealArtBounds(s.Graphic);
                    float scale = Math.Min(1.6f, Math.Min(ArtBox / (float)Math.Max(1, real.Width), ArtBox / (float)Math.Max(1, real.Height)));
                    Width = Math.Max(26, (int)(real.Width * scale));
                }
                else
                {
                    Width = SlotWidth;
                }

                _label = new Label("0", true, 0x0481, 0, 1, FontStyle.BlackBorder) { AcceptMouseInput = false };
                Add(_label);
            }

            public void Refresh()
            {
                string text;
                string tooltip;
                float alpha = 1f;

                if (_supply is { } s)
                {
                    int count = _bar._counts.GetValueOrDefault(s.Graphic);
                    text = count.ToString();
                    alpha = count > 0 ? 1f : 0.35f;

                    tooltip = s.Kind switch
                    {
                        Kind.Bandage => count > 0
                            ? $"{s.Name} ({count}). Click to bandage yourself. Shift-click to choose who to bandage."
                            : $"{s.Name}: you have none. Click to bandage yourself; Shift-click to choose who to bandage.",
                        Kind.Ammo => count > 0 ? $"{s.Name} ({count})." : $"{s.Name}: you have none.",
                        _ => count > 0 ? $"{s.Name} ({count})." : $"{s.Name}: you have none."
                    };
                }
                else
                {
                    Item item = _bar.SlotItem(_slot);
                    bool held = _bar.IsHeld(item);
                    text = item == null ? SlotNames[_slot] : held ? "held" : "equip";
                    alpha = item == null ? 0.35f : 1f;
                    string role = _slot == MainSlot ? "The weapon you held last" : _slot == ShieldSlot ? "The shield you wore last" : "The tool you held last";

                    tooltip = item == null
                        ? $"{role} appears here."
                        : $"{NameOf(item)}. {(held ? "In your hand: click to put it away." : "Click to hold it.")}";
                }

                string key = $"{text}|{alpha}|{tooltip}";

                if (key == _shown)
                {
                    return;
                }

                _shown = key;
                _label.Text = text;
                _label.X = (Width - _label.Width) / 2;
                _label.Y = ArtBox + 1;
                Alpha = alpha;
                SetTooltip(tooltip);
            }

            private static string NameOf(Item item)
            {
                string name = string.IsNullOrEmpty(item.Name) ? item.ItemData.Name : item.Name;

                return string.IsNullOrEmpty(name) ? "Item" : StringHelper.CapitalizeAllWords(name);
            }

            protected override void OnMouseUp(int x, int y, MouseButtonType button)
            {
                if (button == MouseButtonType.Left && Math.Abs(Mouse.LDragOffset.X) < 4 && Math.Abs(Mouse.LDragOffset.Y) < 4)
                {
                    if (_supply is { Kind: Kind.Bandage })
                    {
                        _bar.UseBandage();

                        return;
                    }

                    if (_supply == null)
                    {
                        _bar.Equip(_slot);

                        return;
                    }
                }

                base.OnMouseUp(x, y, button);
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                if (_supply is { } s)
                {
                    DrawArt(renderLists, s.Graphic, 0, false, Alpha, x, y, Width, ArtBox, layerDepthRef);
                }
                else if (_bar.SlotItem(_slot) is { } item)
                {
                    DrawArt(renderLists, item.Graphic, item.Hue, item.ItemData.IsPartialHue, Alpha, x, y, Width, ArtBox, layerDepthRef);
                }

                return base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            }
        }
    }
}
