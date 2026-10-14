// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using System;
using System.Xml;

namespace ClassicUO.Game.UI.Gumps
{
    internal partial class ContainerGump : TextContainerGump
    {
        private long _corpseEyeTicks;
        private ContainerData _data;
        private int _eyeCorspeOffset;
        private GumpPic _eyeGumpPic;
        private GumpPicContainer _gumpPicContainer;
        private readonly bool _hideIfEmpty;
        private HitBox _hitBox;
        private bool _isMinimized;

        internal const int CORPSES_GUMP = 0x0009;

        public ContainerGump(World world) : base(world, 0, 0) { }

        public ContainerGump(World world, uint serial, ushort gumpid, bool playsound) : base(world, serial, 0)
        {
            Item item = world.Items.Get(serial);

            if (item == null)
            {
                Dispose();

                return;
            }

            Graphic = gumpid;

            // New Backpack gumps. Client Version 7.0.53.1
            if (
                item == world.Player.FindItemByLayer(Layer.Backpack)
                && Client.Game.UO.Version >= ClassicUO.Utility.ClientVersion.CV_705301
                && ProfileManager.CurrentProfile != null
            )
            {
                var gumps = Client.Game.UO.Gumps;

                switch (ProfileManager.CurrentProfile.BackpackStyle)
                {
                    case 1:
                        if (gumps.GetGump(0x775E).Texture != null)
                        {
                            Graphic = 0x775E; // Suede Backpack
                        }

                        break;
                    case 2:
                        if (gumps.GetGump(0x7760).Texture != null)
                        {
                            Graphic = 0x7760; // Polar Bear Backpack
                        }

                        break;
                    case 3:
                        if (gumps.GetGump(0x7762).Texture != null)
                        {
                            Graphic = 0x7762; // Ghoul Skin Backpack
                        }

                        break;
                    default:
                        if (gumps.GetGump(0x003C).Texture != null)
                        {
                            Graphic = 0x003C; // Default Backpack
                        }

                        break;
                }
            }

            _grid = WantsGrid(item, Graphic);

            BuildGump();

            if (Graphic == CORPSES_GUMP)
            {
                if (world.Player.ManualOpenedCorpses.Contains(LocalSerial))
                {
                    world.Player.ManualOpenedCorpses.Remove(LocalSerial);
                }
                else if (
                    world.Player.AutoOpenedCorpses.Contains(LocalSerial)
                    && ProfileManager.CurrentProfile != null
                    && ProfileManager.CurrentProfile.SkipEmptyCorpse
                )
                {
                    IsVisible = false;
                    _hideIfEmpty = true;
                }
            }

            if (_data.OpenSound != 0 && playsound)
            {
                Client.Game.Audio.PlaySound(_data.OpenSound);
            }
        }

        public ushort Graphic { get; }

        public override GumpType GumpType => GumpType.Container;

        public bool IsMinimized
        {
            get => _isMinimized;
            set
            {
                if (_grid)
                {
                    _isMinimized = value;
                    IsVisible = !value;

                    return;
                }

                //if (_isMinimized != value)
                {
                    _isMinimized = value;
                    _gumpPicContainer.Graphic = value ? _data.IconizedGraphic : Graphic;
                    float scale = GetScale();

                    Width = _gumpPicContainer.Width = (int)(_gumpPicContainer.Width * scale);
                    Height = _gumpPicContainer.Height = (int)(_gumpPicContainer.Height * scale);

                    foreach (Control c in Children)
                    {
                        c.IsVisible = !value;
                    }

                    _gumpPicContainer.IsVisible = true;

                    SetInScreen();
                }
            }
        }

        public bool IsChessboard =>
            Graphic == 0x091A;

        public bool IsBackgammonBoard =>
            Graphic == 0x092E;

