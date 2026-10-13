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

        // "Your fingers slip!" is not here: the server only counts a slip (damage taken while healing) against the bandage's chance, and the bandage carries on
        private static readonly string[] Ended =
        {
            "finish applying", "stay close enough", "before you died", "bind the wound", "failed to cure", "heal what little", "not damaged",
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

        /// <summary>When a bandage on yourself should be over: the server's own delay for this era (not the later AOS one, which is shorter), 9.4 s plus 0.6 s for each 10 points of Dexterity under 120, and a margin. Too short an estimate would let a second bandage replace the one still being applied.</summary>
        private static long EndEstimateMilliseconds(int dexterity)
        {
            double seconds = Math.Clamp(9.4 + 0.6 * ((120 - dexterity) / 10.0), 8.0, 20.0);

            return (long)((seconds + 3.0) * 1000);
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
                            _stage = Stage.Applying;
                            _until = now + EndEstimateMilliseconds(_world.Player.Dexterity);
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

            // the server's own word that the bandage is on, which is quicker than watching the cursor (a hotkey picks the target within the same frame)
            if (e.Text.Contains("begin applying", StringComparison.OrdinalIgnoreCase) && _world?.Player != null)
            {
                _stage = Stage.Applying;
                _until = Time.Ticks + EndEstimateMilliseconds(_world.Player.Dexterity);

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
