// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Data;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Decides when the health bar of a friendly or neutral character is left out because the character is at full health (UO Rekindled: less clutter, and a missing bar means full health).
    /// A bar that has just reached full health stays for <see cref="LingerMilliseconds"/> and goes only if the character is still full by then; losing health puts it back at once.
    /// </summary>
    internal sealed class FriendlyHealthTracker
    {
        public const uint LingerMilliseconds = 5000;

        // the last time a friendly character was seen below full health
        private readonly Dictionary<uint, uint> _lastWounded = new();

        public int Tracked => _lastWounded.Count;

        /// <summary>
        /// Whose bar can be left out at full health: the player's own, and anyone who is not a threat by their colour: blue (innocent), green (a friend, a guildmate or a pet), yellow (invulnerable, such as a shopkeeper)
        /// and grey creatures that are not human in shape (the animals). Grey in a human body can be a player who has switched Criminal Intent on, a criminal is grey too, and orange and red are enemies: those keep their bars.
        /// </summary>
        public static bool IsQuiet(NotorietyFlag flag, bool humanBody, bool isSelf) =>
            isSelf
            || flag == NotorietyFlag.Innocent
            || flag == NotorietyFlag.Ally
            || flag == NotorietyFlag.Invulnerable
            || (flag == NotorietyFlag.Gray && !humanBody);

        /// <summary>True when the bar is to be left out. Call it once a frame for every character whose bar could be drawn. <paramref name="quiet"/> is <see cref="IsQuiet"/>.</summary>
        public bool ShouldHide(uint serial, bool quiet, int hits, int hitsMax, uint now)
        {
            if (!quiet)
            {
                _lastWounded.Remove(serial);

                return false;
            }

            if (hits < hitsMax)
            {
                _lastWounded[serial] = now;

                return false;
            }

            if (_lastWounded.TryGetValue(serial, out uint woundedAt))
            {
                if ((int) (now - woundedAt) < LingerMilliseconds)
                {
                    return false;
                }

                _lastWounded.Remove(serial);
            }

            return true;
        }

        /// <summary>Forgets characters whose bar has already gone (and any that were last seen wounded long ago, such as one that left the view).</summary>
        public void Prune(uint now)
        {
            if (_lastWounded.Count == 0)
            {
                return;
            }

            List<uint> gone = null;

            foreach (KeyValuePair<uint, uint> entry in _lastWounded)
            {
                if ((int) (now - entry.Value) >= LingerMilliseconds * 12)
                {
                    (gone ??= new List<uint>()).Add(entry.Key);
                }
            }

            if (gone != null)
            {
                foreach (uint serial in gone)
                {
                    _lastWounded.Remove(serial);
                }
            }
        }

        public void Clear() => _lastWounded.Clear();
    }
}