        private void BuildGump()
        {
            if (_grid)
            {
                BuildGrid();

                return;
            }

            CanMove = true;
            CanCloseWithRightClick = true;
            WantUpdateSize = false;

            Item item = World.Items.Get(LocalSerial);

            if (item == null)
            {
                Dispose();

                return;
            }

            float scale = GetScale();

            _data = World.ContainerManager.Get(Graphic);
            ushort g = _data.Graphic;

            _gumpPicContainer?.Dispose();
            _hitBox?.Dispose();

            _hitBox = new HitBox(
                (int)(_data.MinimizerArea.X * scale),
                (int)(_data.MinimizerArea.Y * scale),
                (int)(_data.MinimizerArea.Width * scale),
                (int)(_data.MinimizerArea.Height * scale)
            );

            _hitBox.MouseUp += HitBoxOnMouseUp;
            Add(_hitBox);

            Add(_gumpPicContainer = new GumpPicContainer(0, 0, g, 0));
            _gumpPicContainer.MouseDoubleClick += GumpPicContainerOnMouseDoubleClick;
            _gumpPicContainer.Scale = scale;
            _gumpPicContainer.BaseScale = (IsChessboard || IsBackgammonBoard) ? 1f : UIManager.ContainerScale;
            _gumpPicContainer.NineSlice = IsBigPack && g == GumpPicContainer.BackpackGraphic;

            if (Graphic == CORPSES_GUMP)
            {
                _eyeGumpPic?.Dispose();
                Add(_eyeGumpPic = new GumpPic((int)(45 * scale), (int)(30 * scale), 0x0045, 0));

                _eyeGumpPic.Width = (int)(_eyeGumpPic.Width * scale);
                _eyeGumpPic.Height = (int)(_eyeGumpPic.Height * scale);
            }
            else if (ProfileManager.CurrentProfile.HueContainerGumps)
            {
                _gumpPicContainer.Hue = item.Hue;
            }

            Width = _gumpPicContainer.Width = (int)(_gumpPicContainer.Width * scale);
            Height = _gumpPicContainer.Height = (int)(_gumpPicContainer.Height * scale);

            AddSizeToggle();
            AddGridSwitch();
        }

        private void HitBoxOnMouseUp(object sender, MouseEventArgs e)
        {
            if (
                e.Button == MouseButtonType.Left
                && !IsMinimized
                && !Client.Game.UO.GameCursor.ItemHold.Enabled
            )
            {
                Point offset = Mouse.LDragOffset;

                if (
                    Math.Abs(offset.X) < Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS
                    && Math.Abs(offset.Y) < Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS
                )
                {
                    IsMinimized = true;
                }
            }
        }

        private void GumpPicContainerOnMouseDoubleClick(object sender, MouseDoubleClickEventArgs e)
        {
            if (e.Button == MouseButtonType.Left && IsMinimized)
            {
                IsMinimized = false;
                e.Result = true;
            }
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button != MouseButtonType.Left || UIManager.IsMouseOverWorld)
            {
                return;
            }

            Entity it = SelectedObject.Object as Entity;
            uint serial = it != null ? it.Serial : 0;
            uint dropcontainer = LocalSerial;
            int dropSlot = _grid ? SlotAt(x, y) : -1;

