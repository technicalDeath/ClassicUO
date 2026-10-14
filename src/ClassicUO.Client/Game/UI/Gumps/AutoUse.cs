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
    /// The potion and combat bars' auto-use options (owner request, 2026-10-13; docs/Auto-Use-Evidence.md). Each bar has a gear button that opens a panel above it with check boxes:
    /// bandage yourself below 75% health, drink a Heal potion at 25% health (the strongest first), drink a Refresh potion at 25% stamina (the strongest first), and drink the right
    /// Cure potion when poisoned (the server picks it, because the client is not told a poison's strength). The shard switches the whole thing: the gear only appears when the
    /// server's menu mask carries the auto-use bit. Everything it does is what the player could do by hand with the same bars, one thing at a time and at the pace the server allows.
    /// </summary>
    internal static class AutoUse
    {
        public const int CommandId = 0xAA;

        /// <summary>Raised when one of the four switches changes in the profile, from any window, so every window that shows them can follow.</summary>
        public static event Action Changed;

        /// <summary>Lets the sibling switch set (AutoPickup) wake the same windows.</summary>
        public static void NotifyChanged() => Changed?.Invoke();

        public static bool Get(Profile p, AutoUseOption option) => p != null && option switch
        {
            AutoUseOption.Bandage => p.AutoBandage,
            AutoUseOption.HealPotion => p.AutoHealPotion,
            AutoUseOption.RefreshPotion => p.AutoRefreshPotion,
            _ => p.AutoCure
        };

        public static void Set(Profile p, AutoUseOption option, bool value)
        {
            if (p == null || Get(p, option) == value)
            {
                return;
            }

            switch (option)
            {
                case AutoUseOption.Bandage: p.AutoBandage = value; break;
                case AutoUseOption.HealPotion: p.AutoHealPotion = value; break;
                case AutoUseOption.RefreshPotion: p.AutoRefreshPotion = value; break;
                default: p.AutoCure = value; break;
            }

            Changed?.Invoke();
        }

        /// <summary>The words for each switch, one place so the gear panels and the Options page say the same thing.</summary>
        public static string LabelOf(AutoUseOption option) => option switch
        {
            AutoUseOption.Bandage => $"Bandage me when my health drops below {BandageBelowPercent}%",
            AutoUseOption.HealPotion => $"Drink a Heal potion when my health falls to {HealPotionAtPercent}%",
            AutoUseOption.RefreshPotion => $"Drink a Refresh potion when my stamina falls to {RefreshPotionAtPercent}%",
            _ => "Cure me when I am poisoned"
        };

        public static string DetailOf(AutoUseOption option) => option switch
        {
            AutoUseOption.Bandage => "Also when I am poisoned, if my Healing and Anatomy are both 60 or more, because then a bandage can cure it. Never while a bandage is already being applied.",
            AutoUseOption.HealPotion => "Uses your strongest Heal potion first.",
            AutoUseOption.RefreshPotion => "Uses your strongest Refresh potion first.",
            _ => "Picks the Cure potion that suits the poison, so a strong one is not wasted on a weak poison."
        };

        public static BarOptionsGump.Option OptionFor(Profile p, AutoUseOption option) =>
            new(LabelOf(option), DetailOf(option), () => Get(p, option), v => Set(p, option, v));

        public const int BandageBelowPercent = 75;
        public const int HealPotionAtPercent = 25;
        public const int RefreshPotionAtPercent = 25;

        private const int ThinkMilliseconds = 250;
        private const int GapMilliseconds = 1600;           // between two automatic actions
        private const int HealPotionWaitMilliseconds = 10500; // the server allows one Heal potion every 10 seconds
        private const int CureWaitMilliseconds = 3500;
        private const int CureAskMilliseconds = 2500;
        private const int NoCureQuietMilliseconds = 8000;

        private static readonly byte[] HealCodes = { 12, 11, 10 };      // greater, heal, lesser
        private static readonly byte[] RefreshCodes = { 32, 31 };       // total refresh, refresh

        private static long _forceCureUntil;

        /// <summary>The Alt+U hotkey: for a few seconds the cure step runs as if the Cure option were ticked (the server names the right potion), so a player who keeps auto-cure off can still cure with one key.</summary>
        public static void CureByHotkey(World world)
        {
            if (!world.Player.IsPoisoned)
            {
                GameActions.Print(world, "You are not poisoned.");

                return;
            }

            _forceCureUntil = Time.Ticks + 4000;
            _noCureUntil = 0;
            _toldNoCure = false;
            _nextThink = 0;
        }

        /// <summary>The Alt+H and Alt+F hotkeys: drink the strongest Heal or Refresh potion in the pack, as clicking it on the potion bar would; says so when there is none or the bar is busy. Works with the potion bar on.</summary>
        public static void DrinkByHotkey(World world, bool heal)
        {
            PotionBarGump potions = UIManager.GetGump<PotionBarGump>();

            if (potions == null)
            {
                GameActions.Print(world, "The potion bar is not open.");

                return;
            }

            if (!potions.CanAct)
            {
                return;
            }

            if (!potions.DrinkStrongest(heal ? HealCodes : RefreshCodes))
            {
                GameActions.Print(world, heal ? "You have no Heal potion." : "You have no Refresh potion.");
            }
        }

        private static long _nextThink;
        private static long _nextAction;
        private static long _healReadyAt;
        private static long _cureAskedAt;
        private static long _cureReadyAt;
        private static long _noCureUntil;
        private static uint _curePotion;
        private static bool _toldNoCure;

        public static bool Allowed => TopBarGump.AutoUseAllowed;

        public static bool AnyOn(Profile p) => p != null && (p.AutoBandage || p.AutoHealPotion || p.AutoRefreshPotion || p.AutoCure);

        /// <summary>Whether this much of a pool is at or below the percentage.</summary>
        public static bool AtOrBelow(int value, int max, int percent) => max > 0 && value * 100 <= max * percent;

        public static bool Below(int value, int max, int percent) => max > 0 && value * 100 < max * percent;

        /// <summary>The Healing and Anatomy a bandage needs before it can cure a poison (the server's rule; a lower skill only wastes the bandage).</summary>
        public const double BandageCureSkill = 60.0;

        public static bool BandageCanCure(Mobile me) => SkillValue(me, "Healing") >= BandageCureSkill && SkillValue(me, "Anatomy") >= BandageCureSkill;

        private static double SkillValue(Mobile me, string name)
        {
            if (me is PlayerMobile player)
            {
                foreach (Skill skill in player.Skills)
                {
                    if (skill != null && skill.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return skill.Value;
                    }
                }
            }

            return 0;
        }

        /// <summary>The server's answer to "which cure for my poison": a count of 0 or 1, and for 1 the potion's serial and its chance in percent.</summary>
        public static void OnCureReply(ref StackDataReader p)
        {
            int count = p.Remaining > 0 ? p.ReadUInt8() : 0;

            if (count > 0 && p.Remaining >= 5)
            {
                _curePotion = p.ReadUInt32BE();
                p.ReadUInt8();
                _noCureUntil = 0;
            }
            else
            {
                _curePotion = 0;
                _noCureUntil = Time.Ticks + NoCureQuietMilliseconds;
            }
        }

        public static void Reset()
        {
            _curePotion = 0;
            _cureAskedAt = _cureReadyAt = _noCureUntil = _nextAction = _healReadyAt = 0;
            _toldNoCure = false;
        }

        /// <summary>Called by the bars every frame; thinks four times a second.</summary>
        public static void Tick(World world)
        {
            long now = Time.Ticks;

            if (now < _nextThink || !Allowed || world?.Player == null)
            {
                return;
            }

            _nextThink = now + ThinkMilliseconds;

            Profile profile = ProfileManager.CurrentProfile;
            PlayerMobile me = world.Player;

            if ((!AnyOn(profile) && now >= _forceCureUntil) || me.IsDead || now < _nextAction)
            {
                return;
            }

            // never with a hand-held item or a cursor the player is using for something else
            if (Client.Game.UO.GameCursor.ItemHold.Enabled || world.TargetManager.IsTargeting)
            {
                return;
            }

            PotionBarGump potions = UIManager.GetGump<PotionBarGump>();
            CombatBarGump combat = UIManager.GetGump<CombatBarGump>();
            bool poisoned = me.IsPoisoned;

            if (!poisoned)
            {
                _curePotion = 0;
                _cureAskedAt = 0;
                _toldNoCure = false;
            }

            if ((profile.AutoCure || now < _forceCureUntil) && poisoned && potions != null && potions.CanAct)
            {
                if (DoCure(world, potions, now))
                {
                    return;
                }
            }

            // a Heal potion refuses a poisoned drinker, so it is not tried while poisoned
            if (!poisoned && profile.AutoHealPotion && potions != null && potions.CanAct && now >= _healReadyAt && AtOrBelow(me.Hits, me.HitsMax, HealPotionAtPercent))
            {
                if (potions.DrinkStrongest(HealCodes))
                {
                    _healReadyAt = now + HealPotionWaitMilliseconds;
                    _nextAction = now + GapMilliseconds;

                    return;
                }
            }

            // a bandage when hurt; and when poisoned too, if the character's Healing and Anatomy are high enough for a bandage to cure (the Cure potion above comes first when there is one)
            bool bandageNeeded = poisoned ? BandageCanCure(me) : Below(me.Hits, me.HitsMax, BandageBelowPercent);

            if (profile.AutoBandage && bandageNeeded && combat != null && !BandageWatch.Active)
            {
                if (combat.TryBandageSelf())
                {
                    _nextAction = now + GapMilliseconds;

                    return;
                }
            }

            if (profile.AutoRefreshPotion && potions != null && potions.CanAct && AtOrBelow(me.Stamina, me.StaminaMax, RefreshPotionAtPercent))
            {
                if (potions.DrinkStrongest(RefreshCodes))
                {
                    _nextAction = now + GapMilliseconds;
                }
            }
        }

        /// <summary>Asks the server which cure to drink, then drinks the one it names. Returns true when it did something (asked or drank), so nothing else is tried this time.</summary>
        private static bool DoCure(World world, PotionBarGump potions, long now)
        {
            if (now < _cureReadyAt || now < _noCureUntil)
            {
                if (now < _noCureUntil && !_toldNoCure)
                {
                    _toldNoCure = true;
                    GameActions.Print(world, "You are poisoned, and you have no Cure potion that could cure it.", 0x22, MessageType.System);
                }

                return false;
            }

            if (_curePotion != 0)
            {
                Item potion = world.Items.Get(_curePotion);
                _curePotion = 0;

                if (potion != null && potions.DrinkItem(potion))
                {
                    _cureReadyAt = now + CureWaitMilliseconds;
                    _nextAction = now + GapMilliseconds;

                    return true;
                }

                return false;
            }

            if (now - _cureAskedAt >= CureAskMilliseconds)
            {
                _cureAskedAt = now;
                NetClient.Socket.Send_AutoUseRequest();

                return true;
            }

            return false;
        }
    }

    internal enum AutoUseOption { Bandage, HealPotion, RefreshPotion, Cure }

    /// <summary>The small panel a bar's gear button opens over the bar: a title and some check boxes bound to profile settings. It closes on a press anywhere outside it, on the gear again, or on Escape.</summary>
    internal sealed class BarOptionsGump : Gump
    {
        public sealed record Option(string Label, string Detail, Func<bool> Get, Action<bool> Set);

        private const int Pad = 8;
        private const int RowHeight = 24;
        private const int GraceMilliseconds = 200;
        private const byte Font = 1;
        private const ushort Ink = 0x0481;

        private readonly long _openedAt = Time.Ticks;
        private readonly Gump _bar;
        private readonly List<(Checkbox Box, Option Option)> _boxes = new();
        private bool _mouseWasDown = true;

        public BarOptionsGump(World world, Gump bar, string title, IReadOnlyList<Option> options) : base(world, 0, 0)
        {
            _bar = bar;
            CanMove = false;
            AcceptMouseInput = true;
            CanCloseWithRightClick = true;
            WantUpdateSize = false;
            LayerOrder = UILayer.Over;

            var background = new AlphaBlendControl(1f);
            Add(background);

            var heading = new Label(title, true, 0x0035, 0, Font, FontStyle.BlackBorder) { AcceptMouseInput = false, X = Pad, Y = Pad - 2 };
            Add(heading);

            int y = Pad + heading.Height + 6;
            int widest = heading.Width;

            foreach (Option option in options)
            {
                Option o = option;
                var box = new Checkbox(0x00D2, 0x00D3, o.Label, Font, Ink) { X = Pad, Y = y, IsChecked = o.Get() };

                box.ValueChanged += (_, _) =>
                {
                    // a change that came from elsewhere (the Options page) already is the value: nothing to do
                    if (o.Get() == box.IsChecked)
                    {
                        return;
                    }

                    o.Set(box.IsChecked);
                    ProfileManager.CurrentProfile?.Save(World, ProfileManager.ProfilePath);
                };

                _boxes.Add((box, o));
                Add(box);
                widest = Math.Max(widest, box.Width);
                y += box.Height + 1;

                // a plain sentence under the box, in grey, about what it does for the player
                var detail = new Label(o.Detail, true, 0x0386, 380, Font, FontStyle.BlackBorder) { AcceptMouseInput = false, X = Pad + 22, Y = y };
                Add(detail);
                widest = Math.Max(widest, detail.Width + 22);
                y += detail.Height + 8;
            }

            Width = widest + 2 * Pad;
            Height = y + Pad - 4;
            background.Width = Width;
            background.Height = Height;

            // above the bar, over the game view, where it covers none of the other bars; below it only when there is no room above
            X = Math.Clamp(bar.X, 0, Math.Max(0, Client.Game.ClientBounds.Width - Width));
            Y = bar.Y - Height - 4 >= 0 ? bar.Y - Height - 4 : bar.Y + bar.Height + 4;

            AutoUse.Changed += OnSwitchesChanged;
        }

        private void OnSwitchesChanged()
        {
            foreach ((Checkbox box, Option option) in _boxes)
            {
                box.IsChecked = option.Get();
            }
        }

        public override void Dispose()
        {
            AutoUse.Changed -= OnSwitchesChanged;
            base.Dispose();
        }

        public Gump Bar => _bar;

        public static void Toggle(World world, Gump bar, string title, IReadOnlyList<Option> options)
        {
            BarOptionsGump open = UIManager.GetGump<BarOptionsGump>();

            if (open != null)
            {
                bool same = ReferenceEquals(open.Bar, bar);
                open.Dispose();

                if (same)
                {
                    return;
                }
            }

            UIManager.Add(new BarOptionsGump(world, bar, title, options));
        }

        public override void Update()
        {
            base.Update();

            if (IsDisposed)
            {
                return;
            }

            if (_bar == null || _bar.IsDisposed || !_bar.IsVisible)
            {
                Dispose();

                return;
            }

            bool down = Mouse.LButtonPressed || Mouse.RButtonPressed;
            bool inside = new Rectangle(X, Y, Width, Height).Contains(Mouse.Position);
            bool onBar = new Rectangle(_bar.X, _bar.Y, _bar.Width, _bar.Height).Contains(Mouse.Position);

            // the press that opened the panel is still down when it appears: only a later press, outside the panel and its bar, closes it
            if (down && !_mouseWasDown && !inside && !onBar && Time.Ticks - _openedAt > GraceMilliseconds)
            {
                Dispose();

                return;
            }

            _mouseWasDown = down;
        }
    }
}
