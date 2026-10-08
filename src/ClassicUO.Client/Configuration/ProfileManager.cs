// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Configuration
{
    internal static class ProfileManager
    {
        public static GlobalProfile GlobalProfile { get; private set; }
        public static Profile CurrentProfile { get; private set; }
        public static string ProfilePath { get; private set; }

        private static string _rootPath;
        private static string RootPath
        {
            get
            {
                if (string.IsNullOrEmpty(_rootPath))
                {
                    if (string.IsNullOrWhiteSpace(Settings.GlobalSettings.ProfilesPath))
                    {
                        _rootPath = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Profiles");
                    }
                    else
                    {
                        _rootPath = Settings.GlobalSettings.ProfilesPath;
                    }
                }

                return _rootPath;
            }
        }

        public static void Load(string servername, string username, string charactername)
        {
            GlobalProfile = ConfigurationResolver.Load<GlobalProfile>(Path.Combine(RootPath, "globalprofile.json"), ProfileJsonContext.DefaultToUse.GlobalProfile) ?? new GlobalProfile();

            string path = FileSystemHelper.CreateFolderIfNotExists(RootPath, username, servername, charactername);
            string fileToLoad = Path.Combine(path, "profile.json");

            ProfilePath = path;
            CurrentProfile = ConfigurationResolver.Load<Profile>(fileToLoad, ProfileJsonContext.DefaultToUse.Profile) ?? NewFromDefault();

            CurrentProfile.Username = username;
            CurrentProfile.ServerName = servername;
            CurrentProfile.CharacterName = charactername;

            ValidateFields(CurrentProfile);
        }

        public static void SetProfileAsDefault(Profile profile)
        {
            Save(profile, RootPath, "default.json");
        }

        public static Profile NewFromDefault()
        {
            return ConfigurationResolver.Load<Profile>(Path.Combine(RootPath, "default.json"), ProfileJsonContext.DefaultToUse.Profile) ?? new Profile();
        }

        /// <summary>
        /// The game window a new profile starts with when its default asks for a share of the client window: that share of each side, never
        /// smaller than the smallest game window the client allows (640 x 480) and never larger than the window. Zero (no share asked for,
        /// or a share outside 0 to 1, or an unknown window) leaves the size alone.
        /// </summary>
        public static Point StartGameWindowSize(double fraction, int windowWidth, int windowHeight, Point current)
        {
            if (fraction <= 0.0 || fraction > 1.0 || windowWidth <= 0 || windowHeight <= 0)
            {
                return current;
            }

            return new Point(
                Math.Min(windowWidth, Math.Max(640, (int) (windowWidth * fraction))),
                Math.Min(windowHeight, Math.Max(480, (int) (windowHeight * fraction)))
            );
        }

        private static void ValidateFields(Profile profile)
        {
            if (profile == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(profile.ServerName))
            {
                throw new InvalidDataException();
            }

            if (string.IsNullOrEmpty(profile.Username))
            {
                throw new InvalidDataException();
            }

            if (string.IsNullOrEmpty(profile.CharacterName))
            {
                throw new InvalidDataException();
            }

            if (profile.WindowClientBounds.X < 600)
            {
                profile.WindowClientBounds = new Point(600, profile.WindowClientBounds.Y);
            }

            if (profile.WindowClientBounds.Y < 480)
            {
                profile.WindowClientBounds = new Point(profile.WindowClientBounds.X, 480);
            }
        }

        public static void UnLoadProfile()
        {
            GlobalProfile = null;
            CurrentProfile = null;
        }

        internal static void Save(Profile profile, string path, string filename = "profile.json")
        {
            ConfigurationResolver.Save(profile, Path.Combine(path, filename), ProfileJsonContext.DefaultToUse.Profile);
            if (GlobalProfile != null)
            {
                ConfigurationResolver.Save(GlobalProfile, Path.Combine(RootPath, "globalprofile.json"), ProfileJsonContext.DefaultToUse.GlobalProfile);
            }
        }
    }
}
