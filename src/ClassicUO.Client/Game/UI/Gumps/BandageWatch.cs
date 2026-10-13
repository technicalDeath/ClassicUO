// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Game.Managers;
using ClassicUO.Utility;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Whether a bandage is being applied, for the combat bar and the item shortcut bar to show on their bandage slot. The server sends no message when a bandage starts, only when
    /// it ends or cannot start, so this follows the player's own steps: a bar uses the bandages (armed), the server's target cursor comes up (choosing), and the cursor goes away
    /// either because the player picked someone (the bandage is on: shown until the server says it finished or an estimate of its delay for healing yourself runs out) or because the
    /// player cancelled it (nothing to show). A cursor that never comes up, or a server message that says the bandage could not start or has ended, also stops it.
    /// </summary>
    internal static class BandageWatch
    {
        private enum Stage { None, Armed, Choosing, Applying }

        private const int ArmedMilliseconds = 3000;

        private static readonly string[] Ended =
        {
            "finish applying", "fingers slip", "stay close enough", "before you died", "bind the wound", "failed to cure", "heal what little", "not damaged",
            "cannot be used on that", "cannot heal", "can not heal", "too far away", "unable to resurrect", "fail to resurrect"
        };

        private static World _world;
        private static Stage _stage;
        private static long _until;
        private static int _cancelsAtStart;

        /// <summary>True while a bandage is actually being applied (not while the target is still being chosen).</summary>
        public static bool Active
        {
            get
            {
                Step();

                return _stage == Stage.Applying;
            }
        }

        public static void Start(World world)
        {
            if (!ReferenceEquals(_world, world))
            {
                _world = world;
                world.MessageManager.MessageReceived += OnMessage;
            }

            _stage = Stage.Armed;
            _until = Time.Ticks + ArmedMilliseconds;
            _cancelsAtStart = world.TargetManager.CancelCount;
        }

        private static void Step()
        {
            if (_stage == Stage.None || _world?.Player == null)
            {
                return;
            }

            long now = Time.Ticks;
            bool targeting = _world.TargetManager.IsTargeting;

            switch (_stage)
            {
                case Stage.Armed:
                    if (targeting)
                    {
                        _stage = Stage.Choosing;
                    }
                    else if (now > _until)
                    {
                        _stage = Stage.None;
                    }

                    break;

                case Stage.Choosing:
                    if (!targeting)
                    {
                        if (_world.TargetManager.CancelCount != _cancelsAtStart)
                        {
                            _stage = Stage.None;
                        }
                        else
                        {
                            double seconds = Math.Clamp(5.0 + 0.5 * ((120 - _world.Player.Dexterity) / 10.0), 4.0, 12.0);
                            _stage = Stage.Applying;
                            _until = now + (long)((seconds + 3.0) * 1000);
                        }
                    }

                    break;

                case Stage.Applying:
                    if (now >= _until)
                    {
                        _stage = Stage.None;
                    }

                    break;
            }
        }

        private static void OnMessage(object sender, MessageEventArgs e)
        {
            if (_stage == Stage.None || string.IsNullOrEmpty(e.Text))
            {
                return;
            }

            foreach (string text in Ended)
            {
                if (e.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    _stage = Stage.None;

                    return;
                }
            }
        }
    }
}
