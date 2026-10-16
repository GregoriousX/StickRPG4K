using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace StickRpg4k {
    public sealed class IntegrationLink {
        public int Format { get; set; }
        public string ModRoot { get; set; }
        public string OriginalSha256 { get; set; }
        public string LauncherSha256 { get; set; }
        public string ConfigSha256 { get; set; }
    }
    public static class SteamIntegration {
        public const string LinkFileName = "StickRPG4K.link.json";
        public static IntegrationLink ReadLink(string directory) {
            string path = Path.Combine(directory, LinkFileName);
            if (!File.Exists(path)) return null;
            IntegrationLink value = new JavaScriptSerializer().Deserialize<IntegrationLink>(File.ReadAllText(path));
            if (value == null || value.Format != 1 || value.OriginalSha256 != SourceDiscovery.OriginalSha256 || String.IsNullOrEmpty(value.ModRoot))
                throw new InvalidOperationException("Invalid StickRPG4K installation link. Your original EXE has not been changed.");
            return value;
        }
        private static bool SamePath(string first, string second) {
            return String.Equals(Path.GetFullPath(first).TrimEnd('\\'), Path.GetFullPath(second).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsInstalled(string root, string directory) {
            if (String.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;
            IntegrationLink link = ReadLink(directory);
            string target = Path.Combine(directory, SourceDiscovery.OriginalFileName);
            return link != null && SamePath(root, link.ModRoot) && File.Exists(target) && SourceDiscovery.Hash(target) == link.LauncherSha256 &&
                   SourceDiscovery.IsOriginal(Path.Combine(directory, SourceDiscovery.BackupFileName));
        }
        private static void AllowOwnedFile(string path, string wantedHash, string previousHash) {
            if (!File.Exists(path)) return;
            string actual = SourceDiscovery.Hash(path);
            if (actual != wantedHash && actual != previousHash)
                throw new InvalidOperationException("An unrelated file already exists: " + path + ". Move it before installing StickRPG4K.");
        }
        public static void Install(string root, string gameDirectory) {
            root = Path.GetFullPath(root); gameDirectory = Path.GetFullPath(gameDirectory);
            string launcher = Path.Combine(root, "StickRPG4K.exe"), config = Path.Combine(root, "config.exe");
            string target = Path.Combine(gameDirectory, SourceDiscovery.OriginalFileName), backup = Path.Combine(gameDirectory, SourceDiscovery.BackupFileName);
            if (!Directory.Exists(gameDirectory) || !File.Exists(launcher) || !File.Exists(config)) throw new InvalidOperationException("The mod package or original game folder is incomplete.");
            if (SourceDiscovery.IsOriginal(launcher) || SourceDiscovery.IsOriginal(config)) throw new InvalidOperationException("The mod launcher must not contain the original game EXE.");
            IntegrationLink old = ReadLink(gameDirectory);
            string launcherHash = SourceDiscovery.Hash(launcher), configHash = SourceDiscovery.Hash(config);
            bool hasBackup = File.Exists(backup), originalAtTarget = SourceDiscovery.IsOriginal(target);
            if (hasBackup && !SourceDiscovery.IsOriginal(backup)) throw new InvalidOperationException("stick.exe already exists and is not the supported original game. No files were changed.");
            if (!hasBackup && !originalAtTarget) throw new InvalidOperationException("The original game EXE is missing. Restore the Steam game files before installing.");
            if (!originalAtTarget) AllowOwnedFile(target, launcherHash, old == null ? null : old.LauncherSha256);
            string installedConfig = Path.Combine(gameDirectory, "config.exe");
            AllowOwnedFile(installedConfig, configHash, old == null ? null : old.ConfigSha256);
            IntegrationLink link = new IntegrationLink {
                Format = 1, ModRoot = root, OriginalSha256 = SourceDiscovery.OriginalSha256,
                LauncherSha256 = launcherHash, ConfigSha256 = configHash
            };
            bool renamed = false;
            using (FileTransaction transaction = new FileTransaction(gameDirectory)) {
                try {
                    transaction.PutFile(config, installedConfig);
                    transaction.PutText(new JavaScriptSerializer().Serialize(link), Path.Combine(gameDirectory, LinkFileName));
                    if (!hasBackup) { File.Move(target, backup); renamed = true; }
                    transaction.PutFile(launcher, target);
                    if (!IsInstalled(root, gameDirectory)) throw new IOException("Steam launcher verification failed.");
                    transaction.Commit();
                } catch {
                    transaction.Rollback();
                    if (renamed && !File.Exists(target)) File.Move(backup, target);
                    throw;
                }
            }
        }
        public static void Restore(string root, string gameDirectory) {
            gameDirectory = Path.GetFullPath(gameDirectory);
            IntegrationLink link = ReadLink(gameDirectory);
            if (link == null || !SamePath(root, link.ModRoot)) throw new InvalidOperationException("This game folder is not linked to this mod installation.");
            string target = Path.Combine(gameDirectory, SourceDiscovery.OriginalFileName), backup = Path.Combine(gameDirectory, SourceDiscovery.BackupFileName);
            if (!SourceDiscovery.IsOriginal(backup)) throw new InvalidOperationException("The original stick.exe backup is missing or changed. Restore the Steam game files instead.");
            if (!SourceDiscovery.IsOriginal(target)) AllowOwnedFile(target, link.LauncherSha256, null);
            using (FileTransaction transaction = new FileTransaction(gameDirectory)) {
                transaction.PutFile(backup, target);
                if (!SourceDiscovery.IsOriginal(target)) throw new IOException("Restored EXE verification failed.");
                transaction.Commit();
            }
            File.Delete(backup);
            // Keep config.exe and its link so the user can enable the mod again.
        }
        // Each replacement is atomic on the game's volume. On an ordinary I/O
        // failure, restore only files touched by this transaction. No directories
        // or user saves are removed; the original stick.exe is never overwritten.
        private sealed class FileTransaction : IDisposable {
            private sealed class Change { public string Target; public string Previous; }
            private readonly string directory;
            private readonly List<Change> changes = new List<Change>();
            private readonly List<string> temporaryFiles = new List<string>();
            private bool finished;
            public FileTransaction(string path) { directory = Path.GetFullPath(path); }
            private string Temporary() {
                string path = Path.Combine(directory, ".sr4k-" + Guid.NewGuid().ToString("N") + ".tmp");
                temporaryFiles.Add(path); return path;
            }
            private void Put(string staged, string target) {
                if (!SamePath(directory, Path.GetDirectoryName(target))) throw new IOException("Unsafe installation path.");
                Change change = new Change { Target = target };
                if (File.Exists(target)) { change.Previous = Temporary(); File.Replace(staged, target, change.Previous); }
                else File.Move(staged, target);
                changes.Add(change);
            }
            public void PutFile(string source, string target) {
                if (File.Exists(target) && SourceDiscovery.Hash(source) == SourceDiscovery.Hash(target)) return;
                string staged = Temporary(); File.Copy(source, staged); Put(staged, target);
            }
            public void PutText(string text, string target) {
                if (File.Exists(target) && File.ReadAllText(target) == text) return;
                string staged = Temporary(); File.WriteAllText(staged, text); Put(staged, target);
            }
            public void Commit() { finished = true; }
            public void Rollback() {
                if (finished) return;
                for (int index = changes.Count - 1; index >= 0; index--) {
                    Change change = changes[index];
                    if (change.Previous == null) File.Delete(change.Target);
                    else File.Copy(change.Previous, change.Target, true);
                }
                finished = true;
            }
            public void Dispose() {
                if (!finished) Rollback();
                foreach (string path in temporaryFiles) if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
