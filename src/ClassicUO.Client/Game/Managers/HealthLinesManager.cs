// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.Managers
{
    internal sealed class HealthLinesManager
    {
        private const int BAR_WIDTH = 34; //28;
        private const int BAR_HEIGHT = 8;
        private const int BAR_WIDTH_HALF = BAR_WIDTH >> 1;
        private const int BAR_HEIGHT_HALF = BAR_HEIGHT >> 1;

        // a bar that belongs to a possible threat (see FriendlyHealthTracker.IsQuiet) is wider and filled amber instead of teal, so it cannot be taken for a friend's or an animal's
        private const int THREAT_BAR_WIDTH = 50;
        private const int THREAT_EXTRA_HEIGHT = 0;
        private const ushort THREAT_FILL_HUE = 0x0030;

        const ushort BACKGROUND_GRAPHIC = 0x1068;
        const ushort HP_GRAPHIC = 0x1069;

        private readonly World _world;
        private readonly FriendlyHealthTracker _friendlyHealth = new FriendlyHealthTracker();

        public HealthLinesManager(World world) { _world = world; }

        private bool IsQuiet(Mobile mobile) =>
            FriendlyHealthTracker.IsQuiet(mobile.NotorietyFlag, mobile.IsHuman, mobile.Serial == _world.Player.Serial);

        public bool IsEnabled =>
            ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.ShowMobilesHP;

        public void Draw(UltimaBatcher2D batcher, float layerDepth)
        {
            var camera = Client.Game.Scene.Camera;
            int mode = ProfileManager.CurrentProfile.MobileHPType;

            if (mode < 0)
            {
                return;
            }

            int showWhen = ProfileManager.CurrentProfile.MobileHPShowWhen;
            var useNewTargetSystem = ProfileManager.CurrentProfile.UseNewTargetSystem;
            var animations = Client.Game.UO.Animations;
            var isEnabled = IsEnabled;
            bool hideFullFriendly = ProfileManager.CurrentProfile.HideFullFriendlyHealth;

            foreach (Mobile mobile in _world.Mobiles.Values)
            {
                if (mobile.IsDestroyed)
                {
                    continue;
                }

                var newTargSystem = false;
                var forceDraw = false;
                var passive = mobile.Serial != _world.Player.Serial;

                if (_world.TargetManager.LastTargetInfo.Serial == mobile ||
                    _world.TargetManager.LastAttack == mobile ||
                    _world.TargetManager.SelectedTarget == mobile ||
                    _world.TargetManager.NewTargetSystemSerial == mobile)
                {
                    newTargSystem = useNewTargetSystem && _world.TargetManager.NewTargetSystemSerial == mobile;
                    passive = false;
                    forceDraw = true;
                }

                int current = mobile.Hits;
                int max = mobile.HitsMax;

                if (!newTargSystem)
                {
                    if (max == 0)
                    {
                        continue;
                    }

                    if (showWhen == 1 && current == max)
                    {
                        continue;
                    }

                    // a targeted friend or animal is left out too: the rule is the same for all of them, so a missing bar always means full health
                    if (hideFullFriendly && _friendlyHealth.ShouldHide(mobile.Serial, IsQuiet(mobile), current, max, Time.Ticks))
                    {
                        continue;
                    }
                }

                Point p = mobile.RealScreenPosition;
                p.X += (int)mobile.Offset.X + 22 + 5;
                p.Y += (int)(mobile.Offset.Y - mobile.Offset.Z) + 22 + 5;
                var offsetY = 0;

                if (isEnabled)
                {
                    if (mode != 1 && !mobile.IsDead)
                    {
                        if (showWhen == 2 && current != max || showWhen <= 1)
                        {
                            if (mobile.HitsPercentage != 0)
                            {
                                animations.GetAnimationDimensions(
                                    mobile.AnimIndex,
                                    mobile.GetGraphicForAnimation(),
                                    /*(byte) m.GetDirectionForAnimation()*/
                                    0,
                                    /*Mobile.GetGroupForAnimation(m, isParent:true)*/
                                    0,
                                    mobile.IsMounted,
                                    /*(byte) m.AnimIndex*/
                                    0,
                                    out int centerX,
                                    out int centerY,
                                    out int width,
                                    out int height
                                );

                                Point p1 = p;
                                p1.Y -= height + centerY + 8 + 22;

                                if (mobile.IsGargoyle && mobile.IsFlying)
                                {
                                    p1.Y -= 22;
                                }
                                else if (!mobile.IsMounted)
                                {
                                    p1.Y += 22;
                                }

                                p1 = Client.Game.Scene.Camera.WorldToScreen(p1, true);
                                p1.X -= (mobile.HitsTexture.Width >> 1) + 5;
                                p1.Y -= mobile.HitsTexture.Height;

                                if (mobile.ObjectHandlesStatus == ObjectHandlesStatus.DISPLAYING)
                                {
                                    int ohHeight = Constants.OBJECT_HANDLES_GUMP_HEIGHT
                                        + (ProfileManager.CurrentProfile.NameOverheadShowHpBar
                                            ? Constants.OBJECT_HANDLES_HP_BAR_HEIGHT + 1 : 0);
                                    p1.Y -= ohHeight + 5;
                                    offsetY += ohHeight + 5;
                                }

                                if (
                                    !(
                                        p1.X < 0
                                        || p1.X > camera.Bounds.Width - mobile.HitsTexture.Width
                                        || p1.Y < 0
                                        || p1.Y > camera.Bounds.Height
                                    )
                                )
                                {
                                    mobile.HitsTexture.Draw(batcher, p1.X, p1.Y, layerDepth);
                                }

                                if (newTargSystem)
                                {
                                    offsetY += mobile.HitsTexture.Height;
                                }
                            }
                        }
                    }
                }

                p.X -= 5;
                p = Client.Game.Scene.Camera.WorldToScreen(p, true);

                p.X -= BAR_WIDTH_HALF;
                p.Y -= BAR_HEIGHT_HALF;

                if (p.X < 0 || p.X > camera.Bounds.Width - BAR_WIDTH)
                {
                    continue;
                }

                if (p.Y < 0 || p.Y > camera.Bounds.Height - BAR_HEIGHT)
                {
                    continue;
                }

                if ((isEnabled && mode >= 1) || newTargSystem || forceDraw)
                {
                    bool threat = !newTargSystem && !IsQuiet(mobile);

                    DrawHealthLine(batcher, mobile, p.X, p.Y, offsetY, passive, newTargSystem, threat, layerDepth);
                }
            }

            _friendlyHealth.Prune(Time.Ticks);
        }

        /// <summary>The stock bar tiles its fill art, which is right at the stock width; a threat's wider bar stretches it instead, so the art's edge does not repeat inside the bar.</summary>
        private static void DrawBarPart(UltimaBatcher2D batcher, bool stretch, Microsoft.Xna.Framework.Graphics.Texture2D texture, Rectangle destination, Rectangle source, Vector3 hue, float layerDepth)
        {
            if (stretch)
            {
                batcher.Draw(texture, destination, source, hue, layerDepth);
            }
            else
            {
                batcher.DrawTiled(texture, destination, source, hue, layerDepth);
            }
        }

        private void DrawHealthLine(
            UltimaBatcher2D batcher,
            Entity entity,
            int x,
            int y,
            int offsetY,
            bool passive,
            bool newTargetSystem,
            bool threat,
            float layerDepth
        )
        {
            if (entity == null)
            {
                return;
            }

            int barWidth = threat ? THREAT_BAR_WIDTH : BAR_WIDTH;
            int per = barWidth * entity.HitsPercentage / 100;

            Mobile mobile = entity as Mobile;

            float alpha = passive && !newTargetSystem ? 0.5f : 1.0f;
            ushort hue =
                mobile != null
                    ? Notoriety.GetHue(mobile.NotorietyFlag)
                    : Notoriety.GetHue(NotorietyFlag.Gray);

            //Vector3 hueVec = ShaderHueTranslator.GetHueVector(hue, false, alpha);
            Vector3 hueVecZero = ShaderHueTranslator.GetHueVector(0, false, alpha);
            Vector3 hueVecNoto = ShaderHueTranslator.GetHueVector(hue, false, alpha);

            if (mobile == null)
            {
                y += 22;
            }

            const int MULTIPLER = 1;

            if (newTargetSystem && mobile != null && mobile.Serial != _world.Player.Serial)
            {
                Client.Game.UO.Animations.GetAnimationDimensions(
                    mobile.AnimIndex,
                    mobile.GetGraphicForAnimation(),
                    (byte) mobile.GetDirectionForAnimation(),
                    Mobile.GetGroupForAnimation(mobile, isParent: true),
                    mobile.IsMounted,
                    0, //mobile.AnimIndex,
                    out int centerX,
                    out int centerY,
                    out int width,
                    out int height
                );

                uint topGump;
                uint bottomGump;
                //uint gumpHue = 0x7570;
                uint gumpHue = 0x7572; // gray

                if (mobile != null)
                {
                    if (mobile.NotorietyFlag == NotorietyFlag.Innocent)
                        gumpHue = 0x7570; // blue

                    else if (mobile.NotorietyFlag == NotorietyFlag.Ally)
                        gumpHue = 0x7571; // green

                    else if (mobile.NotorietyFlag == NotorietyFlag.Criminal || mobile.NotorietyFlag == NotorietyFlag.Gray)
                        gumpHue = 0x7572; // grey

                    else if (mobile.NotorietyFlag == NotorietyFlag.Enemy)
                        gumpHue = 0x7573; // orange

                    if (mobile.NotorietyFlag == NotorietyFlag.Invulnerable)
                        gumpHue = 0x7575; // yellow

                    else if (mobile.NotorietyFlag == NotorietyFlag.Murderer)
                        gumpHue = 0x7577; // red
                }

                if (width >= 80)
                {
                    topGump = 0x756D;
                    bottomGump = 0x756A;
                }
                else if (width >= 40)
                {
                    topGump = 0x756E;
                    bottomGump = 0x756B;
                }
                else
                {
                    topGump = 0x756F;
                    bottomGump = 0x756C;
                }

                ref readonly var hueGumpInfo = ref Client.Game.UO.Gumps.GetGump(gumpHue);
                var targetX = x + BAR_WIDTH_HALF - hueGumpInfo.UV.Width / 2f;
                var topTargetY = height + centerY + 8 + 22 + offsetY;

                ref readonly var newTargGumpInfo = ref Client.Game.UO.Gumps.GetGump(topGump);
                if (newTargGumpInfo.Texture != null)
                    batcher.Draw(
                        newTargGumpInfo.Texture,
                        new Vector2(targetX, y - topTargetY),
                        newTargGumpInfo.UV,
                        hueVecZero,
                        layerDepth
                    );

                if (hueGumpInfo.Texture != null)
                    batcher.Draw(
                        hueGumpInfo.Texture,
                        new Vector2(targetX, y - topTargetY),
                        hueGumpInfo.UV,
                        hueVecZero,
                        layerDepth
                    );

                y += 7 + newTargGumpInfo.UV.Height / 2 - centerY;

                newTargGumpInfo = ref Client.Game.UO.Gumps.GetGump(bottomGump);
                if (newTargGumpInfo.Texture != null)
                    batcher.Draw(
                        newTargGumpInfo.Texture,
                        new Vector2(targetX, y - 1 - newTargGumpInfo.UV.Height / 2f),
                        newTargGumpInfo.UV,
                        hueVecZero,
                        layerDepth
                    );
            }


            ref readonly var gumpInfo = ref Client.Game.UO.Gumps.GetGump(BACKGROUND_GRAPHIC);

            int backgroundHeight = gumpInfo.UV.Height;
            int barHeight = threat ? backgroundHeight + THREAT_EXTRA_HEIGHT : backgroundHeight;
            int bx = x - (barWidth - BAR_WIDTH) / 2;
            int by = y - (barHeight - backgroundHeight) / 2;

            batcher.Draw(
                gumpInfo.Texture,
                new Rectangle(bx, by, (threat ? barWidth : gumpInfo.UV.Width) * MULTIPLER, barHeight * MULTIPLER),
                gumpInfo.UV,
                hueVecNoto,
                layerDepth
            );

            hueVecNoto.X = 0x21;

            if (entity.Hits != entity.HitsMax || entity.HitsMax == 0)
            {
                int offset = 2;

                if (per >> 2 == 0)
                {
                    offset = per;
                }

                gumpInfo = ref Client.Game.UO.Gumps.GetGump(HP_GRAPHIC);

                DrawBarPart(
                    batcher,
                    threat,
                    gumpInfo.Texture,
                    new Rectangle(
                        bx + per * MULTIPLER - offset,
                        by,
                        (barWidth - per) * MULTIPLER - offset / 2,
                        (threat ? barHeight : gumpInfo.UV.Height) * MULTIPLER
                    ),
                    gumpInfo.UV,
                    hueVecNoto,
                    layerDepth
                );
            }

            hue = threat ? THREAT_FILL_HUE : (ushort) 90;

            if (per > 0)
            {
                if (mobile != null)
                {
                    if (mobile.IsPoisoned)
                    {
                        hue = 63;
                    }
                    else if (mobile.IsYellowHits)
                    {
                        hue = 53;
                    }
                }

                hueVecNoto.X = hue;

                gumpInfo = ref Client.Game.UO.Gumps.GetGump(HP_GRAPHIC);
                DrawBarPart(
                    batcher,
                    threat,
                    gumpInfo.Texture,
                    new Rectangle(bx, by, per * MULTIPLER, (threat ? barHeight : gumpInfo.UV.Height) * MULTIPLER),
                    gumpInfo.UV,
                    hueVecNoto,
                    layerDepth
                );
            }
        }
    }
}
