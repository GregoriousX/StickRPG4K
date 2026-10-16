using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("StickRPG4K Launcher")]
[assembly: AssemblyDescription("Launch settings and Steam game discovery for the StickRPG4K mod")]
[assembly: AssemblyProduct("StickRPG4K")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace StickRpg4k {
    public sealed class LauncherSettings {
        public int Fps { get; set; }
        public bool Fullscreen { get; set; }
        public string OriginalExe { get; set; }
        public LauncherSettings() { Fps = 60; Fullscreen = true; OriginalExe = ""; }
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
        public void Save(string root) { File.WriteAllText(Path.Combine(root, "launcher-settings.json"), new JavaScriptSerializer().Serialize(this)); }
    }
    public static class LaunchCommand {
        private static string Q(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
        public static string InstallerArguments(string root, string original) {
            return "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " + Q(Path.Combine(root, "Install.ps1")) +
                   (String.IsNullOrEmpty(original) ? "" : " -OriginalExe " + Q(original));
        }
        public static string RuffleArguments(string root, int fps, bool fullscreen) {
            if (fps != 30 && fps != 60 && fps != 120) throw new ArgumentException("Unsupported motion setting.");
            return Q(Path.Combine(root, "game", "Zinc Shell Mac.swf")) + (fullscreen ? " --fullscreen" : " --width 1600 --height 900") +
                   " --quality high --scale show-all --force-scale --storage disk --save-directory " + Q(Path.Combine(root, "saves")) +
                   " --config " + Q(Path.Combine(root, "runtime", "config")) + " --cache-directory " + Q(Path.Combine(root, "runtime", "cache")) +
                   " --filesystem-access-mode allow --load-behavior delayed -P hdFPS=" + fps;
        }
    }
    public sealed class LauncherForm : Form {
        private readonly string root;
        private readonly TextBox source = new TextBox();
        private readonly ComboBox motion = new ComboBox();
        private readonly ComboBox display = new ComboBox();
        private readonly Label sourceStatus = new Label();
        private readonly Label note = new Label();
        private readonly Label status = new Label();
        private readonly Button browse = new Button();
        private readonly Button findSteam = new Button();
        private readonly Button play = new Button();
        private readonly ProgressBar progress = new ProgressBar();
        private bool busy;
        private readonly Color muted = Color.FromArgb(166, 177, 194);
        public LauncherForm(string rootPath, string originalArgument) {
            root = rootPath;
            Text = "StickRPG4K";
            ClientSize = new Size(608, 390);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(23, 28, 38);
            ForeColor = Color.FromArgb(235, 240, 248);
            Font = new Font("Segoe UI", 9.5F);
            Label title = AddLabel("StickRPG4K", 24, 17, 550, 35);
            title.Font = new Font("Segoe UI", 21, FontStyle.Bold);
            AddLabel("Widescreen. Ready-made 4K assets. Smoother motion.", 25, 57, 550, 21).ForeColor = muted;
            AddLabel("Original game EXE", 24, 98, 550, 20);
            source.SetBounds(24, 122, 420, 27);
            source.ReadOnly = true;
            source.BackColor = Color.FromArgb(35, 43, 57);
            source.ForeColor = ForeColor;
            source.BorderStyle = BorderStyle.FixedSingle;
            source.TabIndex = 0;
            Controls.Add(source);
            ConfigureButton(browse, "Choose EXE...", 456, 120, 128, 31, false);
            browse.TabIndex = 1;
            browse.Click += BrowseClicked;
            ConfigureButton(findSteam, "Find Steam copy", 24, 159, 143, 29, false);
            findSteam.TabIndex = 2;
            findSteam.Click += delegate { FindSource(true); };
            sourceStatus.SetBounds(179, 163, 405, 32);
            sourceStatus.ForeColor = muted;
            Controls.Add(sourceStatus);
            AddLabel("Motion smoothness", 24, 210, 260, 22);
            AddLabel("Display mode", 316, 210, 268, 22);
            ConfigureCombo(motion, 24, 236, 268);
            motion.Items.AddRange(new object[] { "30 FPS (original)", "60 FPS (recommended)", "120 FPS (experimental)" });
            motion.TabIndex = 3;
            motion.SelectedIndexChanged += delegate { UpdateNote(); };
            ConfigureCombo(display, 316, 236, 268);
            display.Items.AddRange(new object[] { "Fullscreen", "Windowed (1600 x 900)" });
            display.TabIndex = 4;
            note.SetBounds(24, 276, 560, 35);
            note.ForeColor = muted;
            Controls.Add(note);
            progress.SetBounds(24, 319, 560, 5);
            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 25;
            progress.Visible = false;
            Controls.Add(progress);
            status.SetBounds(24, 337, 402, 40);
            status.ForeColor = muted;
            Controls.Add(status);
            ConfigureButton(play, "Play", 448, 335, 136, 38, true);
            play.TabIndex = 5;
            play.Click += PlayClicked;
            AcceptButton = play;
            FormClosing += delegate(object sender, FormClosingEventArgs args) { if (busy) args.Cancel = true; };
            LauncherSettings settings = LauncherSettings.Load(root);
            motion.SelectedIndex = settings.Fps == 30 ? 0 : settings.Fps == 120 ? 2 : 1;
            display.SelectedIndex = settings.Fullscreen ? 0 : 1;
            if (!String.IsNullOrEmpty(originalArgument) && File.Exists(originalArgument)) SetSource(originalArgument, "Game copy selected.");
            else if (!String.IsNullOrEmpty(settings.OriginalExe) && File.Exists(settings.OriginalExe)) SetSource(settings.OriginalExe, "Saved game copy.");
            else FindSource(false);
            status.Text = File.Exists(Path.Combine(root, "game", ".installed.json")) ? "Mod installed. Ready to play." : "First launch prepares the mod automatically.";
        }
        private Label AddLabel(string text, int x, int y, int width, int height) {
            Label label = new Label { Text = text };
            label.SetBounds(x, y, width, height);
            Controls.Add(label);
            return label;
        }
        private void ConfigureButton(Button button, string text, int x, int y, int width, int height, bool primary) {
            button.Text = text;
            button.SetBounds(x, y, width, height);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(65, 149, 241) : Color.FromArgb(67, 79, 98);
            button.BackColor = primary ? Color.FromArgb(44, 115, 205) : Color.FromArgb(35, 43, 57);
            button.ForeColor = ForeColor;
            button.Font = new Font(Font, primary ? FontStyle.Bold : FontStyle.Regular);
            button.Cursor = Cursors.Hand;
            Controls.Add(button);
        }
        private void ConfigureCombo(ComboBox combo, int x, int y, int width) {
            combo.SetBounds(x, y, width, 28);
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Flat;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 22;
            combo.BackColor = Color.FromArgb(35, 43, 57);
            combo.ForeColor = ForeColor;
            combo.DrawItem += delegate(object sender, DrawItemEventArgs args) {
                if (args.Index < 0) return;
                Color background = (args.State & DrawItemState.Selected) != 0 ? Color.FromArgb(44, 115, 205) : combo.BackColor;
                using (SolidBrush brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, args.Bounds);
                Rectangle textBounds = new Rectangle(args.Bounds.X + 7, args.Bounds.Y, args.Bounds.Width - 10, args.Bounds.Height);
                TextRenderer.DrawText(args.Graphics, combo.GetItemText(combo.Items[args.Index]), combo.Font, textBounds, combo.ForeColor,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            Controls.Add(combo);
        }
        private void UpdateNote() {
            note.Text = motion.SelectedIndex == 2 ? "Experimental: this Ruffle build supports up to about 100 smoothing updates/s." : "Smoother camera and movement without speeding up the game.";
        }
        private void SetSource(string path, string message) { source.Text = Path.GetFullPath(path); sourceStatus.Text = message; }
        private void FindSource(bool onlySteam) {
            if (onlySteam) {
                string[] games = SourceDiscovery.FindSteamExecutables(SourceDiscovery.SteamRoots());
                if (games.Length > 0) SetSource(games[0], "Steam copy found.");
                else sourceStatus.Text = "Steam copy not found. Choose the EXE manually.";
            } else {
                GameSource[] games = SourceDiscovery.FindCandidates(root);
                if (games.Length > 0) SetSource(games[0].Path, games[0].Origin == "Steam" ? "Steam copy found." : "Original game copy found.");
                else sourceStatus.Text = "Choose your original Director's Cut EXE.";
            }
        }
        private void BrowseClicked(object sender, EventArgs args) {
            using (OpenFileDialog dialog = new OpenFileDialog()) {
                dialog.Title = "Choose the original Steam / Director's Cut EXE";
                dialog.Filter = "Stick RPG 2 Director's Cut.exe|Stick RPG 2 Director's Cut.exe|Executable files (*.exe)|*.exe";
                dialog.CheckFileExists = true;
                if (File.Exists(source.Text)) { dialog.InitialDirectory = Path.GetDirectoryName(source.Text); dialog.FileName = Path.GetFileName(source.Text); }
                if (dialog.ShowDialog(this) == DialogResult.OK) SetSource(dialog.FileName, "Game copy selected.");
            }
        }
        private void SetBusy(bool value) {
            busy = value;
            browse.Enabled = findSteam.Enabled = motion.Enabled = display.Enabled = play.Enabled = !value;
            ControlBox = !value;
            progress.Visible = value;
            play.Text = value ? "Preparing..." : "Play";
        }
        private void ProgressMessage(string text) {
            if (String.IsNullOrWhiteSpace(text) || IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)delegate { if (!IsDisposed) status.Text = text; });
        }
        private void RunInstaller(string original) {
            ProcessStartInfo info = new ProcessStartInfo {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = LaunchCommand.InstallerArguments(root, original), WorkingDirectory = root,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (Process process = new Process { StartInfo = info }) {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args) { ProgressMessage(args.Data); };
                process.Start();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                process.BeginOutputReadLine();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException(ShortError(errors.Result));
            }
        }
        private static string ShortError(string output) {
            if (output.IndexOf("not the supported original", StringComparison.OrdinalIgnoreCase) >= 0) return "This EXE is not the supported Director's Cut build. Choose the original Steam game EXE.";
            if (output.IndexOf("folder exists", StringComparison.OrdinalIgnoreCase) >= 0) return "An older or incomplete game/ folder exists. Rename that generated folder, then try again. Your saves are in saves/.";
            if (output.IndexOf("EXE does not exist", StringComparison.OrdinalIgnoreCase) >= 0) return "The original game EXE could not be found. Choose it again.";
            string message = output.Trim();
            return message.Length > 700 ? message.Substring(0, 700) : message;
        }
        internal LauncherSettings CurrentSettings() {
            return new LauncherSettings {
                Fps = motion.SelectedIndex == 0 ? 30 : motion.SelectedIndex == 2 ? 120 : 60,
                Fullscreen = display.SelectedIndex == 0, OriginalExe = source.Text
            };
        }
        private async void PlayClicked(object sender, EventArgs args) {
            if (busy) return;
            if (String.IsNullOrEmpty(source.Text) && !File.Exists(Path.Combine(root, "game", ".installed.json"))) { status.Text = "Choose the original game EXE first."; return; }
            LauncherSettings settings = CurrentSettings();
            SetBusy(true);
            status.Text = "Checking the installed mod...";
            try {
                settings.Save(root);
                await Task.Run(() => RunInstaller(settings.OriginalExe));
                Directory.CreateDirectory(Path.Combine(root, "saves"));
                Process.Start(new ProcessStartInfo {
                    FileName = Path.Combine(root, "runtime", "ruffle.exe"), Arguments = LaunchCommand.RuffleArguments(root, settings.Fps, settings.Fullscreen),
                    WorkingDirectory = Path.Combine(root, "game"), UseShellExecute = false
                });
                SetBusy(false);
                Close();
            } catch (Exception error) {
                SetBusy(false);
                status.Text = "Could not start the game.";
                MessageBox.Show(this, error.Message, "StickRPG4K", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    internal static class Program {
        [STAThread]
        private static void Main(string[] args) {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string original = null;
            foreach (string argument in args)
                if (String.Equals(Path.GetFileName(argument), SourceDiscovery.OriginalFileName, StringComparison.OrdinalIgnoreCase)) { original = argument; break; }
            Application.Run(new LauncherForm(AppDomain.CurrentDomain.BaseDirectory, original));
        }
    }
}
