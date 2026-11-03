// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.IO;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// What you gathered floats up from your character: the item's picture in its colour and the count, "+5", rising and fading (the owner: the picture,
    /// not the word). The shard (UO Rekindled, GatheredItemPopups on the server) sends extended command 0xB5 to the gatherer when a harvest gives an
    /// item: the serial, the art number, the hue, the amount and a short name, which shows only for an item whose art the client lacks. A stock server
    /// never sends it. The Options switch "Show what you gather over your head" turns it off.
    /// </summary>
    internal static class GatheredItemPopups
    {
        public const int CommandId = 0xB5;

        private const uint LIFE_MS = 1600;
        private const uint FADE_MS = 500;
        private const int RISE = 34;
        private const int ICON = 30;
        private const int GAP = 3;

        // starts above the progress bars over the head (their gap and a step for each, at least one, counted when the pop-up starts), so the next bar
        // of auto-repeat never covers it
        private const int START_ABOVE_HEAD = ActionProgressBars.GAP_ABOVE_HEAD;

        // a faint dark plate behind the picture and text, so a picture reads against trees, rock and water alike
        private const int PLATE_PAD = 3;
        private const float PLATE_ALPHA = 0.55f;
        private static readonly Color Plate = new Color(20, 16, 12);

        // two gains at once (a bonus item) stack, the later one a line lower
        private const uint STACK_WINDOW_MS = 400;
        private const int LINE = 20;

        private sealed class Popup
        {
            public uint Serial;
            public ushort Graphic;
            public ushort Hue;
            public RenderedText Text;
            public bool HasArt;
            public uint Start;
            public int Line;
            public int Above;
        }

        private static readonly List<Popup> _popups = new List<Popup>();

        public static void OnCommand(World world, ref StackDataReader p)
        {
            if (p.Remaining < 11)
            {
                return;
            }

            uint serial = p.ReadUInt32BE();
            ushort graphic = p.ReadUInt16BE();
            ushort hue = p.ReadUInt16BE();
            ushort amount = p.ReadUInt16BE();
            int nameLength = p.ReadUInt8();
            string name = p.ReadASCII(System.Math.Min(nameLength, p.Remaining));

            if (amount == 0 || ProfileManager.CurrentProfile?.ShowGatheredItemPopups == false)
            {
                return;
            }

            bool hasArt = Client.Game.UO.Arts.GetArt(graphic).Texture != null;
            int line = 0;

            foreach (Popup other in _popups)
            {
                if (other.Serial == serial && Time.Ticks - other.Start < STACK_WINDOW_MS)
                {
                    line = System.Math.Max(line, other.Line + 1);
                }
            }

            _popups.Add(
                new Popup
                {
                    Serial = serial,
                    Graphic = graphic,
                    Hue = hue,
                    Text = RenderedText.Create(hasArt ? $"+{amount}" : $"{name} +{amount}", 0xFFFF, 1, true, FontStyle.BlackBorder),
                    HasArt = hasArt,
                    Start = Time.Ticks,
                    Line = line,
                    Above = START_ABOVE_HEAD + System.Math.Max(1, ActionProgressBars.Count(serial)) * ActionProgressBars.STEP
                }
            );
        }

        public static void Clear()
        {
            foreach (Popup popup in _popups)
            {
                popup.Text.Destroy();
            }

            _popups.Clear();
        }

        public static void Draw(World world, UltimaBatcher2D batcher, float layerDepth)
        {
            if (_popups.Count == 0)
            {
                return;
            }

            var camera = Client.Game.Scene.Camera;

            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                Popup popup = _popups[i];
                uint age = Time.Ticks - popup.Start;
                Mobile mobile = world.Mobiles.Get(popup.Serial);

                if (mobile == null || mobile.IsDestroyed || age > LIFE_MS)
                {
                    popup.Text.Destroy();
                    _popups.RemoveAt(i);

                    continue;
                }

                float alpha = age < LIFE_MS - FADE_MS ? 1f : (LIFE_MS - age) / (float)FADE_MS;
                int icon = popup.HasArt ? ICON + GAP : 0;
                int width = icon + popup.Text.Width + PLATE_PAD * 2;
                int height = System.Math.Max(ICON, popup.Text.Height);

                Point head = ActionProgressBars.HeadTop(mobile);
                int x = head.X - width / 2;
                int y = head.Y - popup.Above - height + popup.Line * LINE - (int)(RISE * (long)age / LIFE_MS);

                if (x < 0 || x > camera.Bounds.Width - width || y < 0 || y > camera.Bounds.Height - height)
                {
                    continue;
                }

                batcher.Draw(
                    SolidColorTextureCache.GetTexture(Plate),
                    new Rectangle(x, y, width, height),
                    ShaderHueTranslator.GetHueVector(0, false, alpha * PLATE_ALPHA),
                    layerDepth
                );

                if (popup.HasArt)
                {
                    DrawArt(batcher, popup.Graphic, popup.Hue, alpha, x + PLATE_PAD, y + (height - ICON) / 2, layerDepth);
                }

                popup.Text.Draw(batcher, x + PLATE_PAD + icon, y + (height - popup.Text.Height) / 2, layerDepth, alpha);
            }
        }

        // the item's picture trimmed to what is drawn in it and fitted into an ICON-sized box, never enlarged
        private static void DrawArt(UltimaBatcher2D batcher, ushort graphic, ushort hue, float alpha, int boxX, int boxY, float layerDepth)
        {
            ref readonly var art = ref Client.Game.UO.Arts.GetArt(graphic);

            if (art.Texture == null)
            {
                return;
            }

            Rectangle real = Client.Game.UO.Arts.GetRealArtBounds(graphic);
            Rectangle source = new Rectangle(art.UV.X + real.X, art.UV.Y + real.Y, real.Width, real.Height);
            float scale = System.Math.Min(1f, System.Math.Min(ICON / (float)System.Math.Max(1, real.Width), ICON / (float)System.Math.Max(1, real.Height)));
            int w = System.Math.Max(1, (int)(real.Width * scale));
            int h = System.Math.Max(1, (int)(real.Height * scale));
            Vector3 hueVector = ShaderHueTranslator.GetHueVector(hue, Client.Game.UO.FileManager.TileData.StaticData[graphic].IsPartialHue, alpha);

            batcher.Draw(art.Texture, new Rectangle(boxX + (ICON - w) / 2, boxY + (ICON - h) / 2, w, h), source, hueVector, layerDepth);
        }
    }
}
