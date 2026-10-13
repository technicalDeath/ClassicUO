// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Where is my body? When the player dies the client remembers where (the profile keeps it, so a relog does not lose it) and shows a small strip with the direction and distance to
    /// the body, updated as the player walks, as a ghost and after coming back, until the body is reached and opened (or has decayed, or the player right-clicks the strip). It uses only
    /// what the client already knows: the player's own position at death and the corpses the client can see. A new player who dies in the wilderness otherwise has to guess.
    /// </summary>
    internal sealed class BodyFinderGump : Gump
    {
        private const int Pad = 6;
        private const int NearTiles = 2;   // a body opens from two tiles away
        private const int SeeTiles = 10;          // inside this distance a corpse must be visible to the client if it exists
        private const int OpenedClearMilliseconds = 1500;
        private const int DecayedAfterMilliseconds = 6000;

        private readonly AlphaBlendControl _background;
        private readonly Label _label;
        private bool _wasDead;
        private bool _started;
        private long _nextCheck;
        private long _missingSince;
        private long _openedSince;
        private uint _corpseSerial;
        private bool _placed;
        private string _shown = string.Empty;

        public BodyFinderGump(World world) : base(world, 0, 0)
        {
            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;
            IsVisible = false;

            _background = new AlphaBlendControl(0.7f);
            Add(_background);
            _label = new Label(string.Empty, true, 0x0035, 0, 1, FontStyle.BlackBorder) { AcceptMouseInput = false, X = Pad, Y = Pad - 1 };
            Add(_label);
            SetTooltip("Where your body lies. Right-click to hide this.");
        }

        public static string Direction(int dx, int dy)
        {
            // y grows to the south; a body is called "here" when both are 0
            if (dx == 0 && dy == 0)
            {
                return "here";
            }

            double angle = Math.Atan2(-dy, dx) * 180.0 / Math.PI;   // 0 = east, 90 = north
            string[] names = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
            int index = (int)Math.Round(angle / 45.0);

            return names[((index % 8) + 8) % 8];
        }

        public static string Describe(int dx, int dy, bool dead)
        {
            int tiles = Math.Max(Math.Abs(dx), Math.Abs(dy));

            if (tiles <= NearTiles)
            {
                return dead ? "Your body is right here" : "Your body is here: double-click it to open it";
            }

            return $"Your body: {tiles} tiles {Direction(dx, dy)}";
        }

        public override void Update()
        {
            base.Update();

            PlayerMobile player = World.Player;
            Profile profile = ProfileManager.CurrentProfile;

            if (IsDisposed || player == null || profile == null || !profile.BodyFinderEnabled)
            {
                IsVisible = false;

                return;
            }

            bool dead = player.IsDead;

            if (!_started)
            {
                // the first look after logging in: a character who is already dead keeps the body marker the profile has; it died earlier, and the ghost may have wandered
                _started = true;
                _wasDead = dead;
            }

            if (dead && !_wasDead)
            {
                // died just now: this is where the body is
                profile.BodyMarkerX = player.X;
                profile.BodyMarkerY = player.Y;
                profile.BodyMarkerMap = World.MapIndex;
                _corpseSerial = 0;
                _missingSince = 0;
                _openedSince = 0;
                profile.Save(World, ProfileManager.ProfilePath);
            }

            _wasDead = dead;

            if (profile.BodyMarkerX < 0 || profile.BodyMarkerMap != World.MapIndex)
            {
                IsVisible = false;

                return;
            }

            int dx = profile.BodyMarkerX - player.X;
            int dy = profile.BodyMarkerY - player.Y;
            int tiles = Math.Max(Math.Abs(dx), Math.Abs(dy));

            if (Time.Ticks >= _nextCheck)
            {
                _nextCheck = Time.Ticks + 500;
                CheckCorpse(profile, tiles, dead);
            }

            if (profile.BodyMarkerX < 0)
            {
                IsVisible = false;

                return;
            }

            string text = Describe(dx, dy, dead);

            if (dead)
            {
                // a ghost cannot take anything: say how to come back first, then where the body is
                text = "You are dead: walk to a healer in a town," + Environment.NewLine + "or an ankh shrine, to come back." + Environment.NewLine + text;
            }

            if (text != _shown)
            {
                _shown = text;
                _label.Text = text;
                _background.Width = Width = _label.Width + 2 * Pad;
                _background.Height = Height = _label.Height + 2 * Pad - 2;
            }

            if (!_placed)
            {
                Place();
            }

            IsVisible = true;
        }

        /// <summary>Finds the body on the ground near the marker; clears the marker when the player has opened it, or when it should be in sight and is not (it has decayed).</summary>
        private void CheckCorpse(Profile profile, int tiles, bool dead)
        {
            Item corpse = null;

            if (_corpseSerial != 0 && World.Items.TryGetValue(_corpseSerial, out Item known) && !known.IsDestroyed)
            {
                corpse = known;
            }
            else
            {
                _corpseSerial = 0;

                foreach (Item item in World.Items.Values)
                {
                    if (item.IsCorpse && item.OnGround && Math.Abs(item.X - profile.BodyMarkerX) <= 2 && Math.Abs(item.Y - profile.BodyMarkerY) <= 2)
                    {
                        corpse = item;
                        _corpseSerial = item.Serial;

                        break;
                    }
                }
            }

            if (corpse != null)
            {
                _missingSince = 0;

                // the body has been opened by the player after coming back: the job is done
                if (!dead && UIManager.GetGump<ContainerGump>(corpse.Serial) != null)
                {
                    if (_openedSince == 0)
                    {
                        _openedSince = Time.Ticks;
                    }
                    else if (Time.Ticks - _openedSince > OpenedClearMilliseconds)
                    {
                        Clear(profile);
                    }
                }

                return;
            }

            if (tiles <= SeeTiles)
            {
                if (_missingSince == 0)
                {
                    _missingSince = Time.Ticks;
                }
                else if (Time.Ticks - _missingSince > DecayedAfterMilliseconds)
                {
                    Clear(profile);
                }
            }
            else
            {
                _missingSince = 0;
            }
        }

        private void Clear(Profile profile)
        {
            profile.BodyMarkerX = -1;
            profile.BodyMarkerY = -1;
            profile.BodyMarkerMap = -1;
            _corpseSerial = 0;
            _missingSince = 0;
            _openedSince = 0;
            _shown = string.Empty;
            IsVisible = false;
            profile.Save(World, ProfileManager.ProfilePath);
        }

        /// <summary>Under the combat bar's neighbours in the black column when there is room, else along the top of the game window.</summary>
        private void Place()
        {
            GameScene scene = Client.Game.GetScene<GameScene>();

            if (scene == null || Width == 0)
            {
                return;
            }

            Rectangle view = scene.Camera.Bounds;

            if (!ShardLayout.Dock(this, Width, Height))
            {
                X = view.X + (view.Width - Width) / 2;
                Y = view.Y + 32;
            }

            _placed = true;
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button == MouseButtonType.Right && ProfileManager.CurrentProfile is { } profile)
            {
                Clear(profile);

                return;
            }

            base.OnMouseUp(x, y, button);
        }
    }
}
