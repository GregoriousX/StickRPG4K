using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickRpg4k {
    public sealed class LauncherForm : Form {
        private readonly string root;
        private readonly bool launchAfterSave;
        private readonly TextBox source = new TextBox();
        private readonly ComboBox motion = new ComboBox();
        private readonly ComboBox display = new ComboBox();
        private readonly Label sourceStatus = new Label();
        private readonly Label note = new Label();
        private readonly Label status = new Label();
        private readonly Button browse = new Button();
        private readonly Button findSteam = new Button();
        private readonly Button play = new Button();
        private readonly Button restore = new Button();
        private readonly ProgressBar progress = new ProgressBar();
        private bool busy;
        private readonly Color muted = Color.FromArgb(166, 177, 194);
        public LauncherForm(string rootPath, string originalArgument) : this(rootPath, originalArgument, false) { }
        public LauncherForm(string rootPath, string originalArgument, bool continueToGame) {
            root = rootPath;
            launchAfterSave = continueToGame;
            Text = "StickRPG4K settings";
            ClientSize = new Size(608, 433);
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
            AddLabel("Original game EXE in this folder", 24, 98, 550, 20);
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
            progress.SetBounds(24, 365, 560, 5);
            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 25;
            progress.Visible = false;
            Controls.Add(progress);
            status.SetBounds(24, 319, 560, 40);
            status.ForeColor = muted;
            Controls.Add(status);
            ConfigureButton(restore, "Restore original EXE", 24, 382, 190, 34, false);
            restore.TabIndex = 5;
            restore.Click += RestoreClicked;
            ConfigureButton(play, "Save & install", 394, 380, 190, 38, true);
            play.TabIndex = 6;
            play.Click += PlayClicked;
            AcceptButton = play;
            FormClosing += delegate(object sender, FormClosingEventArgs args) { if (busy) args.Cancel = true; };
            LauncherSettings settings = LauncherSettings.Load(root);
            motion.SelectedIndex = settings.Fps == 30 ? 0 : settings.Fps == 120 ? 2 : 1;
            display.SelectedIndex = settings.Fullscreen ? 0 : 1;
            if (!String.IsNullOrEmpty(originalArgument) && File.Exists(originalArgument)) SetSource(originalArgument, "Game copy selected.");
            else if (!String.IsNullOrEmpty(settings.OriginalExe) && File.Exists(Path.Combine(root, settings.OriginalExe))) SetSource(Path.Combine(root, settings.OriginalExe), "Saved game copy.");
            else FindSource(false);
            UpdateInstallationStatus();
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
        private void SetSource(string path, string message) { source.Text = SourceDiscovery.ResolveOriginal(path); sourceStatus.Text = message; }
        private string SelectedDirectory() { return String.IsNullOrEmpty(source.Text) ? "" : Path.GetDirectoryName(source.Text); }
        private void UpdateInstallationStatus() {
            bool installed = SteamIntegration.IsInstalled(root, SelectedDirectory());
            restore.Enabled = installed;
            play.Text = launchAfterSave ? "Save & play" : installed ? "Save settings" : "Save & install";
            status.Text = installed ? "Installed in this folder. Start the game normally or use Steam." : "Extract the full release ZIP beside the game EXE. Any folder works.";
        }
        private void FindSource(bool onlySteam) {
            if (onlySteam) {
                string[] games = SourceDiscovery.FindSteamExecutables(SourceDiscovery.SteamRoots());
                if (games.Length > 0) SetSource(games[0], "Steam copy found.");
                else sourceStatus.Text = "Steam copy not found. Choose the EXE manually.";
            } else {
                GameSource[] games = SourceDiscovery.FindCandidates(root);
                if (games.Length > 0) SetSource(games[0].Path, games[0].Origin == "Steam" ? "Steam copy found. Extract the ZIP there." : "Compatible EXE found in this folder.");
                else sourceStatus.Text = "Choose your original Director's Cut EXE.";
            }
            UpdateInstallationStatus();
        }
        private void BrowseClicked(object sender, EventArgs args) {
            using (OpenFileDialog dialog = new OpenFileDialog()) {
                dialog.Title = "Choose the original Steam / Director's Cut EXE";
                dialog.Filter = "Game EXE|Stick RPG 2 Director's Cut.exe;stick.exe|Executable files (*.exe)|*.exe";
                dialog.CheckFileExists = true;
                if (File.Exists(source.Text)) { dialog.InitialDirectory = Path.GetDirectoryName(source.Text); dialog.FileName = Path.GetFileName(source.Text); }
                if (dialog.ShowDialog(this) == DialogResult.OK) { SetSource(dialog.FileName, "Game copy selected."); UpdateInstallationStatus(); }
            }
        }
        private void SetBusy(bool value) {
            busy = value;
            browse.Enabled = findSteam.Enabled = motion.Enabled = display.Enabled = play.Enabled = !value;
            restore.Enabled = !value && SteamIntegration.IsInstalled(root, SelectedDirectory());
            ControlBox = !value;
            progress.Visible = value;
            if (value) play.Text = "Preparing..."; else UpdateInstallationStatus();
        }
        private void ProgressMessage(string text) {
            if (String.IsNullOrWhiteSpace(text) || IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)delegate { if (!IsDisposed) status.Text = text; });
        }
        internal LauncherSettings CurrentSettings() {
            return new LauncherSettings {
                Fps = motion.SelectedIndex == 0 ? 30 : motion.SelectedIndex == 2 ? 120 : 60,
                Fullscreen = display.SelectedIndex == 0, OriginalExe = source.Text
            };
        }
        private async void PlayClicked(object sender, EventArgs args) {
            if (busy) return;
            if (String.IsNullOrEmpty(source.Text)) { status.Text = "Choose the original game EXE first."; return; }
            LauncherSettings settings = CurrentSettings();
            SetBusy(true);
            status.Text = "Checking the installed mod...";
            try {
                await Task.Run(() => SetupService.Prepare(root, settings, ProgressMessage));
                SetSource(Path.Combine(root, settings.OriginalExe), "Original preserved as stick.exe.");
                SetBusy(false);
                if (launchAfterSave) { DialogResult = DialogResult.OK; Close(); }
                else status.Text = "Saved. Start the game normally, through Steam, or with StickRPG4K.exe.";
            } catch (Exception error) {
                SetBusy(false);
                status.Text = "Could not save or install the mod.";
                MessageBox.Show(this, error.Message, "StickRPG4K", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private async void RestoreClicked(object sender, EventArgs args) {
            if (busy) return;
            string directory = SelectedDirectory();
            SetBusy(true);
            try {
                string originalName = SteamIntegration.ReadLink(directory).OriginalFileName;
                await Task.Run(() => SteamIntegration.Restore(root, directory));
                LauncherSettings settings = CurrentSettings();
                settings.OriginalExe = originalName;
                settings.GameDirectory = ".";
                settings.Save(root);
                SetSource(Path.Combine(root, settings.OriginalExe), "Original EXE restored.");
                SetBusy(false);
                status.Text = "Original EXE restored. Mod settings and saves are preserved.";
            } catch (Exception error) {
                SetBusy(false);
                MessageBox.Show(this, error.Message, "StickRPG4K", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    internal static class ConfigProgram {
        [STAThread]
        private static int Main(string[] args) {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            bool headless = Array.IndexOf(args, "--install") >= 0 || Array.IndexOf(args, "--restore") >= 0;
            try {
                root = ModLocation.Resolve(root);
                string original = null;
                foreach (string argument in args)
                    if (String.Equals(Path.GetFileName(argument), SourceDiscovery.OriginalFileName, StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(Path.GetFileName(argument), SourceDiscovery.BackupFileName, StringComparison.OrdinalIgnoreCase)) { original = argument; break; }
                int installOption = Array.IndexOf(args, "--install");
                if (installOption >= 0 && installOption + 1 < args.Length && !args[installOption + 1].StartsWith("--")) original = Path.GetFullPath(args[installOption + 1]);
                if (headless) {
                    LauncherSettings settings = LauncherSettings.Load(root);
                    if (original != null) settings.OriginalExe = original;
                    if (Array.IndexOf(args, "--restore") >= 0) {
                        string originalName = SteamIntegration.ReadLink(root).OriginalFileName;
                        SteamIntegration.Restore(root, root);
                        settings.OriginalExe = originalName;
                        settings.GameDirectory = ".";
                        settings.Save(root);
                    } else {
                        if (String.IsNullOrEmpty(settings.OriginalExe)) {
                            GameSource[] games = SourceDiscovery.FindCandidates(root);
                            if (games.Length > 0) settings.OriginalExe = games[0].Path;
                        }
                        SetupService.Prepare(root, settings, Console.WriteLine);
                    }
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool continueToGame = Array.IndexOf(args, "--launch-after-save") >= 0;
                using (LauncherForm form = new LauncherForm(root, original, continueToGame)) {
                    Application.Run(form);
                    return continueToGame && form.DialogResult != DialogResult.OK ? 2 : 0;
                }
            } catch (Exception error) { ModLocation.ReportError(root, error, !headless); return 1; }
        }
    }
}
