using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace StickRpg4k {
    public sealed class GameSource {
        public string Path { get; set; }
        public string Origin { get; set; }
    }
    public static class SourceDiscovery {
        public const string OriginalFileName = "Stick RPG 2 Director's Cut.exe";
        public const string BackupFileName = "stick.exe";
        public const string OriginalSha256 = "2bde20d37e45ea5ac1d517d55a89241250718f213f615982171aad28b9b56a06";
        public const string SteamAppId = "307640";
        public static string Hash(string path) {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static bool IsOriginal(string path) {
            return File.Exists(path) && Hash(path) == OriginalSha256;
        }
        public static string ResolveOriginal(string path) {
            string full = System.IO.Path.GetFullPath(path);
            if (String.Equals(System.IO.Path.GetFileName(full), OriginalFileName, StringComparison.OrdinalIgnoreCase)) {
                string backup = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(full), BackupFileName);
                if (!IsOriginal(full) && IsOriginal(backup)) return backup;
            }
            return full;
        }
        private const string PairPattern = @"""(?<key>(?:\\.|[^""\\])*)""\s*""(?<value>(?:\\.|[^""\\])*)""";
        private static string Decode(string value) {
            return value.Replace("\\\\", "\\").Replace("\\\"", "\"");
        }
        private static void AddPath(List<string> paths, string value) {
            if (String.IsNullOrWhiteSpace(value)) return;
            try {
                string path = System.IO.Path.GetFullPath(value);
                if (!paths.Exists(item => String.Equals(item, path, StringComparison.OrdinalIgnoreCase))) paths.Add(path);
            } catch (ArgumentException) { } catch (NotSupportedException) { }
        }
        private static string ReadValue(string text, string key) {
            foreach (Match pair in Regex.Matches(text, PairPattern))
                if (String.Equals(pair.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase)) return Decode(pair.Groups["value"].Value);
            return null;
        }
        public static string[] SteamRoots() {
            List<string> roots = new List<string>();
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (key != null) AddPath(roots, key.GetValue("SteamPath") as string);
                foreach (string name in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(name))
                        if (key != null) AddPath(roots, key.GetValue("InstallPath") as string);
            } catch (System.Security.SecurityException) { }
            AddPath(roots, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            return roots.ToArray();
        }
        public static string[] FindSteamExecutables(string[] steamRoots) {
            List<string> libraries = new List<string>();
            foreach (string root in steamRoots) {
                AddPath(libraries, root);
                foreach (string relative in new[] { @"steamapps\libraryfolders.vdf", @"config\libraryfolders.vdf" }) {
                    string file = System.IO.Path.Combine(root, relative);
                    if (!File.Exists(file)) continue;
                    try {
                        foreach (Match pair in Regex.Matches(File.ReadAllText(file), PairPattern)) {
                            string key = pair.Groups["key"].Value, value = Decode(pair.Groups["value"].Value);
                            int numericKey;
                            if ((key == "path" || Int32.TryParse(key, out numericKey)) && System.IO.Path.IsPathRooted(value)) AddPath(libraries, value);
                        }
                    } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            List<string> games = new List<string>();
            foreach (string library in libraries) {
                string common = System.IO.Path.Combine(library, @"steamapps\common"), installDir = "Stick RPG 2";
                string manifest = System.IO.Path.Combine(library, "steamapps", "appmanifest_" + SteamAppId + ".acf");
                try {
                    if (File.Exists(manifest)) installDir = ReadValue(File.ReadAllText(manifest), "installdir") ?? installDir;
                    string game = System.IO.Path.GetFullPath(System.IO.Path.Combine(common, installDir, OriginalFileName));
                    string prefix = System.IO.Path.GetFullPath(common).TrimEnd('\\') + "\\";
                    if (game.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                        game = ResolveOriginal(game);
                        if (File.Exists(game)) AddPath(games, game);
                    }
                } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
            }
            return games.ToArray();
        }
        public static GameSource[] FindCandidates(string modRoot) {
            List<GameSource> results = new List<GameSource>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in FindSteamExecutables(SteamRoots()))
                if (seen.Add(path)) results.Add(new GameSource { Path = path, Origin = "Steam" });
            foreach (string folder in new[] { modRoot, System.IO.Path.Combine(modRoot, "original") }) {
                if (!Directory.Exists(folder)) continue;
                string preferred = System.IO.Path.Combine(folder, OriginalFileName);
                preferred = ResolveOriginal(preferred);
                if (File.Exists(preferred) && seen.Add(preferred)) results.Add(new GameSource { Path = preferred, Origin = "Local copy" });
                foreach (string path in Directory.GetFiles(folder, "*.exe")) {
                    string name = System.IO.Path.GetFileName(path);
                    if (String.Equals(name, "StickRPG4K.exe", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(name, "config.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (seen.Add(path)) results.Add(new GameSource { Path = path, Origin = "Local copy" });
                }
            }
            return results.ToArray();
        }
    }
}
