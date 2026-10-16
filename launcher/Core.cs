using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("StickRPG4K")]
[assembly: AssemblyDescription("Steam launcher and configuration for the StickRPG4K mod")]
[assembly: AssemblyProduct("StickRPG4K")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace StickRpg4k {
    public sealed class LauncherSettings {
        public int Fps { get; set; }
        public bool Fullscreen { get; set; }
        public string OriginalExe { get; set; }
        public string GameDirectory { get; set; }
        public LauncherSettings() { Fps = 60; Fullscreen = true; OriginalExe = ""; GameDirectory = ""; }
        public static LauncherSettings Load(string root) {
            try {
                string file = Path.Combine(root, "launcher-settings.json");
                if (File.Exists(file)) {
                    LauncherSettings value = new JavaScriptSerializer().Deserialize<LauncherSettings>(File.ReadAllText(file));
                    if (value != null) {
                        if (value.Fps != 30 && value.Fps != 60 && value.Fps != 120) value.Fps = 60;
                        return value;
                    }
                }
            } catch (IOException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
            return new LauncherSettings();
        }
        public void Save(string root) {
            string file = Path.Combine(root, "launcher-settings.json"), temporary = file + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this));
            if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
        }
    }
    public static class LaunchCommand {
        // Quote a Windows command-line argument, including trailing backslashes.
        public static string Quote(string value) {
            System.Text.StringBuilder result = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char character in value) {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') result.Append('\\', slashes * 2 + 1);
                else result.Append('\\', slashes);
                result.Append(character);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        public static string InstallerArguments(string root, string original) {
            return "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " + Quote(Path.Combine(root, "Install.ps1")) +
                   (String.IsNullOrEmpty(original) ? "" : " -OriginalExe " + Quote(original));
        }
        public static string RuffleArguments(string root, int fps, bool fullscreen) {
            if (fps != 30 && fps != 60 && fps != 120) throw new ArgumentException("Unsupported motion setting.");
            return Quote(Path.Combine(root, "game", "Zinc Shell Mac.swf")) + (fullscreen ? " --fullscreen" : " --width 1600 --height 900") +
                   " --quality high --scale show-all --force-scale --storage disk --save-directory " + Quote(Path.Combine(root, "saves")) +
                   " --config " + Quote(Path.Combine(root, "runtime", "config")) + " --cache-directory " + Quote(Path.Combine(root, "runtime", "cache")) +
                   " --filesystem-access-mode allow --load-behavior delayed -P hdFPS=" + fps;
        }
    }
    public static class SetupService {
        public static void Prepare(string root, LauncherSettings settings, Action<string> progress) {
            if (String.IsNullOrEmpty(settings.OriginalExe)) throw new InvalidOperationException("Choose the original game EXE first.");
            string original = SourceDiscovery.ResolveOriginal(settings.OriginalExe);
            if (!SourceDiscovery.IsOriginal(original)) throw new InvalidOperationException("This is not the supported original Director's Cut EXE.");
            RunInstaller(root, original, progress);
            progress("Installing the Steam launcher...");
            string directory = Path.GetDirectoryName(original);
            SteamIntegration.Install(root, directory);
            settings.OriginalExe = Path.Combine(directory, SourceDiscovery.BackupFileName);
            settings.GameDirectory = directory;
            settings.Save(root);
        }
        public static void RunInstaller(string root, string original, Action<string> progress) {
            ProcessStartInfo info = new ProcessStartInfo {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = LaunchCommand.InstallerArguments(root, original), WorkingDirectory = root,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (Process process = new Process { StartInfo = info }) {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args) { if (args.Data != null) progress(args.Data); };
                process.Start();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                process.BeginOutputReadLine();
                process.WaitForExit();
                if (process.ExitCode != 0) {
                    string message = errors.Result.Trim();
                    if (message.IndexOf("folder exists", StringComparison.OrdinalIgnoreCase) >= 0)
                        message = "An older or incomplete game/ folder exists. Rename that generated folder, then try again. Your saves are in saves/.";
                    throw new InvalidOperationException(message.Length > 700 ? message.Substring(0, 700) : message);
                }
            }
        }
    }
    public static class GameSession {
        public static bool IsPrepared(string root) {
            try {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                var stamp = serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "game", ".installed.json")));
                var manifest = serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "patches", "manifest.json")));
                if (!stamp["version"].Equals(manifest["version"]) || !File.Exists(Path.Combine(root, "game", "Zinc Shell Mac.swf")) || !File.Exists(Path.Combine(root, "runtime", "ruffle.exe"))) return false;
                foreach (System.Collections.Generic.Dictionary<string, object> file in (System.Collections.IEnumerable)manifest["files"])
                    if (!File.Exists(Path.Combine(root, "game", (string)file["path"]))) return false;
                return true;
            } catch (IOException) { return false; } catch (ArgumentException) { return false; } catch (InvalidOperationException) { return false; } catch (System.Collections.Generic.KeyNotFoundException) { return false; }
        }
        public static int Run(string root, LauncherSettings settings) {
            Directory.CreateDirectory(Path.Combine(root, "saves"));
            using (Process game = Process.Start(new ProcessStartInfo {
                FileName = Path.Combine(root, "runtime", "ruffle.exe"), Arguments = LaunchCommand.RuffleArguments(root, settings.Fps, settings.Fullscreen),
                WorkingDirectory = Path.Combine(root, "game"), UseShellExecute = false
            })) {
                // Steam starts this EXE. Keep that process alive for the entire
                // Ruffle session, including when the configuration window is closed.
                game.WaitForExit();
                return game.ExitCode;
            }
        }
    }
    public static class ModLocation {
        public static string Resolve(string directory) {
            if (File.Exists(Path.Combine(directory, "Install.ps1")) && File.Exists(Path.Combine(directory, "patches", "manifest.json"))) return Path.GetFullPath(directory);
            IntegrationLink link = SteamIntegration.ReadLink(directory);
            if (link != null && Path.IsPathRooted(link.ModRoot) && File.Exists(Path.Combine(link.ModRoot, "Install.ps1")) &&
                File.Exists(Path.Combine(link.ModRoot, "patches", "manifest.json"))) return Path.GetFullPath(link.ModRoot);
            throw new InvalidOperationException("The StickRPG4K folder could not be found. Open config.exe in your extracted mod folder to install or repair the Steam launcher.");
        }
        public static void ReportError(string root, Exception error, bool interactive) {
            try { File.WriteAllText(Path.Combine(root, "launcher-error.log"), error.ToString()); } catch (Exception) { }
            if (interactive) MessageBox.Show(error.Message, "StickRPG4K", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else Console.Error.WriteLine(error.Message);
        }
    }
    internal static class GameProgram {
        [STAThread]
        private static int Main() {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            try {
                root = ModLocation.Resolve(root);
                string id;
                using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                    id = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd('\\').ToUpperInvariant()))).Replace("-", "");
                bool created;
                using (Mutex session = new Mutex(true, @"Local\StickRPG4K-" + id, out created)) {
                    if (!created) return 0;
                    LauncherSettings settings = LauncherSettings.Load(root);
                    if (!GameSession.IsPrepared(root) || !SteamIntegration.IsInstalled(root, settings.GameDirectory)) {
                        using (Process config = Process.Start(new ProcessStartInfo {
                            FileName = Path.Combine(root, "config.exe"), Arguments = "--launch-after-save", WorkingDirectory = root, UseShellExecute = false
                        })) {
                            config.WaitForExit();
                            if (config.ExitCode != 0) return config.ExitCode;
                        }
                        settings = LauncherSettings.Load(root);
                        if (!GameSession.IsPrepared(root) || !SteamIntegration.IsInstalled(root, settings.GameDirectory)) return 0;
                    }
                    return GameSession.Run(root, settings);
                }
            } catch (Exception error) { ModLocation.ReportError(root, error, true); return 1; }
        }
    }
}
