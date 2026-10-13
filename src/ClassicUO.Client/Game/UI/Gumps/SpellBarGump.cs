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
using ClassicUO.Renderer;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// The shard's spell bar for a character who has trained Magery: the first and second circle spells as a row of icons under the combat bar, each a click to cast. It is the quick road a
    /// new mage needs, who otherwise opens the spellbook, finds the page, and clicks the spell every time. An icon is dimmed while the mana is short for it; the mana a spell costs sits
    /// under its icon; the tooltip names the spell, its words, its mana and its reagents. The same three states as the other bars: every spell, only the ones you can cast now, docked.
    /// If the spell is not in the book the server says so, as when a spell is cast from the book. Nothing here changes how casting works.
    /// </summary>
    internal sealed class SpellBarGump : Gump
    {
        private const ushort LargerIcon = 62050;
        private const ushort SmallerIcon = 62051;
        private const int IconBox = 29;
        private const int LabelRow = 13;
        private const int CellHeight = IconBox + LabelRow;
        private const int CellWidth = 31;
        private const int Pad = 4;
        private const int Gap = 4;
        private const int GroupGap = 11;
        private const int DiamondSize = 19;
        private const int BarHeight = CellHeight + 2 * Pad;
        private const int DockedHeight = DiamondSize + 2 * Pad + 4;
        private const int Full = 0;
        private const int Shrunk = 1;
        private const int Docked = 2;
        private const int ScanMilliseconds = 250;
        private const int Circles = 8;

        // the mana a Magery spell costs, by circle (the era values)
        private static readonly int[] CircleMana = { 4, 6, 9, 11, 14, 20, 40, 50 };

        private AlphaBlendControl _background;
        private readonly List<Cell> _cells = new();
        private long _nextScan;
        private bool _built;
        private bool _moved;
        private bool _restored;
        private bool _showsBar;
        private int _mode;
        private int _circle;
        private string _signature = string.Empty;

        public SpellBarGump(World world) : base(world, 0, 0)
        {
            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;
            _mode = Math.Clamp(ProfileManager.CurrentProfile?.SpellBarMode ?? Full, Full, Docked);
            _circle = Math.Clamp(ProfileManager.CurrentProfile?.SpellBarCircle ?? 0, 0, Circles);

            if (ProfileManager.CurrentProfile is { SpellBarX: >= 0, SpellBarY: >= 0 } saved)
            {
                X = saved.SpellBarX;
                Y = saved.SpellBarY;
                _moved = true;
                _restored = true;
            }
            IsVisible = false;
        }

        public static int ManaOf(int spellId) => CircleMana[Math.Clamp((spellId - 1) / 8, 0, CircleMana.Length - 1)];

        public static int CircleOf(int spellId) => (spellId - 1) / 8 + 1;

        /// <summary>The bar is for someone who has started on Magery.</summary>
        private bool HasMagery()
        {
            foreach (Skill skill in World.Player.Skills)
            {
                if (skill != null && skill.Base > 0 && skill.Name.Equals("Magery", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The Magery needed for a circle, as the server's table has it for this era: none for the first circle, then ten more for each (ModernUO MagerySpell, UOR).</summary>
        public static int SkillFor(int circle) => (circle - 1) * 10;

        private double Magery()
        {
            foreach (Skill skill in World.Player.Skills)
            {
                if (skill != null && skill.Name.Equals("Magery", StringComparison.OrdinalIgnoreCase))
                {
                    return skill.Value;
                }
            }

            return 0;
        }

        /// <summary>The highest circle the character's Magery can cast.</summary>
        private int HighestCastable()
        {
            double magery = Magery();
            int circle = 1;

            while (circle < Circles && magery >= SkillFor(circle + 1))
            {
                circle++;
            }

            return circle;
        }

        /// <summary>The circle on show: the player's choice, or the highest castable one until they choose.</summary>
        private int ShownCircle => _circle == 0 ? HighestCastable() : _circle;

        private bool SkillEnough(int spellId) => Magery() >= SkillFor(CircleOf(spellId));

        private bool CanCastNow(int spellId) => World.Player.Mana >= ManaOf(spellId) && SkillEnough(spellId);

        private bool ShowsSpell(int spellId) => CircleOf(spellId) == ShownCircle && (_mode == Full || (_mode == Shrunk && CanCastNow(spellId)));

        private void ChooseCircle(int circle)
        {
            circle = Math.Clamp(circle, 1, Circles);
            _circle = circle;

            if (ProfileManager.CurrentProfile != null)
            {
                ProfileManager.CurrentProfile.SpellBarCircle = circle;
                ProfileManager.CurrentProfile.Save(World, ProfileManager.ProfilePath);
            }

            Rebuild();
        }

        private string Signature()
        {
            var text = new System.Text.StringBuilder();
            text.Append(_mode).Append(':').Append(ShownCircle).Append(':');

            for (int id = 1; id <= Circles * 8; id++)
            {
                text.Append(ShowsSpell(id) ? '1' : '0');
            }

            return text.ToString();
        }

        // ---- layout

        private void Rebuild()
        {
            Clear();
            _cells.Clear();

            _background = new AlphaBlendControl(0.6f);
            Add(_background);

            int x = Pad;

            if (_mode == Docked)
            {
                int y = (DockedHeight - DiamondSize) / 2;
                Add(new GumpPic(x, y, 0x1B5B, 0) { AcceptMouseInput = false, Width = DiamondSize, Height = DiamondSize });
                AddButton(x, y, DiamondSize, DiamondSize, "Show the spell bar", () => SetMode(Shrunk));
                Width = x + DiamondSize + Pad;
                Height = DockedHeight;
                _background.Width = Width;
                _background.Height = Height;
                _built = true;
                _signature = Signature();

                return;
            }

            int diamondY = Pad + (IconBox - DiamondSize) / 2;

            if (_mode == Full)
            {
                AddDiamond(ref x, diamondY, SmallerIcon, "Show only the spells you have the mana for", () => SetMode(Shrunk));
            }
            else
            {
                AddDiamond(ref x, diamondY, SmallerIcon, "Fold the bar away", () => SetMode(Docked));
                AddDiamond(ref x, diamondY, LargerIcon, "Show every spell", () => SetMode(Full));
            }

            // the circle's name between two small arrows
            int shown = ShownCircle;
            AddArrow(ref x, "<", shown > 1, "The circle before this one", () => ChooseCircle(shown - 1));
            var circleLabel = new Label($"Circle {shown}", true, 0x0481, 0, 1, FontStyle.BlackBorder) { AcceptMouseInput = false, X = x, Y = Pad + (IconBox - 14) / 2 };
            Add(circleLabel);
            x += Math.Max(circleLabel.Width, 56) + 2;
            AddArrow(ref x, ">", shown < Circles, "The next circle", () => ChooseCircle(shown + 1));
            x += GroupGap - Gap;

            for (int id = (shown - 1) * 8 + 1; id <= shown * 8; id++)
            {
                if (!ShowsSpell(id))
                {
                    continue;
                }

                var cell = new Cell(this, id) { X = x, Y = Pad };
                _cells.Add(cell);
                Add(cell);
                x += CellWidth + Gap;
            }

            Width = Math.Max(x - Gap + Pad, Pad * 2 + DiamondSize * 2 + Gap);
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
                icon.Width = icon.Height = DiamondSize;
            }

            Add(icon);
            AddButton(x, y, DiamondSize, DiamondSize, tooltip, onClick);
            x += DiamondSize + Gap + 3;
        }

        private void AddArrow(ref int x, string text, bool enabled, string tooltip, Action onClick)
        {
            var arrow = new Label(text, true, enabled ? (ushort)0x0035 : (ushort)0x0386, 0, 1, FontStyle.BlackBorder) { AcceptMouseInput = false, X = x + 3, Y = Pad + (IconBox - 14) / 2 };
            Add(arrow);

            if (enabled)
            {
                AddButton(x, Pad, 16, IconBox, tooltip, onClick);
            }

            x += 18;
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
                ProfileManager.CurrentProfile.SpellBarMode = mode;
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
                bool magery = ProfileManager.CurrentProfile is { SpellBarEnabled: true } && HasMagery();

                if (magery != _showsBar)
                {
                    _showsBar = magery;
                    _built = false;
                }

                if (_showsBar && (!_built || Signature() != _signature))
                {
                    Rebuild();
                }
                else if (_showsBar)
                {
                    Refresh();
                }
            }

            if (!_showsBar)
            {
                IsVisible = false;

                return;
            }

            Place();
        }

        /// <summary>Under the combat bar when that is showing (else under the potion bar, else below the game window), in the black, when there is room.</summary>
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
            int top = view.Y + view.Height + 4;

            // stack under the bars that are in their own place; one the player has moved away is not in the way
            PotionBarGump potions = UIManager.GetGump<PotionBarGump>();
            CombatBarGump combat = UIManager.GetGump<CombatBarGump>();

            if (potions != null && potions.IsVisible && !potions.IsMoved)
            {
                top = Math.Max(top, potions.Y + potions.Height + 4);
            }

            if (combat != null && combat.IsVisible && !combat.IsMoved)
            {
                top = Math.Max(top, combat.Y + combat.Height + 4);
            }

            if (Client.Game.ClientBounds.Height - top < Height + 2)
            {
                // no room for another row on a short window: beside the combat bar, or the potion bar, if the window is wide enough
                IsVisible = ShardLayout.BesideAnother(this, combat != null && !combat.IsMoved ? combat : null, potions != null && !potions.IsMoved ? potions : null);

                return;
            }

            IsVisible = true;
            X = view.X;
            Y = top;
        }

        /// <summary>The bar places itself every frame until it has been moved; this must be known when the drag begins, or it would snap back under the mouse.</summary>
        protected override void OnDragBegin(int x, int y)
        {
            // a drag that starts on a spell's icon takes the spell off the bar as a button of its own, as it does from the spellbook; anywhere else on the bar it moves the bar
            // (found from where the button went down, not from where the mouse is now: a quick drag has left the icon by the time it is noticed)
            foreach (Cell cell in _cells)
            {
                Point press = Mouse.LClickPosition;

                if (cell.Spell != null && press.X >= cell.ScreenCoordinateX && press.X < cell.ScreenCoordinateX + cell.Width && press.Y >= cell.ScreenCoordinateY && press.Y < cell.ScreenCoordinateY + cell.Height)
                {
                    MakeFloatingButton(cell.Spell);

                    return;
                }
            }

            _moved = true;
            base.OnDragBegin(x, y);
        }

        private void MakeFloatingButton(SpellDefinition spell)
        {
            for (LinkedListNode<Gump> i = UIManager.Gumps.Last; i != null; i = i.Previous)
            {
                if (i.Value is UseSpellButtonGump existing && existing.SpellID == spell.ID)
                {
                    existing.Dispose();

                    break;
                }
            }

            var button = new UseSpellButtonGump(World, spell)
            {
                X = Mouse.LClickPosition.X - 22,
                Y = Mouse.LClickPosition.Y - 22
            };

            UIManager.Add(button);
            UIManager.AttemptDragControl(button, true);
        }

        protected override void OnDragEnd(int x, int y)
        {
            _moved = true;
            base.OnDragEnd(x, y);

            if (ProfileManager.CurrentProfile is { } profile)
            {
                profile.SpellBarX = X;
                profile.SpellBarY = Y;
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
                profile.SpellBarX = -1;
                profile.SpellBarY = -1;
            }
        }

        private void Cast(int spellId)
        {
            if (World.Player.IsDead)
            {
                return;
            }

            GameActions.CastSpell(spellId);
        }

        // ---- one spell

        private sealed class Cell : Control
        {
            private readonly SpellBarGump _bar;
            private readonly int _id;
            private readonly SpellDefinition _spell;
            private readonly Label _mana;

            public SpellDefinition Spell => _spell;
            private readonly GumpPic _icon;
            private string _shown;

            public Cell(SpellBarGump bar, int id)
            {
                _bar = bar;
                _id = id;
                _spell = SpellDefinition.FullIndexGetSpell(id);
                Width = CellWidth;
                Height = CellHeight;
                AcceptMouseInput = true;
                CanMove = true;
                WantUpdateSize = false;

                _icon = new GumpPic(0, 0, (ushort)_spell.GumpIconSmallID, 0) { AcceptMouseInput = false };

                if (_icon.Width > 0)
                {
                    _icon.Width = _icon.Height = IconBox;
                }

                _icon.X = (CellWidth - IconBox) / 2;
                Add(_icon);

                _mana = new Label(ManaOf(id).ToString(), true, 0x0481, 0, 1, FontStyle.BlackBorder) { AcceptMouseInput = false };
                _mana.X = (CellWidth - _mana.Width) / 2;
                _mana.Y = IconBox + 1;
                Add(_mana);
            }

            public void Refresh()
            {
                bool can = _bar.CanCastNow(_id);
                bool skillShort = !_bar.SkillEnough(_id);
                string reagents = _spell.Regs.Length > 0 ? _spell.CreateReagentListString(", ") : string.Empty;
                string tooltip =
                    $"{_spell.Name} ({_spell.PowerWords}), circle {CircleOf(_id)}: {ManaOf(_id)} mana." +
                    (reagents.Length > 0 ? $" Reagents: {reagents}." : string.Empty) +
                    (can ? " Click to cast." : skillShort ? $" It needs Magery {SkillFor(CircleOf(_id))}." : " You do not have the mana for it yet.") + " Drag it onto the screen to make a button of it.";
                string key = $"{can}|{tooltip}";

                if (key == _shown)
                {
                    return;
                }

                _shown = key;
                _icon.Alpha = can ? 1f : 0.4f;
                _mana.Hue = can ? (ushort)0x0481 : (ushort)0x0026;
                SetTooltip(tooltip);
            }

            protected override void OnMouseUp(int x, int y, MouseButtonType button)
            {
                if (button == MouseButtonType.Left && Math.Abs(Mouse.LDragOffset.X) < 4 && Math.Abs(Mouse.LDragOffset.Y) < 4)
                {
                    _bar.Cast(_id);

                    return;
                }

                base.OnMouseUp(x, y, button);
            }
        }
    }
}
