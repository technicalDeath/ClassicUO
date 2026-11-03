// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.GameObjects;
using ClassicUO.IO;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// A small bar over a character while a timed action runs, with what it is doing written in it ("Chopping"), filling over the action's length.
    /// The shard (UO Rekindled, ActionProgressBars on the server) sends extended command 0xB4 to the player doing it: the serial, the length in
    /// milliseconds (0 clears the bar), the label and, after it, a colour theme by the kind of action. The client only draws; the action is the stock
    /// one, and a stock server never sends it. Each action has its own bar, known by its label: bars of different actions running at once (a camp
    /// being made while the player chops) stack upward from the head in the order they started, and a bar started again for the same label (the next
    /// swing) keeps its place. A clear names the bar it takes away; a clear labelled "-" (from a server that kept one bar) takes them all.
    /// </summary>
    internal static class ActionProgressBars
    {
        public const int CommandId = 0xB4;

        private const int MIN_WIDTH = 64;
        private const int PADDING_X = 8;
        internal const int GAP_ABOVE_HEAD = 18;

        // stacked bars sit this far apart; a bar and its gap make one step, which the gathered-item pop-ups keep clear of
        private const int STACK_GAP = 4;
        internal const int STEP = 22;

        // unicode font 1 leaves blank rows above and below the letters; the bar trims this many from each side so it hugs the text
        private const int TRIM_Y = 2;

        // filled the whole way, the bar stays a moment so the last of it can be seen
        private const uint LINGER_MS = 200;

        private static readonly Color Background = new Color(20, 16, 12);

        // the fill by theme (the owner approved these on 2026-11-02); 0, from a server that sends no theme, is gathering's amber
        private static readonly Color[] Fills =
        {
            new Color(150, 104, 40), // 0: no theme
            new Color(150, 104, 40), // 1 gathering
            new Color(55, 95, 150),  // 2 crafting
            new Color(45, 125, 60),  // 3 healing and help
            new Color(155, 40, 40),  // 4 harmful
            new Color(110, 65, 150), // 5 magic
            new Color(35, 118, 112), // 6 animals
            new Color(92, 92, 104),  // 7 stealth and locks
            new Color(140, 50, 95)   // 8 camp and rest
        };
        private static readonly Color Border = Color.Black;

        private sealed class Bar
        {
            public string Name;
            public RenderedText Label;
            public uint Start;
            public uint Length;
            public Color Fill;
        }

        // each mobile's bars, the first nearest the head
        private static readonly Dictionary<uint, List<Bar>> _bars = new Dictionary<uint, List<Bar>>();
        private static readonly List<uint> _ended = new List<uint>();

        public static void OnCommand(World world, ref StackDataReader p)
        {
            if (p.Remaining < 7)
            {
                return;
            }

            uint serial = p.ReadUInt32BE();
            ushort length = p.ReadUInt16BE();
            int labelLength = p.ReadUInt8();
            string label = p.ReadASCII(System.Math.Min(labelLength, p.Remaining));
            int theme = p.Remaining > 0 ? p.ReadUInt8() : 0;

            // no label, or "-": the clear of a server that kept one bar, which takes them all
            if (string.IsNullOrEmpty(label) || label == "-")
            {
                Remove(serial);

                return;
            }

            if (!_bars.TryGetValue(serial, out List<Bar> bars))
            {
                if (length == 0)
                {
                    return;
                }

                _bars[serial] = bars = new List<Bar>();
            }

            int index = bars.FindIndex(bar => bar.Name == label);

            if (length == 0)
            {
                if (index >= 0)
                {
                    bars[index].Label.Destroy();
                    bars.RemoveAt(index);
                }

                if (bars.Count == 0)
                {
                    _bars.Remove(serial);
                }

                return;
            }

            var started = new Bar
            {
                Name = label,
                Label = RenderedText.Create(label, 0xFFFF, 1, true, FontStyle.BlackBorder),
                Start = Time.Ticks,
                Length = length,
                Fill = Fills[theme < Fills.Length ? theme : 0]
            };

            if (index >= 0)
            {
                bars[index].Label.Destroy();
                bars[index] = started;
            }
            else
            {
                bars.Add(started);
            }
        }

        /// <summary>How many bars are over the mobile now.</summary>
        internal static int Count(uint serial) => _bars.TryGetValue(serial, out List<Bar> bars) ? bars.Count : 0;

        public static void Clear()
        {
            foreach (List<Bar> bars in _bars.Values)
            {
                foreach (Bar bar in bars)
                {
                    bar.Label.Destroy();
                }
            }

            _bars.Clear();
        }

        private static void Remove(uint serial)
        {
            if (_bars.TryGetValue(serial, out List<Bar> bars))
            {
                foreach (Bar bar in bars)
                {
                    bar.Label.Destroy();
                }

                _bars.Remove(serial);
            }
        }

        public static void Draw(World world, UltimaBatcher2D batcher, float layerDepth)
        {
            if (_bars.Count == 0)
            {
                return;
            }

            var camera = Client.Game.Scene.Camera;
            Vector3 solid = ShaderHueTranslator.GetHueVector(0, false, 1f);

            foreach (var (serial, bars) in _bars)
            {
                Mobile mobile = world.Mobiles.Get(serial);

                if (mobile == null || mobile.IsDestroyed || mobile.IsDead)
                {
                    _ended.Add(serial);

                    continue;
                }

                // a bar that has run out goes, and the ones above it step down into its place
                for (int i = bars.Count - 1; i >= 0; i--)
                {
                    if (Time.Ticks - bars[i].Start > bars[i].Length + LINGER_MS)
                    {
                        bars[i].Label.Destroy();
                        bars.RemoveAt(i);
                    }
                }

                if (bars.Count == 0)
                {
                    _ended.Add(serial);

                    continue;
                }

                Point p = HeadTop(mobile);
                int y = p.Y - GAP_ABOVE_HEAD;

                foreach (Bar bar in bars)
                {
                    uint elapsed = Time.Ticks - bar.Start;
                    int width = System.Math.Max(MIN_WIDTH, bar.Label.Width + PADDING_X * 2);
                    int height = bar.Label.Height + 2 - TRIM_Y * 2;
                    int filled = (int)(width * (long)System.Math.Min(elapsed, bar.Length) / bar.Length);
                    int x = p.X - width / 2;

                    y -= height;

                    if (x >= 0 && x <= camera.Bounds.Width - width && y >= 0 && y <= camera.Bounds.Height - height)
                    {
                        batcher.Draw(SolidColorTextureCache.GetTexture(Background), new Rectangle(x, y, width, height), solid, layerDepth);

                        if (filled > 0)
                        {
                            batcher.Draw(SolidColorTextureCache.GetTexture(bar.Fill), new Rectangle(x, y, filled, height), solid, layerDepth);
                        }

                        batcher.DrawRectangle(SolidColorTextureCache.GetTexture(Border), x, y, width, height, solid, layerDepth);
                        bar.Label.Draw(batcher, x + (width - bar.Label.Width) / 2, y + 1 - TRIM_Y, layerDepth);
                    }

                    y -= STACK_GAP;
                }
            }

            foreach (uint serial in _ended)
            {
                Remove(serial);
            }

            _ended.Clear();
        }

        /// <summary>The top of the mobile's head on screen, worked out as the stock overhead health text is (HealthLinesManager).</summary>
        internal static Point HeadTop(Mobile mobile)
        {
            Point p = mobile.RealScreenPosition;
            p.X += (int)mobile.Offset.X + 22;
            p.Y += (int)(mobile.Offset.Y - mobile.Offset.Z) + 22 + 5;

            Client.Game.UO.Animations.GetAnimationDimensions(
                mobile.AnimIndex,
                mobile.GetGraphicForAnimation(),
                0,
                0,
                mobile.IsMounted,
                0,
                out int centerX,
                out int centerY,
                out int width,
                out int height
            );

            p.Y -= height + centerY + 8 + 22;

            if (mobile.IsGargoyle && mobile.IsFlying)
            {
                p.Y -= 22;
            }
            else if (!mobile.IsMounted)
            {
                p.Y += 22;
            }

            return Client.Game.Scene.Camera.WorldToScreen(p, true);
        }
    }
}
