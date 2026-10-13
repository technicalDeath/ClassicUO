// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Where the windows a new player opens first (the journal, the skills window, the small map) appear. The stock client opens each in the top-left corner of the screen, on top of the
    /// paperdoll and of each other. The shard's default layout leaves the game view about 70% of the window, so there is a column of black to its right: new windows go there, one under
    /// the other, below the item shortcut bar. When the window is too narrow for that, or the column is full, the stock position is used. The player can still drag a window anywhere.
    /// </summary>
    internal static class ShardLayout
    {
        private const int Margin = 8;
        private const int TopOffset = 62;   // clear of the item shortcut bar

        private static readonly List<Gump> Docked = new();

        /// <summary>The top of a window that the stock client opens at the very top of the screen (the shop windows at y of 0 to 5): below the top bar, which is drawn above every window and would hide the shop's title and first rows.</summary>
        public static int BelowTopBar(int y)
        {
            TopBarGump bar = UIManager.GetGump<TopBarGump>();

            return bar != null && bar.IsVisible ? Math.Max(y, bar.Y + bar.Height + 4) : y;
        }

        /// <summary>
        /// Where a bar that did not fit as a new row under the others goes instead: beside one of the bars already in place, on the same row, when the window is wide enough. The caller passes
        /// the bars it would stack under, nearest first. Returns false when there is no room either way.
        /// </summary>
        public static bool BesideAnother(Gump bar, params Gump[] others)
        {
            Rectangle screen = Client.Game.ClientBounds;

            foreach (Gump other in others)
            {
                if (other == null || !other.IsVisible)
                {
                    continue;
                }

                int x = other.X + other.Width + Margin;

                if (x + bar.Width + 4 <= screen.Width && other.Y + bar.Height + 2 <= screen.Height)
                {
                    bar.X = x;
                    bar.Y = other.Y;

                    return true;
                }
            }

            return false;
        }

        /// <summary>Places <paramref name="gump"/> in the black column to the right of the game window. Returns false (and leaves it where it is) when there is no room.</summary>
        public static bool Dock(Gump gump, int width, int height)
        {
            GameScene scene = Client.Game.GetScene<GameScene>();

            if (scene == null)
            {
                return false;
            }

            Docked.RemoveAll(g => g == null || g.IsDisposed || !g.IsVisible);

            Rectangle view = scene.Camera.Bounds;
            Rectangle screen = Client.Game.ClientBounds;
            int x = view.Right + Margin;
            int y = view.Y + TopOffset;

            foreach (Gump other in Docked)
            {
                y = Math.Max(y, other.Y + other.Height + Margin);
            }

            if (screen.Width - x < width + Margin || y + height > screen.Height - Margin)
            {
                return false;
            }

            gump.X = x;
            gump.Y = y;
            Docked.Add(gump);

            return true;
        }
    }
}
