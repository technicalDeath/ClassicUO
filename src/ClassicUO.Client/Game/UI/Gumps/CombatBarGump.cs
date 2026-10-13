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
using ClassicUO.Network;
using ClassicUO.IO;
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
        private const int ArtBox = 32;           // every item's art is fitted into a box this big
        private const int LabelRow = 13;
        private const int CellHeight = ArtBox + LabelRow;
        private const int Pad = 4;
        private const int Gap = 8;
        private const int GroupGap = 16;
        private const int IconSize = 19;
        private const int SlotWidth = 36;
        private const int BarHeight = CellHeight + 2 * Pad;
        private const int DockedHeight = IconSize + 2 * Pad + 4;

        private const int Full = 0;
        private const int Shrunk = 1;
        private const int Docked = 2;
        private const int ScanMilliseconds = 250;
        private const int ActionDelay = 700;      // the server limits how fast actions may follow one another
        private const int StepTimeout = 3000;
        private const int SelfTargetWindow = 2500;
        /// <summary>The shard's extended command for item durability, both ways (ShardContent CombatBarService).</summary>
        public const ushort DurabilityCommand = 0xA9;

        /// <summary>Item serial to hit points and maximum hit points, as the server last said. An item the server says nothing about has no gauge.</summary>
        private static readonly Dictionary<uint, (ushort Hits, ushort Max)> Durability = new();

        public static bool TryGetDurability(uint serial, out (ushort Hits, ushort Max) value) => Durability.TryGetValue(serial, out value) && value.Max > 0;

        public static void OnDurabilityReply(ref StackDataReader p)
        {
            int count = p.ReadUInt8();

            for (int i = 0; i < count && p.Remaining >= 8; i++)
            {
                uint serial = p.ReadUInt32BE();
                ushort hits = p.ReadUInt16BE();
                ushort max = p.ReadUInt16BE();
                Durability[serial] = (hits, max);
            }
        }

        /// <summary>Asks the server about up to eight items; used by the bar for its slots and by the selector for the items it lists.</summary>
        public static void AskDurability(IEnumerable<uint> serials)
        {
            var list = new List<uint>();

            foreach (uint serial in serials)
            {
                if (serial != 0 && list.Count < 8 && !list.Contains(serial))
                {
                    list.Add(serial);
                }
            }

            if (list.Count > 0 && NetClient.Socket.IsConnected)
            {
                NetClient.Socket.Send_DurabilityRequest(list);
            }
        }

        private const int DurabilityAskMilliseconds = 2500;
        private long _nextDurabilityAsk;

        /// <summary>A thin gauge: green, with red filling from the top down as the item loses durability.</summary>
        internal static void DrawDurability(RenderLists renderLists, ushort hits, ushort max, int x, int y, int height, float alpha, float layerDepth)
        {
            if (max == 0)
            {
                return;
            }

            float lost = 1f - Math.Clamp(hits / (float)max, 0f, 1f);
            int red = Math.Clamp((int)Math.Round(height * lost), 0, height);
            Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, alpha);

            renderLists.AddGumpNoAtlas(
                batcher =>
                {
                    batcher.Draw(SolidColorTextureCache.GetTexture(Color.Black), new Rectangle(x - 1, y - 1, 5, height + 2), hue, layerDepth);

                    if (red < height)
                    {
                        batcher.Draw(SolidColorTextureCache.GetTexture(new Color(60, 200, 70)), new Rectangle(x, y + red, 3, height - red), hue, layerDepth);
                    }

                    if (red > 0)
                    {
                        batcher.Draw(SolidColorTextureCache.GetTexture(new Color(215, 40, 40)), new Rectangle(x, y, 3, red), hue, layerDepth);
                    }

                    return true;
                }
            );
        }

        private const ushort NormalCountHue = 0x0481;
        private const ushort LowCountHue = 0x0026;   // a warm red

        /// <summary>A stack this size or smaller is running low: bandages and reagents five, ammunition ten.</summary>
        private static int LowLevel(Kind kind) => kind == Kind.Ammo ? 10 : 5;

        // the arrows a bow shoots and the bolts a crossbow shoots, by the weapon's art
        private static bool IsBow(Item item) => item.Graphic is 0x13B2 or 0x13B1;
        private static bool IsCrossbow(Item item) => item.Graphic is 0x0F50 or 0x0F4F or 0x13FD or 0x13FC;

        private readonly Dictionary<ushort, int> _counts = new();
        private readonly Dictionary<ushort, uint> _firstOf = new();
        private readonly List<Cell> _cells = new();
        private AlphaBlendControl _background;
        private long _nextScan;
        private bool _moved;
        private bool _restored;

        /// <summary>The player has put this bar where they want it, so the bars that stack under it do not follow it.</summary>
        public bool IsMoved => _moved;
        private bool _built;
        private bool _gear;
        private int _mode;
        private string _signature = string.Empty;
        private bool _usesReagents;

        // what each hand slot held last: the main-hand weapon, the shield and the tool
        private const int MainSlot = 0;
        private const int ShieldSlot = 1;
        private const int ToolSlot = 2;
        private const int WarSlot = 3;      // the war/peace toggle
        private const int WeightSlot = 4;   // the carried-weight readout
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

            if (ProfileManager.CurrentProfile is { CombatBarX: >= 0, CombatBarY: >= 0 } saved)
            {
                X = saved.CombatBarX;
                Y = saved.CombatBarY;
                _moved = true;
                _restored = true;
            }
            Rebuild();
        }

        // ---- shared with the item shortcut bar

        public bool Bandaging => BandageWatch.Active;

        private bool AmmoNotUsed(ushort ammoGraphic) => AmmoNotUsedByHand(World, ammoGraphic);

        /// <summary>Whether the ammunition is the wrong kind for the bow or crossbow in hand. With no ranged weapon in hand both kinds are normal.</summary>
        internal static bool AmmoNotUsedByHand(World world, ushort ammoGraphic)
        {
            if (ammoGraphic != 0x0F3F && ammoGraphic != 0x1BFB)
            {
                return false;
            }

            Item weapon = world.Player.FindItemByLayer(Layer.TwoHanded) is { } two && two.ItemData.IsWeapon ? two : world.Player.FindItemByLayer(Layer.OneHanded);

            if (weapon == null)
            {
                return false;
            }

            if (IsBow(weapon))
            {
                return ammoGraphic != 0x0F3F;
            }

            return IsCrossbow(weapon) && ammoGraphic != 0x1BFB;
        }

        /// <summary>The stack size at or below which a supply is running low (bandages and reagents 5, ammunition 10), or 0 for an item that is not one of the bar's supplies.</summary>
        internal static int SupplyLowLevelOf(ushort graphic)
        {
            foreach (Supply supply in Supplies)
            {
                if (supply.Graphic == graphic)
                {
                    return LowLevel(supply.Kind);
                }
            }

            return 0;
        }

        internal const ushort LowHue = LowCountHue;

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
            text.Append(_mode).Append(AutoUse.Allowed ? 'g' : '-');

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

            // the war/peace toggle: in front of the supplies
            var war = new Cell(this, WarSlot) { X = x, Y = Pad };
            _cells.Add(war);
            Add(war);
            x += war.Width + GroupGap;
            first = false;

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

            // the carried-weight readout: after everything else
            x += GroupGap - Gap;
            var weight = new Cell(this, WeightSlot) { X = x, Y = Pad };
            _cells.Add(weight);
            Add(weight);
            x += weight.Width + Gap;

            // the options button, when the shard allows auto-use
            _gear = AutoUse.Allowed;

            if (_gear)
            {
                x += GroupGap - Gap;
                int gearY = Pad + (ArtBox - IconSize) / 2;
                var cog = new GumpPic(x, gearY, 62052, 0) { AcceptMouseInput = false };

                if (cog.Width > 0)
                {
                    cog.Width = cog.Height = IconSize;
                }

                Add(cog);
                AddButton(x, gearY, IconSize, IconSize, "Options: bandage you at a set level of health", OpenOptions);
                x += IconSize + Gap;
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
            AutoUse.Tick(World);
        }

        private void Scan()
        {
            _counts.Clear();
            _firstOf.Clear();

            Item backpack = World.Player.FindItemByLayer(Layer.Backpack);

            if (backpack != null)
            {
                PackPrimer.Tick(World);
                Collect(backpack);
            }

            TrackHeld();
            _usesReagents = UsesReagents();

            if (Time.Ticks >= _nextDurabilityAsk)
            {
                _nextDurabilityAsk = Time.Ticks + DurabilityAskMilliseconds;
                AskDurability(_slots);
            }

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

            if (changed)
            {
                _nextDurabilityAsk = 0;
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
            BandageWatch.Start(World);
            _selfTargetUntil = Keyboard.Shift ? 0 : Time.Ticks + SelfTargetWindow;
            GameActions.DoubleClick(World, serial);
        }

        private void OpenOptions()
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null)
            {
                return;
            }

            BarOptionsGump.Toggle(
                World, this, "Combat bar options",
                new[]
                {
                    AutoUse.OptionFor(profile, AutoUseOption.Bandage)
                }
            );
        }

        /// <summary>The auto-bandage: the same steps as a click on the bandage slot, aimed at yourself. Returns false when there is nothing to do it with.</summary>
        public bool TryBandageSelf()
        {
            if (Time.Ticks < _lockUntil || !_firstOf.ContainsKey(BandageGraphic) || _stage != Stage.Idle)
            {
                return false;
            }

            UseBandage();

            return true;
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

        internal static string NameOf(Item item)
        {
            string name = string.IsNullOrEmpty(item.Name) ? item.ItemData.Name : item.Name;

            return string.IsNullOrEmpty(name) ? "Item" : StringHelper.CapitalizeAllWords(name);
        }

        private Item SlotItem(int slot)
        {
            if (slot < 0 || slot >= _slots.Length)
            {
                return null;
            }

            uint serial = _slots[slot];

            return serial == 0 ? null : World.Items.Get(serial);
        }

        private void Equip(int slot) => Equip(SlotItem(slot));

        private void Equip(Item item)
        {
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

        // ---- the item selector

        private const int HoldMilliseconds = 450;

        private void CollectCandidates(Item container, int slot, List<Item> found)
        {
            for (LinkedObject i = container.Items; i != null; i = i.Next)
            {
                var item = (Item)i;
                var layer = (Layer)item.ItemData.Layer;

                if ((layer == Layer.OneHanded || layer == Layer.TwoHanded) && SlotOf(item) == slot)
                {
                    if (!IsHeld(item))
                    {
                        found.Add(item);
                    }
                }
                else if (item.ItemData.IsContainer && !item.IsEmpty)
                {
                    CollectCandidates(item, slot, found);
                }
            }
        }

        /// <summary>Holding a weapon, shield or tool slot: a strip of the matching items from the pack and bags, to put one on with a click.</summary>
        private void OpenPicker(Cell cell, int slot)
        {
            UIManager.GetGump<EquipPickerGump>()?.Close();

            Item backpack = World.Player.FindItemByLayer(Layer.Backpack);

            if (backpack == null)
            {
                return;
            }

            var candidates = new List<Item>();
            CollectCandidates(backpack, slot, candidates);

            if (candidates.Count == 0)
            {
                GameActions.Print(World, $"You have no other {SlotNames[slot]} in your pack.");

                return;
            }

            candidates.Sort((a, b) =>
            {
                int byName = string.Compare(NameOf(a), NameOf(b), StringComparison.OrdinalIgnoreCase);

                return byName != 0 ? byName : a.Serial.CompareTo(b.Serial);
            });

            var shown = new List<uint>();

            for (int i = 0; i < Math.Min(candidates.Count, EquipPickerGump.MaxRows); i++)
            {
                shown.Add(candidates[i].Serial);
            }

            AskDurability(shown);

            UIManager.Add(
                new EquipPickerGump(
                    World, new Rectangle(cell.ScreenCoordinateX, cell.ScreenCoordinateY, cell.Width, cell.Height), candidates, Equip
                )
            );
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
            PotionBarGump potions = UIManager.GetGump<PotionBarGump>();
            int top = potions != null && potions.IsVisible && !potions.IsMoved ? potions.Y + potions.Height + 4 : view.Y + view.Height + 4;

            if (Client.Game.ClientBounds.Height - top < Height + 2)
            {
                // no room for another row on a short window: beside the potion bar, if the window is wide enough
                IsVisible = ShardLayout.BesideAnother(this, potions != null && !potions.IsMoved ? potions : null);

                return;
            }

            IsVisible = true;
            X = view.X;
            Y = top;
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
                profile.CombatBarX = X;
                profile.CombatBarY = Y;
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
                profile.CombatBarX = -1;
                profile.CombatBarY = -1;
            }
        }

        // ---- drawing

        internal static void DrawArt(RenderLists renderLists, ushort graphic, ushort hueId, bool partial, float alpha, int boxX, int boxY, int boxWidth, int boxHeight, float layerDepth)
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
                    Width = Math.Max(23, (int)(real.Width * scale));
                }
                else
                {
                    Width = slot == WeightSlot ? 52 : slot == WarSlot ? 32 : SlotWidth;
                }

                _label = new Label("0", true, 0x0481, 0, 1, FontStyle.BlackBorder) { AcceptMouseInput = false };
                Add(_label);

                if (slot == WeightSlot)
                {
                    var caption = new Label("stones", true, 0x0481, 0, 1, FontStyle.BlackBorder) { AcceptMouseInput = false };
                    caption.X = (Width - caption.Width) / 2;
                    caption.Y = ArtBox / 2 - caption.Height / 2;
                    Add(caption);
                }
            }

            private long _downAt;
            private bool _openedPicker;

            protected override void OnMouseDown(int x, int y, MouseButtonType button)
            {
                if (button == MouseButtonType.Left && _supply == null && _slot < _bar._slots.Length)
                {
                    _downAt = Time.Ticks;
                    _openedPicker = false;
                }

                base.OnMouseDown(x, y, button);
            }

            public override void Update()
            {
                base.Update();

                if (_supply != null || _slot >= _bar._slots.Length || _downAt == 0)
                {
                    return;
                }

                if (!Mouse.LButtonPressed)
                {
                    _downAt = 0;

                    return;
                }

                if (!_openedPicker && Time.Ticks - _downAt >= HoldMilliseconds && Math.Abs(Mouse.LDragOffset.X) < 4 && Math.Abs(Mouse.LDragOffset.Y) < 4)
                {
                    _openedPicker = true;
                    _bar.OpenPicker(this, _slot);
                }
            }

            public void Refresh()
            {
                string text;
                string tooltip;
                float alpha = 1f;
                ushort hue = NormalCountHue;
                string note = string.Empty;

                if (_supply is { } s)
                {
                    int count = _bar._counts.GetValueOrDefault(s.Graphic);
                    text = count.ToString();
                    alpha = count > 0 ? 1f : 0.35f;
                    bool bandaging = s.Kind == Kind.Bandage && _bar.Bandaging;
                    bool low = count > 0 && count <= LowLevel(s.Kind);
                    bool unused = s.Kind == Kind.Ammo && _bar.AmmoNotUsed(s.Graphic);
                    hue = low ? LowCountHue : NormalCountHue;
                    note = (bandaging ? " Bandaging now." : string.Empty) + (low ? " Running low." : string.Empty) + (unused ? " The weapon in your hand does not use these." : string.Empty);

                    if (bandaging)
                    {
                        text = "heal";
                    }

                    if (unused)
                    {
                        alpha = Math.Min(alpha, 0.45f);
                    }

                    tooltip = s.Kind switch
                    {
                        Kind.Bandage => count > 0
                            ? $"{s.Name} ({count}). Click to bandage yourself. Shift-click to choose who to bandage."
                            : $"{s.Name}: you have none. Click to bandage yourself; Shift-click to choose who to bandage.",
                        Kind.Ammo => count > 0 ? $"{s.Name} ({count})." : $"{s.Name}: you have none.",
                        _ => count > 0 ? $"{s.Name} ({count})." : $"{s.Name}: you have none."
                    };
                }
                else if (_slot == WarSlot)
                {
                    bool inWar = _bar.World.Player.InWarMode;
                    text = inWar ? "WAR" : "peace";
                    hue = inWar ? LowCountHue : NormalCountHue;
                    alpha = inWar ? 1f : 0.55f;
                    tooltip = inWar
                        ? "War mode: you will attack what you target. Click to go back to peace."
                        : "Peace mode: you will not attack on your own. Click to enter war mode so you can fight.";
                }
                else if (_slot == WeightSlot)
                {
                    int carried = _bar.World.Player.Weight;
                    int limit = _bar.World.Player.WeightMax;
                    text = $"{carried}/{limit}";
                    hue = carried > limit ? LowCountHue : limit > 0 && carried * 100 >= limit * 85 ? (ushort)0x0035 : NormalCountHue;
                    tooltip = carried > limit
                        ? $"You carry {carried} of {limit} stones: you are overloaded. Drop or store something."
                        : $"You carry {carried} of {limit} stones." + (hue == 0x0035 ? " You are close to the limit." : string.Empty);
                }
                else
                {
                    Item item = _bar.SlotItem(_slot);
                    bool held = _bar.IsHeld(item);
                    text = item == null ? SlotNames[_slot] : held ? "held" : "equip";
                    alpha = item == null ? 0.35f : 1f;
                    string role = _slot == MainSlot ? "The weapon you held last" : _slot == ShieldSlot ? "The shield you wore last" : "The tool you held last";

                    string wear = item != null && TryGetDurability(item.Serial, out var worn) ? $" Durability {worn.Hits} of {worn.Max}." : string.Empty;

                    tooltip = item == null
                        ? $"{role} appears here. Hold to choose from your pack."
                        : $"{NameOf(item)}.{wear} {(held ? "In your hand: click to put it away." : "Click to hold it.")} Hold the button to choose another.";
                }

                tooltip += note;
                string key = $"{text}|{alpha}|{tooltip}|{hue}";

                if (key == _shown)
                {
                    return;
                }

                _shown = key;
                _label.Text = text;
                _label.Hue = hue;
                _label.X = (Width - _label.Width) / 2;
                _label.Y = ArtBox + 1;
                Alpha = alpha;
                SetTooltip(tooltip);
            }

            protected override void OnMouseUp(int x, int y, MouseButtonType button)
            {
                if (Time.Ticks < EquipPickerGump.SwallowClickUntil)
                {
                    // this press closed the selector: it is not also a click on the bar
                    _downAt = 0;

                    return;
                }

                if (_openedPicker)
                {
                    // the press that opened the strip: letting go must not also equip or put away
                    _openedPicker = false;
                    _downAt = 0;

                    return;
                }

                if (button == MouseButtonType.Left && Math.Abs(Mouse.LDragOffset.X) < 4 && Math.Abs(Mouse.LDragOffset.Y) < 4)
                {
                    if (_supply is { Kind: Kind.Bandage })
                    {
                        _bar.UseBandage();

                        return;
                    }

                    if (_supply == null && _slot == WarSlot)
                    {
                        GameActions.ToggleWarMode(_bar.World.Player);

                        return;
                    }

                    if (_supply == null && _slot < _bar._slots.Length)
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
                    float alpha = Alpha;

                    if (s.Kind == Kind.Bandage && _bar.Bandaging)
                    {
                        // pulsing while a bandage is being applied
                        alpha = 0.5f + 0.5f * (0.5f + 0.5f * MathF.Sin(Time.Ticks / 170f));
                    }

                    DrawArt(renderLists, s.Graphic, 0, false, alpha, x, y, Width, ArtBox, layerDepthRef);
                }
                else if (_slot == WarSlot)
                {
                    DrawArt(renderLists, 0x0F5E, 0, false, Alpha, x, y, Width, ArtBox, layerDepthRef);
                }
                else if (_bar.SlotItem(_slot) is { } item)
                {
                    DrawArt(renderLists, item.Graphic, item.Hue, item.ItemData.IsPartialHue, Alpha, x, y, Width, ArtBox, layerDepthRef);

                    if (TryGetDurability(item.Serial, out var worn))
                    {
                        DrawDurability(renderLists, worn.Hits, worn.Max, x + Width - 4, y + 3, ArtBox - 6, Alpha, layerDepthRef);
                    }
                }

                return base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            }
        }
    }
}
