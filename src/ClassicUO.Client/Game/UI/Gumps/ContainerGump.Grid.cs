// SPDX-License-Identifier: BSD-2-Clause

// The shard's grid view of a container (UO Rekindled): the same ContainerGump, drawn as a resizable window of square cells instead of the classic
// art with items at the positions the server remembers. Everything that already looks up a ContainerGump by serial keeps working, because it is one.
// Cell numbers are the client's own: they live in GridContainers.txt in the profile folder and never go to the server.
// Written for this fork. The idea (cells, locks, sort, search) follows what TazUO's grid containers offer; no TazUO code is copied.

using ClassicUO.Assets;
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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ClassicUO.Game.UI.Gumps
{
    internal partial class ContainerGump
    {
        private const int CellSize = 48;
        private const int CellGap = 3;
        private const int CellPitch = CellSize + CellGap;
        private const int GridMargin = 8;
        private const int HeaderHeight = 78;
        private const int FooterHeight = 22;
        private const int ScrollBarWidth = 14;
        private const int MinColumns = 4;
        private const int MinRows = 3;
        private const int DefaultColumns = 6;
        private const int DefaultRows = 5;

        private bool _grid;
        private int _columns = DefaultColumns;
        private int _rows = DefaultRows;
        private ScrollArea _gridScroll;
        private StbTextBox _search;
        private Label _title;
        private Label _countLabel;
        private GridButton _classicButton;
        private Label _searchHint;
        private GridButton _sortButton;
        private GridButton _autoButton;
        private GridButton _orderButton;
        private GridButton _modeButton;
        private GridGrip _resizeButton;
        private Label _hint;
        private GridChrome _chrome;
        private bool _resizing;
        private Point _resizeStart;
        private string _lastSearch = string.Empty;
        private bool _relayout;
        private long _nextSave;
        private GridContainerState _state;
        private readonly Dictionary<int, uint> _slotItems = new Dictionary<int, uint>();

        /// <summary>True when this window is the shard's grid view.</summary>
        public bool IsGrid => _grid;

        private static readonly string[] OrderNames = { "Type", "Name", "Wear" };

        private static bool WantsGrid(Item item, ushort gumpId)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || profile.ContainerStyle != 1 || item == null)
            {
                return false;
            }

            // the chess and backgammon boards are games, not storage
            if (gumpId == 0x091A || gumpId == 0x092E)
            {
                return false;
            }

            // the old grid-only corpse window still wins when the player chose it
            if (gumpId == CORPSES_GUMP && profile.GridLootType == 1)
            {
                return false;
            }

            return true;
        }

        private bool IsCorpseGrid => Graphic == CORPSES_GUMP || World.Items.Get(LocalSerial)?.IsCorpse == true;

        private void BuildGrid()
        {
            CanMove = true;
            CanCloseWithRightClick = true;
            WantUpdateSize = false;

            Item item = World.Items.Get(LocalSerial);

            if (item == null)
            {
                Dispose();

                return;
            }

            _data = World.ContainerManager.Get(Graphic);
            _state = GridStore.Get(World, LocalSerial, IsCorpseGrid, out bool known);

            if (known)
            {
                _columns = _state.Columns;
                _rows = _state.Rows;
            }
            else
            {
                _columns = GridStore.DefaultColumns(IsCorpseGrid);
                _rows = GridStore.DefaultRows(IsCorpseGrid);
                _state.Columns = _columns;
                _state.Rows = _rows;
            }

            _columns = Math.Max(MinColumns, _columns);
            _rows = Math.Max(MinRows, _rows);

            Width = WindowWidth(_columns);
            Height = WindowHeight(_rows);

            Add(_chrome = new GridChrome(this) { Width = Width, Height = Height });

            Add(_title = new Label(TitleText(item), true, 0x0481, Width - 2 * GridMargin - 140, 1, FontStyle.BlackBorder) { X = GridMargin + 2, Y = 6 });
            Add(_countLabel = new Label(string.Empty, true, 0x0481, 0, 1) { Y = 7 });

            Add(_classicButton = new GridButton("Classic", 52, () => SwitchStyle(0)));
            _classicButton.SetTooltip("Show this container as the classic window. You can switch back with the Grid button on the backpack, or under Options, Containers.", 230);

            _search = new StbTextBox(1, 30, Width - 2 * GridMargin - 8, true, FontStyle.None, 0x0481)
            {
                X = GridMargin + 6,
                Y = 31,
                Width = Width - 2 * GridMargin - 8,
                Height = 16
            };
            _search.TextChanged += (sender, e) => _relayout = true;
            Add(_search);

            Add(_hint = new Label("Ctrl-click an item to lock its cell", true, 0x0481, 0, 1) { AcceptMouseInput = false });

            Add(_searchHint = new Label("Search this container", true, 0x0481, 0, 1) { X = GridMargin + 6, Y = 31, AcceptMouseInput = false });

            Add(_sortButton = new GridButton("Sort", 44, () => SortNow()));
            _sortButton.SetTooltip("Put everything in order now. Items you locked (Ctrl-click an item) stay where they are.", 230);
            Add(_autoButton = new GridButton("Auto", 44, () =>
            {
                _state.Auto = !_state.Auto;
                _state.Dirty = true;
                _relayout = true;
            }));
            _autoButton.SetTooltip("Keep this container sorted every time something changes.", 230);
            Add(_orderButton = new GridButton(OrderNames[0], 52, () =>
            {
                _state.Order = (_state.Order + 1) % OrderNames.Length;
                _state.Dirty = true;
                _relayout = true;
            }));
            _orderButton.SetTooltip("How Sort orders things: by type, by name, or by where it is worn.", 230);
            Add(_modeButton = new GridButton("Mark", 48, () =>
            {
                ProfileManager.CurrentProfile.GridSearchHide = !ProfileManager.CurrentProfile.GridSearchHide;
                _relayout = true;
            }));
            _modeButton.SetTooltip("Search either marks the matches with a gold frame, or hides everything else.", 230);

            BuildScrollArea();
            _relayout = true;
        }

        private static int WindowWidth(int columns) => 2 * GridMargin + columns * CellPitch - CellGap + ScrollBarWidth + 4;

        private static int WindowHeight(int rows) => HeaderHeight + rows * CellPitch - CellGap + FooterHeight;

        private string TitleText(Item item)
        {
            if (!string.IsNullOrEmpty(item.Name))
            {
                return item.Name;
            }

            if (item == World.Player?.FindItemByLayer(Layer.Backpack))
            {
                return "Backpack";
            }

            string name = item.ItemData.Name;

            return string.IsNullOrEmpty(name) ? (IsCorpseGrid ? "a corpse" : "Container") : name;
        }

        private void PositionChrome()
        {
            if (_chrome == null)
            {
                return;
            }

            _chrome.Width = Width;
            _chrome.Height = Height;

            int right = Width - GridMargin;
            _orderButton.X = right - _orderButton.Width - ScrollBarWidth + 6;
            _orderButton.Y = 54;
            _autoButton.X = _orderButton.X - _autoButton.Width - 4;
            _autoButton.Y = 54;
            _sortButton.X = _autoButton.X - _sortButton.Width - 4;
            _sortButton.Y = 54;
            _modeButton.X = GridMargin + 6;
            _modeButton.Y = 54;

            _search.Width = Width - 2 * GridMargin - ScrollBarWidth - 4;
            _classicButton.X = Width - GridMargin - _classicButton.Width - 8;
            _classicButton.Y = 4;
            _countLabel.X = _classicButton.X - _countLabel.Width - 10;
            _resizeButton.X = Width - _resizeButton.Width - 4;
            _resizeButton.Y = Height - _resizeButton.Height - 4;
            _hint.X = GridMargin + 2;
            _hint.Y = Height - FooterHeight + 3;
        }

        private void BuildScrollArea()
        {
            _gridScroll?.Dispose();

            _gridScroll = new ScrollArea(GridMargin, HeaderHeight, _columns * CellPitch - CellGap + ScrollBarWidth + 4, _rows * CellPitch - CellGap, true)
            {
                ScrollbarBehaviour = ScrollbarBehaviour.ShowWhenDataExceedFromView,
                CanMove = true
            };
            Add(_gridScroll);

            _resizeButton?.Dispose();
            _resizeButton = new GridGrip();
            _resizeButton.MouseDown += (sender, e) =>
            {
                _resizing = true;
                _resizeStart = new Point(Width, Height);
            };
            _resizeButton.MouseUp += (sender, e) => _resizing = false;
            Add(_resizeButton);
            PositionChrome();
        }

        /// <summary>Changes how containers are drawn (0 classic, 1 grid) and reopens this one in the new style, in the same place.</summary>
        private void SwitchStyle(int style)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || Client.Game.UO.GameCursor.ItemHold.Enabled)
            {
                return;
            }

            profile.ContainerStyle = style;
            profile.Save(World, ProfileManager.ProfilePath);

            // the open-container packet finds this window by serial and replaces it where it stands
            GameActions.DoubleClick(World, LocalSerial);
        }

        /// <summary>A small Grid button on the classic backpack, beside the size button.</summary>
        private void AddGridSwitch()
        {
            if (_grid || !IsMainPack || IsChessboard || IsBackgammonBoard)
            {
                return;
            }

            float scale = UIManager.ContainerScale;
            GridButton button = new GridButton("Grid", 40, () => SwitchStyle(1));
            button.X = _sizeIcon != null ? _sizeIcon.X - button.Width - 6 : (int)(Width - 90 * scale);
            button.Y = (int)(12 * scale);
            button.SetTooltip("Show your containers as a grid of cells, with search, sort and locks.", 230);
            Add(button);
        }

        // ---- contents -------------------------------------------------------------------------

        private List<Item> GridContents()
        {
            var list = new List<Item>();
            Entity container = World.Get(LocalSerial);

            if (container == null)
            {
                return list;
            }

            bool isCorpse = container.Graphic == 0x2006;

            for (LinkedObject i = container.Items; i != null; i = i.Next)
            {
                Item item = (Item)i;

                if (item.Amount <= 0 || item.IsDestroyed)
                {
                    continue;
                }

                Layer layer = (Layer)item.ItemData.Layer;

                if (isCorpse && item.Layer > 0 && !Constants.BAD_CONTAINER_LAYERS[(int)layer])
                {
                    continue;
                }

                if (item.ItemData.IsWearable && (layer == Layer.Face || layer == Layer.Beard || layer == Layer.Hair))
                {
                    continue;
                }

                list.Add(item);
            }

            return list;
        }

        private static string NameOf(Item item)
        {
            string name = item.Name;

            return string.IsNullOrEmpty(name) ? item.ItemData.Name ?? string.Empty : name;
        }

        private bool Matches(Item item, string needle)
        {
            if (needle.Length == 0)
            {
                return true;
            }

            if (NameOf(item).Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return World.OPL.TryGetNameAndData(item.Serial, out string name, out string data)
                && ((name?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (data?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        private IEnumerable<Item> Sorted(List<Item> items)
        {
            switch (_state.Order)
            {
                case 1:
                    return items.OrderBy(i => NameOf(i), StringComparer.OrdinalIgnoreCase).ThenByDescending(i => i.Amount).ThenBy(i => i.Serial);
                case 2:
                    return items.OrderBy(i => (int)(Layer)i.ItemData.Layer == 0 ? 255 : (int)(Layer)i.ItemData.Layer).ThenBy(i => i.Graphic).ThenBy(i => i.Hue).ThenBy(i => i.Serial);
                default:
                    return items.OrderBy(i => i.Graphic).ThenBy(i => i.Hue).ThenBy(i => i.Serial);
            }
        }

        /// <summary>Gives every item a cell: locked and remembered cells first, the rest fill the first free ones.</summary>
        private Dictionary<uint, int> AssignSlots(List<Item> items, bool resort)
        {
            var result = new Dictionary<uint, int>();
            var taken = new HashSet<int>();

            // a drop the player aimed at a cell wins over what was remembered
            foreach (Item item in items)
            {
                if (_state.Pending.TryGetValue(item.Serial, out int wanted))
                {
                    _state.Pending.Remove(item.Serial);

                    if (wanted >= 0 && !taken.Contains(wanted))
                    {
                        result[item.Serial] = wanted;
                        taken.Add(wanted);
                        SetSlot(item.Serial, wanted);
                    }
                }
            }

            foreach (Item item in items)
            {
                if (result.ContainsKey(item.Serial))
                {
                    continue;
                }

                bool locked = _state.Locked.Contains(item.Serial);

                if ((locked || !resort) && _state.Slots.TryGetValue(item.Serial, out int slot) && slot >= 0 && !taken.Contains(slot))
                {
                    result[item.Serial] = slot;
                    taken.Add(slot);
                }
            }

            IEnumerable<Item> rest = items.Where(i => !result.ContainsKey(i.Serial));
            rest = resort || _state.Auto ? Sorted(rest.ToList()) : rest;

            int next = 0;

            foreach (Item item in rest)
            {
                while (taken.Contains(next))
                {
                    next++;
                }

                result[item.Serial] = next;
                taken.Add(next);
                SetSlot(item.Serial, next);
            }

            foreach (var pair in result)
            {
                SetSlot(pair.Key, pair.Value);
            }

            // forget items that are known to be somewhere else now (an item the client has not heard of yet keeps its cell)
            foreach (uint key in _state.Slots.Keys.ToList())
            {
                Item known = World.Items.Get(key);

                if (known != null && known.Container != LocalSerial)
                {
                    _state.Slots.Remove(key);
                    _state.Locked.Remove(key);
                    _state.Dirty = true;
                }
            }


            return result;
        }

        private void SetSlot(uint serial, int slot)
        {
            if (!_state.Slots.TryGetValue(serial, out int old) || old != slot)
            {
                _state.Slots[serial] = slot;
                _state.Dirty = true;
            }
        }

        private void SortNow()
        {
            RefreshGrid(true);
        }

        private void RefreshGrid(bool resort = false)
        {
            if (!_grid || IsDisposed || _gridScroll == null)
            {
                return;
            }

            List<Item> items = GridContents();
            string needle = _search.Text?.Trim() ?? string.Empty;
            bool hide = ProfileManager.CurrentProfile.GridSearchHide;

            Dictionary<uint, int> slots = AssignSlots(items, resort || _state.Auto);
            Dictionary<uint, Item> bySerial = items.ToDictionary(i => i.Serial);

            _slotItems.Clear();

            // hiding the others closes the gaps, so the matches sit together at the top
            if (needle.Length > 0 && hide)
            {
                int n = 0;

                foreach (Item match in items.Where(i => Matches(i, needle)).OrderBy(i => slots[i.Serial]))
                {
                    _slotItems[n++] = match.Serial;
                }
            }
            else
            {
                foreach (var pair in slots)
                {
                    _slotItems[pair.Value] = pair.Key;
                }
            }

            int maxSlot = _slotItems.Count == 0 ? -1 : _slotItems.Keys.Max();
            int totalRows = Math.Max(_rows, (maxSlot + _columns + 1) / _columns);

            _gridScroll.Clear();

            for (int row = 0; row < totalRows; row++)
            {
                for (int col = 0; col < _columns; col++)
                {
                    int slot = row * _columns + col;
                    int x = col * CellPitch;
                    int y = row * CellPitch;

                    _slotItems.TryGetValue(slot, out uint serial);
                    bySerial.TryGetValue(serial, out Item item);

                    bool match = item != null && needle.Length > 0 && Matches(item, needle);
                    bool dim = item != null && needle.Length > 0 && !match;

                    _gridScroll.Add(new GridSlot(x, y, item != null, item != null && _state.Locked.Contains(item.Serial), match));

                    if (item == null)
                    {
                        continue;
                    }

                    ushort graphic = item.DisplayedGraphic;
                    GridItemGump control = new GridItemGump(this, item, graphic)
                    {
                        Alpha = dim ? 0.3f : 1f
                    };

                    if (control.IsDisposed)
                    {
                        continue;
                    }

                    control.X = x;
                    control.Y = y;
                    control.MouseUp += OnGridItemMouseUp;
                    _gridScroll.Add(control);

                    if (item.ItemData.IsStackable && item.Amount > 1)
                    {
                        string text = item.Amount >= 100000 ? (item.Amount / 1000) + "k" : item.Amount.ToString(CultureInfo.InvariantCulture);
                        Label count = new Label(text, true, dim ? (ushort)0x0481 : (ushort)0x0481, 0, 1, FontStyle.BlackBorder) { AcceptMouseInput = false };
                        count.X = x + CellSize - count.Width - 2;
                        count.Y = y + CellSize - count.Height - 1;
                        _gridScroll.Add(count);
                    }
                }
            }

            int shown = items.Count;
            _countLabel.Text = shown == 1 ? "1 item" : shown + " items";
            Item container = World.Items.Get(LocalSerial);

            if (container != null)
            {
                _title.Text = TitleText(container);
            }

            _autoButton.Active = _state.Auto;
            _orderButton.Text = OrderNames[_state.Order];
            _modeButton.Text = ProfileManager.CurrentProfile.GridSearchHide ? "Hide" : "Mark";
            PositionChrome();

            if (items.Count > 0 && _hideIfEmpty && !IsVisible)
            {
                IsVisible = true;
            }
        }

        private void OnGridItemMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtonType.Left || !Keyboard.Ctrl || sender is not ItemGump control)
            {
                return;
            }

            Point offset = Mouse.LDragOffset;

            if (Math.Abs(offset.X) >= Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS || Math.Abs(offset.Y) >= Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS)
            {
                return;
            }

            uint serial = control.LocalSerial;

            if (!_state.Locked.Remove(serial))
            {
                _state.Locked.Add(serial);
            }

            _state.Dirty = true;
            Mouse.CancelDoubleClick = true;
            World.DelayedObjectClickManager.Clear();
            _relayout = true;
        }

        /// <summary>The cell under a point of this window, or -1 when it is not over the grid.</summary>
        private int SlotAt(int x, int y)
        {
            if (!_grid || _gridScroll == null)
            {
                return -1;
            }

            int gx = x - _gridScroll.X;
            int gy = y - _gridScroll.Y + _gridScroll.ScrollValue;

            if (gx < 0 || gy < 0 || gx >= _columns * CellPitch)
            {
                return -1;
            }

            int col = gx / CellPitch;
            int row = gy / CellPitch;

            return row * _columns + col;
        }

        private void GridNoteDrop(uint item, int slot)
        {
            if (!_grid || slot < 0)
            {
                return;
            }

            // an item that is already here and is dropped on a cell holding another item swaps with it
            if (_state.Slots.TryGetValue(item, out int old) && _slotItems.TryGetValue(slot, out uint other) && other != item && !_state.Locked.Contains(other))
            {
                _state.Pending[other] = old;
                _state.Slots[other] = old;
            }

            _state.Pending[item] = slot;
            _state.Dirty = true;
        }

        // ---- per frame ------------------------------------------------------------------------

        private void GridUpdate()
        {
            if (_state == null || _search == null)
            {
                return;
            }

            if (_resizing)
            {
                if (!Mouse.LButtonPressed)
                {
                    _resizing = false;
                    SetInScreen();
                }
                else
                {
                    Point drag = Mouse.LDragOffset;
                    int columns = Math.Max(MinColumns, (_resizeStart.X + drag.X - 2 * GridMargin - ScrollBarWidth - 4 + CellGap + CellPitch / 2) / CellPitch);
                    int rows = Math.Max(MinRows, (_resizeStart.Y + drag.Y - HeaderHeight - FooterHeight + CellGap + CellPitch / 2) / CellPitch);

                    if (columns != _columns || rows != _rows)
                    {
                        _columns = columns;
                        _rows = rows;
                        _state.Columns = columns;
                        _state.Rows = rows;
                        _state.Dirty = true;
                        GridStore.RememberSize(IsCorpseGrid, columns, rows);
                        Width = WindowWidth(columns);
                        Height = WindowHeight(rows);
                        BuildScrollArea();
                        _search.Width = Width - 2 * GridMargin - ScrollBarWidth - 4;
                        _relayout = true;
                    }
                }
            }

            string text = _search.Text ?? string.Empty;

            if (text != _lastSearch)
            {
                _lastSearch = text;
                _relayout = true;
            }

            if (_state.Dirty && (long)Time.Ticks >= _nextSave)
            {
                _nextSave = (long)Time.Ticks + 2000;

                if (!IsCorpseGrid)
                {
                    GridStore.Save();
                    _state.Dirty = false;
                }
            }

            _searchHint.IsVisible = text.Length == 0 && !_search.HasKeyboardFocus;

            if (_relayout)
            {
                _relayout = false;
                RefreshGrid();
            }
        }

        /// <summary>A corpse grid opens where the last one was closed. Returns true when it was placed.</summary>
        public bool TryRestoreGridPlacement()
        {
            if (!_grid || !IsCorpseGrid || GridStore.CorpsePosition.X < 0)
            {
                return false;
            }

            X = GridStore.CorpsePosition.X;
            Y = GridStore.CorpsePosition.Y;
            SetInScreen();

            return true;
        }

        private void GridDispose()
        {
            if (_state != null)
            {
                GridStore.Release(LocalSerial, IsCorpseGrid, _state, X, Y);
            }
        }

        // ---- controls -------------------------------------------------------------------------

        private static readonly Color Brass = new Color(150, 124, 72);
        private static readonly Color BrassDark = new Color(70, 58, 34);
        private static readonly Color Panel = new Color(24, 20, 16);
        private static readonly Color PanelHeader = new Color(44, 36, 26);
        private static readonly Color Gold = new Color(255, 214, 86);

        private static void Rect(UltimaBatcher2D batcher, int x, int y, int w, int h, Color color, float depth, float alpha = 1f)
        {
            Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, alpha);
            batcher.Draw(SolidColorTextureCache.GetTexture(color), new Rectangle(x, y, w, h), hue, depth);
        }

        private static void Frame(UltimaBatcher2D batcher, int x, int y, int w, int h, Color color, float depth, float alpha = 1f)
        {
            Rect(batcher, x, y, w, 1, color, depth, alpha);
            Rect(batcher, x, y + h - 1, w, 1, color, depth, alpha);
            Rect(batcher, x, y, 1, h, color, depth, alpha);
            Rect(batcher, x + w - 1, y, 1, h, color, depth, alpha);
        }

        /// <summary>The window's dark panel, brass frame and header band.</summary>
        private sealed class GridChrome : Control
        {
            public GridChrome(ContainerGump gump)
            {
                CanMove = true;
                AcceptMouseInput = true;
                WantUpdateSize = false;
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float depth = layerDepthRef;
                int w = Width;
                int h = Height;

                renderLists.AddGumpWithAtlas(batcher =>
                {
                    Rect(batcher, x, y, w, h, Panel, depth, 0.92f);
                    Rect(batcher, x + 1, y + 1, w - 2, HeaderHeight - GridMargin - 1, PanelHeader, depth, 0.95f);
                    Rect(batcher, x + 1, y + HeaderHeight - GridMargin, w - 2, 1, BrassDark, depth);
                    Rect(batcher, x + 1, y + h - FooterHeight, w - 2, 1, BrassDark, depth);
                    Rect(batcher, x + 1, y + h - FooterHeight + 1, w - 2, FooterHeight - 2, PanelHeader, depth, 0.8f);
                    Rect(batcher, x + GridMargin + 3, y + 28, w - 2 * GridMargin - ScrollBarWidth - 2, 21, new Color(14, 11, 8), depth);
                    Frame(batcher, x + GridMargin + 3, y + 28, w - 2 * GridMargin - ScrollBarWidth - 2, 21, BrassDark, depth);
                    Frame(batcher, x, y, w, h, Brass, depth);
                    Frame(batcher, x + 2, y + 2, w - 4, h - 4, BrassDark, depth, 0.7f);

                    return true;
                });

                return true;
            }
        }

        /// <summary>One cell: a dark square, a gold frame when it matches the search, a gold corner when its item is locked.</summary>
        private sealed class GridSlot : Control
        {
            private readonly bool _filled;
            private readonly bool _locked;
            private readonly bool _match;

            public GridSlot(int x, int y, bool filled, bool locked, bool match)
            {
                X = x;
                Y = y;
                Width = CellSize;
                Height = CellSize;
                _filled = filled;
                _locked = locked;
                _match = match;
                AcceptMouseInput = false;
                WantUpdateSize = false;
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float depth = layerDepthRef;

                renderLists.AddGumpWithAtlas(batcher =>
                {
                    Rect(batcher, x, y, CellSize, CellSize, _filled ? new Color(58, 48, 36) : new Color(34, 28, 22), depth, 0.95f);

                    return true;
                });

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    // the frame and the padlock go over the item art
                    Frame(batcher, x, y, CellSize, CellSize, _match ? Gold : _locked ? new Color(190, 150, 60) : (_filled ? new Color(96, 80, 54) : new Color(56, 46, 34)), depth);

                    if (_match)
                    {
                        Frame(batcher, x + 1, y + 1, CellSize - 2, CellSize - 2, Gold, depth);
                    }

                    if (_locked)
                    {
                        // a gold padlock on a dark tag: shackle and body
                        Rect(batcher, x + 2, y + 2, 14, 14, new Color(20, 16, 10), depth, 0.85f);
                        Rect(batcher, x + 6, y + 3, 6, 1, Gold, depth);
                        Rect(batcher, x + 5, y + 4, 1, 4, Gold, depth);
                        Rect(batcher, x + 12, y + 4, 1, 4, Gold, depth);
                        Rect(batcher, x + 4, y + 8, 10, 7, Gold, depth);
                        Rect(batcher, x + 8, y + 10, 2, 3, new Color(20, 16, 10), depth);
                    }

                    return true;
                });

                return true;
            }
        }

        /// <summary>An item in a cell: the whole cell is the target (not just the opaque pixels of a small bottle), and the art is drawn centred in it.</summary>
        private sealed class GridItemGump : ItemGump
        {
            private readonly World _world;

            public GridItemGump(ContainerGump gump, Item item, ushort graphic)
                : base(gump, item.Serial, graphic, item.Hue, 0, 0)
            {
                _world = gump.World;
                Width = CellSize;
                Height = CellSize;
            }

            public override bool Contains(int x, int y) => x >= 0 && y >= 0 && x < CellSize && y < CellSize;

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                if (IsDisposed)
                {
                    return false;
                }

                float depth = layerDepthRef;
                Item item = _world.Items.Get(LocalSerial);

                if (item == null)
                {
                    return false;
                }

                ref readonly var info = ref Client.Game.UO.Arts.GetArt(item.DisplayedGraphic);
                var texture = info.Texture;

                if (texture == null)
                {
                    return false;
                }

                Rectangle uv = info.UV;
                int w = uv.Width;
                int h = uv.Height;

                if (w > CellSize - 4 || h > CellSize - 4)
                {
                    float scale = Math.Min((CellSize - 4f) / w, (CellSize - 4f) / h);
                    w = Math.Max(1, (int)(w * scale));
                    h = Math.Max(1, (int)(h * scale));
                }

                bool hover = MouseIsOver || LocalSerial == SelectedObject.SelectedContainer?.Serial;
                ushort hue = hover ? (ushort)0x0035 : item.Hue;
                bool partial = !hover && item.ItemData.IsPartialHue;
                Vector3 hueVector = ShaderHueTranslator.GetHueVector(hue, partial, Alpha);
                bool stack = item.Amount > 1 && item.ItemData.IsStackable && !item.IsCoin;
                Rectangle rect = new Rectangle(x + (CellSize - w) / 2, y + (CellSize - h) / 2, w, h);

                renderLists.AddGumpWithAtlas(batcher =>
                {
                    batcher.Draw(texture, rect, uv, hueVector, depth);

                    if (stack)
                    {
                        batcher.Draw(texture, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height), uv, hueVector, depth);
                    }

                    return true;
                });

                return true;
            }
        }

        /// <summary>The resize grip in the bottom-right corner: three short diagonal strokes.</summary>
        private sealed class GridGrip : Control
        {
            public GridGrip()
            {
                Width = 16;
                Height = 16;
                AcceptMouseInput = true;
                CanMove = false;
                WantUpdateSize = false;
                SetTooltip("Drag to resize");
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float depth = layerDepthRef;
                bool over = MouseIsOver;

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    Color color = over ? Gold : Brass;

                    foreach (int sum in new[] { 13, 19, 25 })
                    {
                        for (int a = 0; a < 16; a++)
                        {
                            int b = sum - a;

                            if (b >= 0 && b < 16)
                            {
                                Rect(batcher, x + a, y + b, 1, 1, color, depth);
                            }
                        }
                    }

                    return true;
                });

                return true;
            }
        }

        /// <summary>A small flat button with a caption; can show an on state.</summary>
        private sealed class GridButton : Control
        {
            private readonly Label _label;
            private readonly Action _click;

            public GridButton(string text, int width, Action click)
            {
                _click = click;
                Width = width;
                Height = 18;
                AcceptMouseInput = true;
                CanMove = false;
                WantUpdateSize = false;

                _label = new Label(text, true, 0x0481, 0, 1) { AcceptMouseInput = false };
                Add(_label);
                Center();
            }

            public bool Active { get; set; }

            public string Text
            {
                get => _label.Text;
                set
                {
                    if (_label.Text != value)
                    {
                        _label.Text = value;
                        Center();
                    }
                }
            }

            private void Center()
            {
                _label.X = (Width - _label.Width) / 2;
                _label.Y = (Height - _label.Height) / 2;
            }

            protected override void OnMouseUp(int x, int y, MouseButtonType button)
            {
                if (button == MouseButtonType.Left && !Client.Game.UO.GameCursor.ItemHold.Enabled)
                {
                    _click?.Invoke();

                    return;
                }

                base.OnMouseUp(x, y, button);
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float depth = layerDepthRef;
                bool over = MouseIsOver;
                bool active = Active;

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    Rect(batcher, x, y, Width, Height, active ? new Color(92, 74, 36) : over ? new Color(70, 58, 40) : new Color(46, 38, 28), depth);
                    Frame(batcher, x, y, Width, Height, active || over ? Gold : Brass, depth);

                    return true;
                });

                base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);

                return true;
            }
        }
    }

    /// <summary>What a grid window remembers about one container: its size, its cells, its locks and its sorting.</summary>
    internal sealed class GridContainerState
    {
        public int Columns;
        public int Rows;
        public bool Auto;
        public int Order;
        public bool Dirty;
        public readonly Dictionary<uint, int> Slots = new Dictionary<uint, int>();
        public readonly HashSet<uint> Locked = new HashSet<uint>();
        public readonly Dictionary<uint, int> Pending = new Dictionary<uint, int>();
    }

    /// <summary>The profile's GridContainers.txt: one line per container and one per remembered cell. A corpse keeps only its own size and place.</summary>
    internal static class GridStore
    {
        private static readonly Dictionary<uint, GridContainerState> States = new Dictionary<uint, GridContainerState>();
        private static string _loadedFrom;
        private static World _world;
        private static int _packColumns = 6, _packRows = 5, _corpseColumns = 5, _corpseRows = 4;

        public static Point CorpsePosition { get; private set; } = new Point(-1, -1);

        private static string FilePath => string.IsNullOrEmpty(ProfileManager.ProfilePath) ? null : Path.Combine(ProfileManager.ProfilePath, "GridContainers.txt");

        public static int DefaultColumns(bool corpse) => corpse ? _corpseColumns : _packColumns;

        public static int DefaultRows(bool corpse) => corpse ? _corpseRows : _packRows;

        public static void RememberSize(bool corpse, int columns, int rows)
        {
            if (corpse)
            {
                _corpseColumns = columns;
                _corpseRows = rows;
            }
            else
            {
                _packColumns = columns;
                _packRows = rows;
            }
        }

        private static void EnsureLoaded()
        {
            string path = FilePath;

            if (path == null || _loadedFrom == path)
            {
                return;
            }

            _loadedFrom = path;
            States.Clear();

            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (p.Length == 0)
                    {
                        continue;
                    }

                    switch (p[0])
                    {
                        case "D" when p.Length >= 5:
                            _packColumns = Math.Max(MinCols, ParseInt(p[1], 6));
                            _packRows = Math.Max(MinRowsConst, ParseInt(p[2], 5));
                            _corpseColumns = Math.Max(MinCols, ParseInt(p[3], 5));
                            _corpseRows = Math.Max(MinRowsConst, ParseInt(p[4], 4));
                            break;
                        case "P" when p.Length >= 3:
                            CorpsePosition = new Point(ParseInt(p[1], -1), ParseInt(p[2], -1));
                            break;
                        case "C" when p.Length >= 6:
                        {
                            uint serial = ParseSerial(p[1]);
                            GridContainerState state = Fresh(serial);
                            state.Order = Math.Clamp(ParseInt(p[2], 0), 0, 2);
                            state.Auto = p[3] == "1";
                            state.Columns = Math.Max(MinCols, ParseInt(p[4], 6));
                            state.Rows = Math.Max(MinRowsConst, ParseInt(p[5], 5));
                            break;
                        }
                        case "S" when p.Length >= 5:
                        {
                            GridContainerState state = Fresh(ParseSerial(p[1]));
                            uint item = ParseSerial(p[2]);
                            state.Slots[item] = ParseInt(p[3], 0);

                            if (p[4] == "1")
                            {
                                state.Locked.Add(item);
                            }

                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // a damaged file only costs the remembered cells
            }
        }

        private const int MinCols = 4;
        private const int MinRowsConst = 3;

        private static GridContainerState Fresh(uint serial)
        {
            if (!States.TryGetValue(serial, out GridContainerState state))
            {
                States[serial] = state = new GridContainerState();
            }

            return state;
        }

        private static int ParseInt(string text, int fallback) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        private static uint ParseSerial(string text) => uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v) ? v : 0;

        public static GridContainerState Get(World world, uint serial, bool corpse, out bool known)
        {
            _world = world;
            EnsureLoaded();

            known = States.TryGetValue(serial, out GridContainerState state) && state.Columns > 0;

            if (state == null)
            {
                States[serial] = state = new GridContainerState { Columns = corpse ? _corpseColumns : _packColumns, Rows = corpse ? _corpseRows : _packRows };
                known = true;
            }

            return state;
        }

        public static void Release(uint serial, bool corpse, GridContainerState state, int x, int y)
        {
            if (corpse)
            {
                CorpsePosition = new Point(x, y);
                // a corpse is gone for good once it decays; keeping its cells would only grow the file
                States.Remove(serial);
                Save();

                return;
            }

            if (state.Dirty)
            {
                Save();
                state.Dirty = false;
            }
        }

        public static void Save()
        {
            string path = FilePath;

            if (path == null)
            {
                return;
            }

            try
            {
                var lines = new List<string>
                {
                    string.Format(CultureInfo.InvariantCulture, "D {0} {1} {2} {3}", _packColumns, _packRows, _corpseColumns, _corpseRows),
                    string.Format(CultureInfo.InvariantCulture, "P {0} {1}", CorpsePosition.X, CorpsePosition.Y)
                };

                World world = _world;

                foreach (var pair in States.OrderBy(s => s.Key))
                {
                    Item container = world?.Items.Get(pair.Key);

                    // a corpse or a bag that is no longer known is not kept; locks and cells of a pack that is out of view stay
                    if (container != null && container.IsCorpse)
                    {
                        continue;
                    }

                    GridContainerState s = pair.Value;
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "C {0:X} {1} {2} {3} {4}", pair.Key, s.Order, s.Auto ? 1 : 0, s.Columns, s.Rows));

                    foreach (var slot in s.Slots.OrderBy(x => x.Value))
                    {
                        lines.Add(string.Format(CultureInfo.InvariantCulture, "S {0:X} {1:X} {2} {3}", pair.Key, slot.Key, slot.Value, s.Locked.Contains(slot.Key) ? 1 : 0));
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, lines);
            }
            catch (Exception)
            {
                // not being able to remember the layout is not worth an error window
            }
        }

        /// <summary>Called when the window is closed so a changed layout reaches the disk.</summary>
        public static void Flush() => Save();
    }
}