            if (
                World.TargetManager.IsTargeting
                && !Client.Game.UO.GameCursor.ItemHold.Enabled
                && SerialHelper.IsValid(serial)
            )
            {
                World.TargetManager.Target(serial);
                Mouse.CancelDoubleClick = true;

                if (World.TargetManager.TargetingState == CursorTarget.SetTargetClientSide)
                {
                    UIManager.Add(new InspectorGump(World, World.Get(serial)));
                }
            }
            else
            {
                Entity thisCont = World.Items.Get(dropcontainer);

                if (thisCont == null)
                {
                    return;
                }

                thisCont = World.Get(((Item)thisCont).RootContainer);

                if (thisCont == null)
                {
                    return;
                }

                bool candrop = thisCont.Distance <= Constants.DRAG_ITEMS_DISTANCE;

                if (candrop && SerialHelper.IsValid(serial))
                {
                    candrop = false;

                    if (
                        Client.Game.UO.GameCursor.ItemHold.Enabled
                        && !Client.Game.UO.GameCursor.ItemHold.IsFixedPosition
                    )
                    {
                        candrop = true;

                        Item target = World.Items.Get(serial);

                        if (target != null)
                        {
                            if (target.ItemData.IsContainer)
                            {
                                dropcontainer = target.Serial;
                                x = 0xFFFF;
                                y = 0xFFFF;
                            }
                            else if (
                                target.ItemData.IsStackable
                                && target.Graphic == Client.Game.UO.GameCursor.ItemHold.Graphic
                            )
                            {
                                dropcontainer = target.Serial;
                                x = target.X;
                                y = target.Y;
                            }
                            else
                            {
                                switch (target.Graphic)
                                {
                                    case 0x0EFA:
                                    case 0x2253:
                                    case 0x2252:
                                    case 0x238C:
                                    case 0x23A0:
                                    case 0x2D50:
                                    {
                                        dropcontainer = target.Serial;
                                        x = target.X;
                                        y = target.Y;

                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (
                    !candrop
                    && Client.Game.UO.GameCursor.ItemHold.Enabled
                    && !Client.Game.UO.GameCursor.ItemHold.IsFixedPosition
                )
                {
                    Client.Game.Audio.PlaySound(0x0051);
                }

                if (
                    candrop
                    && Client.Game.UO.GameCursor.ItemHold.Enabled
                    && !Client.Game.UO.GameCursor.ItemHold.IsFixedPosition
                )
                {
                    ContainerGump gump = UIManager.GetGump<ContainerGump>(dropcontainer);

                    if (
                        gump != null
                        && (
                            it == null
                            || it.Serial != dropcontainer
                                && it is Item item
                                && !item.ItemData.IsContainer
                        )
                    )
                    {
                        if (gump.IsChessboard)
                        {
                            y += 20;
                        }

                        Rectangle containerBounds = World.ContainerManager.Get(gump.Graphic).Bounds;

                        ref readonly var spriteInfo = ref (
                            (gump.IsChessboard || gump.IsBackgammonBoard)
                                ? ref Client.Game.UO.Gumps.GetGump(
                                    (ushort)(
                                        Client.Game.UO.GameCursor.ItemHold.DisplayedGraphic
                                        - Constants.ITEM_GUMP_TEXTURE_OFFSET
                                    )
                                )
                                : ref Client.Game.UO.Arts.GetArt(Client.Game.UO.GameCursor.ItemHold.DisplayedGraphic)
                        );

                        float scale = gump.GetScale();

                        containerBounds.X = (int)(containerBounds.X * scale);
                        containerBounds.Y = (int)(containerBounds.Y * scale);
                        containerBounds.Width = (int)(containerBounds.Width * scale);
                        containerBounds.Height = (int)(
                            (containerBounds.Height + (gump.IsChessboard ? 20 : 0)) * scale
                        );

                        if (spriteInfo.Texture != null)
                        {
                            int textureW,
                                textureH;

                            if (
                                ProfileManager.CurrentProfile != null
                                && ProfileManager.CurrentProfile.ScaleItemsInsideContainers
                            )
                            {
                                textureW = (int)(spriteInfo.UV.Width * scale);
                                textureH = (int)(spriteInfo.UV.Height * scale);
                            }
                            else
                            {
                                textureW = spriteInfo.UV.Width;
                                textureH = spriteInfo.UV.Height;
                            }

                            if (
                                ProfileManager.CurrentProfile != null
                                && ProfileManager.CurrentProfile.RelativeDragAndDropItems
                            )
                            {
                                x += Client.Game.UO.GameCursor.ItemHold.MouseOffset.X;
                                y += Client.Game.UO.GameCursor.ItemHold.MouseOffset.Y;
                            }

                            x -= textureW >> 1;
                            y -= textureH >> 1;

                            if (x + textureW > containerBounds.Width)
                            {
                                x = containerBounds.Width - textureW;
                            }

                            if (y + textureH > containerBounds.Height)
                            {
                                y = containerBounds.Height - textureH;
                            }
                        }

                        if (x < containerBounds.X)
                        {
                            x = containerBounds.X;
                        }

                        if (y < containerBounds.Y)
                        {
                            y = containerBounds.Y;
                        }

                        x = (int)(x / scale);
                        y = (int)(y / scale);
                    }

                    if (_grid && dropcontainer == LocalSerial)
                    {
                        GridNoteDrop(Client.Game.UO.GameCursor.ItemHold.Serial, dropSlot);
                        _relayout = true;
                    }

                    GameActions.DropItem(
                        Client.Game.UO.GameCursor.ItemHold.Serial,
                        x,
                        y,
                        0,
                        dropcontainer
                    );

                    Mouse.CancelDoubleClick = true;
                }
                else if (!Client.Game.UO.GameCursor.ItemHold.Enabled && SerialHelper.IsValid(serial))
                {
                    if (!World.DelayedObjectClickManager.IsEnabled)
                    {
                        Point off = Mouse.LDragOffset;

                        World.DelayedObjectClickManager.Set(
                            serial,
                            Mouse.Position.X - off.X - ScreenCoordinateX,
                            Mouse.Position.Y - off.Y - ScreenCoordinateY,
                            Time.Ticks + Mouse.MOUSE_DELAY_DOUBLE_CLICK
                        );
                    }
                }
            }
        }

        public override void Update()
        {
            base.Update();

            if (IsDisposed)
            {
                return;
            }

            Item item = World.Items.Get(LocalSerial);

            if (item == null || item.IsDestroyed)
            {
                Dispose();

                return;
            }

            if (
                UIManager.MouseOverControl != null
                && UIManager.MouseOverControl.RootParent == this
                && ProfileManager.CurrentProfile != null
                && ProfileManager.CurrentProfile.HighlightContainerWhenSelected
            )
            {
                SelectedObject.SelectedContainer = item;
            }

            if (_grid)
            {
                GridUpdate();

                return;
            }

            if (Graphic == CORPSES_GUMP && _corpseEyeTicks < Time.Ticks)
            {
                _eyeCorspeOffset = _eyeCorspeOffset == 0 ? 1 : 0;
                _corpseEyeTicks = (long)Time.Ticks + 750;
                _eyeGumpPic.Graphic = (ushort)(0x0045 + _eyeCorspeOffset);
                float scale = GetScale();
                _eyeGumpPic.Width = (int)(_eyeGumpPic.Width * scale);
                _eyeGumpPic.Height = (int)(_eyeGumpPic.Height * scale);
            }
        }

        protected override void UpdateContents()
        {
            if (_grid)
            {
                _relayout = true;

                return;
            }

            Clear();
            BuildGump();
            IsMinimized = IsMinimized;
            ItemsOnAdded();
        }

        public override void Save(XmlTextWriter writer)
        {
            base.Save(writer);
            writer.WriteAttributeString("graphic", Graphic.ToString());
            writer.WriteAttributeString("isminimized", IsMinimized.ToString());
        }

        public override void Restore(XmlElement xml)
        {
            base.Restore(xml);
            // skip loading

            Client.Game.GetScene<GameScene>()?.DoubleClickDelayed(LocalSerial);

            Dispose();
        }

        /// <summary>How much larger the big main pack is than an ordinary container window.</summary>
        public const float BigPackFactor = 1.5f;

        private bool IsMainPack
        {
            get
            {
                Item item = World.Items.Get(LocalSerial);

                return item != null && World.Player != null && item == World.Player.FindItemByLayer(Layer.Backpack);
            }
        }

        private bool IsBigPack =>
            ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.BigBackpack && IsMainPack;

        private float GetScale()
        {
            if (_grid)
            {
                return 1f;
            }

            return (IsChessboard || IsBackgammonBoard) ? 1f : UIManager.ContainerScale * (IsBigPack ? BigPackFactor : 1f);
        }

        /// <summary>The shard's own art for the size button: a brass diamond with a plus (make it larger) or a minus (make it smaller), see ShardContent/tools/gump-art/make_pack_icons.py.</summary>
        private const ushort LargerIcon = 62050;

        private const ushort SmallerIcon = 62051;

        private GumpPic _sizeIcon;
        private HitBox _sizeHit;

        private void AddSizeToggle()
        {
            _sizeIcon?.Dispose();
            _sizeHit?.Dispose();
            _sizeIcon = null;
            _sizeHit = null;

            if (_grid || !IsMainPack || IsChessboard || IsBackgammonBoard)
            {
                return;
            }

            float baseScale = UIManager.ContainerScale;

            _sizeIcon = new GumpPic(0, 0, IsBigPack ? SmallerIcon : LargerIcon, 0)
            {
                AcceptMouseInput = false
            };

            if (_sizeIcon.Width <= 0)
            {
                _sizeIcon.Dispose();
                _sizeIcon = null;

                return; // the shard's art is not installed in this client: no button rather than an empty one
            }

            _sizeIcon.Width = (int)(_sizeIcon.Width * baseScale);
            _sizeIcon.Height = (int)(_sizeIcon.Height * baseScale);
            _sizeIcon.X = (int)(Width - 46 * baseScale - _sizeIcon.Width);
            _sizeIcon.Y = (int)(12 * baseScale);

            _sizeHit = new HitBox(_sizeIcon.X, _sizeIcon.Y, _sizeIcon.Width, _sizeIcon.Height);
            _sizeHit.MouseUp += SizeHitOnMouseUp;
            _sizeHit.SetTooltip(IsBigPack ? "Make this pack smaller" : "Make this pack larger");

            Add(_sizeHit);
            Add(_sizeIcon);
        }

        private void SizeHitOnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtonType.Left || Client.Game.UO.GameCursor.ItemHold.Enabled)
            {
                return;
            }

            Point offset = Mouse.LDragOffset;

            if (
                Math.Abs(offset.X) >= Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS
                || Math.Abs(offset.Y) >= Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS
            )
            {
                return;
            }

            ProfileManager.CurrentProfile.BigBackpack = !ProfileManager.CurrentProfile.BigBackpack;
            ProfileManager.CurrentProfile.Save(World, ProfileManager.ProfilePath);

            UpdateContents(); // rebuilds the window and puts every item at its position times the new scale
            SetInScreen();
        }

        private void ItemsOnAdded()
        {
            Entity container = World.Get(LocalSerial);

            if (container == null)
            {
                return;
            }

            bool is_corpse = container.Graphic == 0x2006;

            if (!container.IsEmpty && _hideIfEmpty && !IsVisible)
            {
                IsVisible = true;
            }

            for (LinkedObject i = container.Items; i != null; i = i.Next)
            {
                Item item = (Item)i;

                // NOTE: Switched from 'item.Layer' property which comes from server to 'ItemData.Layer' from tiledata.mul.
                //       In the past I found some issues using the server property.
                //       Probably lack of knowledge about some client behaviour.
                //       Remember it.

                if (item.Amount <= 0)
                {
                    continue;
                }

                var layer = (Layer)item.ItemData.Layer;

                if (is_corpse && item.Layer > 0 && !Constants.BAD_CONTAINER_LAYERS[(int)layer])
                {
                    continue;
                }

                // some items has layer = [face | beard | hair] and we need to check if it's a wearable item or not.
                // when the item is wearable we dont add it to the container.
                // Tested with --> client = 7.0.95.0 | graphic = 0x0A02
                if (
                    item.ItemData.IsWearable
                    && (layer == Layer.Face || layer == Layer.Beard || layer == Layer.Hair)
                )
                {
                    continue;
                }

                ItemGump itemControl = new ItemGump(
                    this,
                    item.Serial,
                    (ushort)(
                        item.DisplayedGraphic
                        - ((IsChessboard || IsBackgammonBoard) ? Constants.ITEM_GUMP_TEXTURE_OFFSET : 0)
                    ),
                    item.Hue,
                    item.X,
                    item.Y,
                    (IsChessboard || IsBackgammonBoard)
                );

                itemControl.IsVisible = !IsMinimized;

                float scale = GetScale();

                if (
                    ProfileManager.CurrentProfile != null
                    && ProfileManager.CurrentProfile.ScaleItemsInsideContainers
                )
                {
                    itemControl.Width = (int)(itemControl.Width * scale);
                    itemControl.Height = (int)(itemControl.Height * scale);
                }

                itemControl.X = (int)((short)item.X * scale);
                itemControl.Y = (int)(((short)item.Y - (IsChessboard ? 20 : 0)) * scale);

                Add(itemControl);
            }
        }

        public void CheckItemControlPosition(Item item)
        {
            Rectangle dataBounds = _data.Bounds;

            int boundX = dataBounds.X;
            int boundY = dataBounds.Y;
            int boundWidth = dataBounds.Width;
            int boundHeight = dataBounds.Height + (IsChessboard ? 20 : 0);

            ref readonly var spriteInfo = ref (
                (IsChessboard || IsBackgammonBoard)
                    ? ref Client.Game.UO.Gumps.GetGump(
                        (ushort)(
                            item.DisplayedGraphic
                            - Constants.ITEM_GUMP_TEXTURE_OFFSET
                        )
                    )
                    : ref Client.Game.UO.Arts.GetArt(item.DisplayedGraphic)
            );

            if (spriteInfo.Texture != null)
            {
                float scale = GetScale();

                boundWidth -= (int)(spriteInfo.UV.Width / scale);
                boundHeight -= (int)(spriteInfo.UV.Height / scale);
            }

            if (item.X < boundX)
            {
                item.X = (ushort)boundX;
            }
            else if (item.X > boundWidth)
            {
                item.X = (ushort)boundWidth;
            }

            if (item.Y < boundY)
            {
                item.Y = (ushort)boundY;
            }
            else if (item.Y > boundHeight)
            {
                item.Y = (ushort)boundHeight;
            }
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            float layerDepth = layerDepthRef;

            if (CUOEnviroment.Debug && !IsMinimized)
            {
                Rectangle bounds = _data.Bounds;
                float scale = GetScale();
                ushort boundX = (ushort)(bounds.X * scale);
                ushort boundY = (ushort)(bounds.Y * scale);
                ushort boundWidth = (ushort)(bounds.Width * scale);
                ushort boundHeight = (ushort)(bounds.Height * scale);

                Vector3 hueVector = ShaderHueTranslator.GetHueVector(0);

                renderLists.AddGumpNoAtlas(
                    batcher =>
                    {
                        batcher.DrawRectangle(
                            SolidColorTextureCache.GetTexture(Color.Red),
                            x + boundX,
                            y + boundY,
                            boundWidth - boundX,
                            boundHeight - boundY,
                            hueVector,
                            layerDepth
                        );
                        return true;
                    }
                );
            }

            return true;
        }

        public override void Dispose()
        {
            Item item = World.Items.Get(LocalSerial);
            if (_grid)
            {
                GridDispose();
            }


            if (item != null)
            {
                if (
                    World.Player != null
                    && ProfileManager.CurrentProfile?.OverrideContainerLocationSetting == 3
                )
                {
                    UIManager.SavePosition(item, Location);
                }

                for (LinkedObject i = item.Items; i != null; i = i.Next)
                {
                    Item child = (Item)i;

                    if (child.Container == item)
                    {
                        UIManager.GetGump<ContainerGump>(child)?.Dispose();
                    }
                }
            }

            base.Dispose();
        }

        protected override void CloseWithRightClick()
        {
            base.CloseWithRightClick();

            if (_data.ClosedSound != 0)
            {
                Client.Game.Audio.PlaySound(_data.ClosedSound);
            }
        }

        protected override void OnDragEnd(int x, int y)
        {
            if (
                ProfileManager.CurrentProfile.OverrideContainerLocation
                && ProfileManager.CurrentProfile.OverrideContainerLocationSetting >= 2
            )
            {
                Point gumpCenter = new Point(X + (Width >> 1), Y + (Height >> 1));
                ProfileManager.CurrentProfile.OverrideContainerLocationPosition = gumpCenter;
            }

            base.OnDragEnd(x, y);
        }

        private class GumpPicContainer : GumpPic
        {
            public const ushort BackpackGraphic = 0x003C;

            public GumpPicContainer(int x, int y, ushort graphic, ushort hue)
                : base(x, y, graphic, hue) { }

            /// <summary>The window's total scale (the container scale times the big-pack factor).</summary>
            public float Scale { get; set; } = 1f;

            /// <summary>The container scale alone: the parts of the nine-slice that keep their size are drawn at this scale.</summary>
            public float BaseScale { get; set; } = 1f;

            public bool NineSlice { get; set; }

            private bool Slicing => NineSlice && Graphic == BackpackGraphic;

            public override bool Contains(int x, int y)
            {
                if (Slicing)
                {
                    return x >= 0 && y >= 0 && x < Width && y < Height;
                }

                float scale = Graphic == 0x091A || Graphic == 0x092E ? 1f : Scale;

                x = (int)(x / scale);
                y = (int)(y / scale);

                return base.Contains(x, y);
            }

            // The backpack art is 229 x 210: a flap on top with a loop, a lip with the buckle at the bottom, strap handles on each side and a seam down the middle. Only the leather
            // between those is stretched. Columns (fractions of the width): left edge, stretch, middle (loop, seam, buckle), stretch, right edge. Rows in the middle columns: flap,
            // stretch, lip. Rows in the edge columns: flap and handles, stretch, lip. Measured from the art in the real client.
            private static readonly float[] ColumnEdges = { 0f, 0.175f, 0.41f, 0.59f, 0.825f, 1f };
            private static readonly bool[] ColumnStretches = { false, true, false, true, false };
            private static readonly float[] MiddleRowEdges = { 0f, 0.262f, 0.762f, 1f };
            private static readonly float[] EdgeRowEdges = { 0f, 0.595f, 0.762f, 1f };
            private static readonly bool[] RowStretches = { false, true, false };

            /// <summary>Where each piece starts in a span of <paramref name="total"/> pixels: the fixed pieces keep their size (times <paramref name="fixedScale"/>) and the stretching pieces share what is left in proportion to their original size.</summary>
            private static int[] Layout(int source, float[] edges, bool[] stretches, float fixedScale, int total)
            {
                float fixedSize = 0f;
                float stretchSource = 0f;

                for (int i = 0; i < stretches.Length; i++)
                {
                    float size = (edges[i + 1] - edges[i]) * source;

                    if (stretches[i])
                    {
                        stretchSource += size;
                    }
                    else
                    {
                        fixedSize += size * fixedScale;
                    }
                }

                float stretchTotal = Math.Max(0f, total - fixedSize);
                var result = new int[stretches.Length + 1];
                float position = 0f;

                for (int i = 0; i < stretches.Length; i++)
                {
                    float size = (edges[i + 1] - edges[i]) * source;
                    position += stretches[i] ? size / stretchSource * stretchTotal : size * fixedScale;
                    result[i + 1] = (int)Math.Round(position);
                }

                result[stretches.Length] = total;

                return result;
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                if (!Slicing || IsDisposed)
                {
                    return base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
                }

                float layerDepth = layerDepthRef;
                Vector3 hueVector = ShaderHueTranslator.GetHueVector(Hue, IsPartialHue, Alpha, true);
                ref readonly var gumpInfo = ref Client.Game.UO.Gumps.GetGump(Graphic);

                if (gumpInfo.Texture != null)
                {
                    var texture = gumpInfo.Texture;
                    Rectangle source = gumpInfo.UV;
                    int width = Width;
                    int height = Height;
                    float baseScale = BaseScale;

                    renderLists.AddGumpWithAtlas(
                        batcher =>
                        {
                            int[] sourceX = new int[ColumnEdges.Length];

                            for (int i = 0; i < ColumnEdges.Length; i++)
                            {
                                sourceX[i] = (int)(ColumnEdges[i] * source.Width);
                            }

                            sourceX[ColumnEdges.Length - 1] = source.Width;

                            int[] destinationX = Layout(source.Width, ColumnEdges, ColumnStretches, baseScale, width);

                            for (int column = 0; column < ColumnStretches.Length; column++)
                            {
                                bool edge = column == 0 || column == ColumnStretches.Length - 1;
                                float[] rows = edge ? EdgeRowEdges : MiddleRowEdges;
                                int[] sourceY = new int[rows.Length];

                                for (int i = 0; i < rows.Length; i++)
                                {
                                    sourceY[i] = (int)(rows[i] * source.Height);
                                }

                                sourceY[rows.Length - 1] = source.Height;

                                int[] destinationY = Layout(source.Height, rows, RowStretches, baseScale, height);

                                for (int row = 0; row < RowStretches.Length; row++)
                                {
                                    int destinationWidth = destinationX[column + 1] - destinationX[column];
                                    int destinationHeight = destinationY[row + 1] - destinationY[row];

                                    if (destinationWidth <= 0 || destinationHeight <= 0)
                                    {
                                        continue;
                                    }

                                    batcher.Draw(
                                        texture,
                                        new Rectangle(x + destinationX[column], y + destinationY[row], destinationWidth, destinationHeight),
                                        new Rectangle(
                                            source.X + sourceX[column],
                                            source.Y + sourceY[row],
                                            sourceX[column + 1] - sourceX[column],
                                            sourceY[row + 1] - sourceY[row]
                                        ),
                                        hueVector,
                                        layerDepth
                                    );
                                }
                            }

                            return true;
                        }
                    );
                }

                return true;
            }
        }
    }
}
