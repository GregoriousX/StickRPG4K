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
        private readonly CheckBox upscale = new CheckBox();
        private readonly Label sourceStatus = new Label();
        private readonly Label note = new Label();
        private readonly Label status = new Label();
        private readonly Button browse = new Button();
        private readonly Button findSteam = new Button();
        private readonly Button play = new Button();
        private readonly Button restore = new Button();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Panel banner = new Panel();
        private readonly Panel bannerStripe = new Panel();
        private readonly Label bannerText = new Label();
        private bool busy;
        private readonly Color muted = Color.FromArgb(166, 177, 194);
        private readonly Color card = Color.FromArgb(30, 37, 50);
        private readonly Color input = Color.FromArgb(40, 49, 65);
        public LauncherForm(string rootPath, string originalArgument) : this(rootPath, originalArgument, false) { }
        public LauncherForm(string rootPath, string originalArgument, bool continueToGame) {
            root = rootPath;
            launchAfterSave = continueToGame;
            Text = "StickRPG4K settings";
            ClientSize = new Size(640, 596);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            // Layout is in 96-DPI pixels and scaled once below; fonts are in points.
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(23, 28, 38);
            ForeColor = Color.FromArgb(235, 240, 248);
            Font = new Font("Segoe UI", 9.5F);
            Label title = AddLabel(this, "StickRPG4K", 22, 14, 300, 38);
            title.Font = new Font("Segoe UI", 20, FontStyle.Bold);
            Label version = AddLabel(this, "v" + typeof(LauncherForm).Assembly.GetName().Version.ToString(3), 456, 26, 160, 20);
            version.TextAlign = ContentAlignment.MiddleRight;
            version.ForeColor = muted;
            AddLabel(this, "Widescreen Stick RPG 2 with sharper art and smoother motion.", 24, 52, 592, 20).ForeColor = muted;
            // Installation state at a glance.
            banner.SetBounds(24, 82, 592, 40);
            banner.BackColor = card;
            bannerStripe.SetBounds(0, 0, 5, 40);
            banner.Controls.Add(bannerStripe);
            bannerText.SetBounds(16, 0, 570, 40);
            bannerText.TextAlign = ContentAlignment.MiddleLeft;
            banner.Controls.Add(bannerText);
            Controls.Add(banner);

            Panel game = AddSection("GAME", 136, 126);
            AddLabel(game, "Original Stick RPG 2 Director's Cut EXE (in this folder)", 14, 30, 560, 20);
            source.SetBounds(14, 54, 430, 27);
            source.ReadOnly = true;
            source.BackColor = input;
            source.ForeColor = ForeColor;
            source.BorderStyle = BorderStyle.FixedSingle;
            source.TabIndex = 0;
            game.Controls.Add(source);
            ConfigureButton(game, browse, "Choose EXE...", 454, 52, 124, 31, false);
            browse.TabIndex = 1;
            browse.Click += BrowseClicked;
            ConfigureButton(game, findSteam, "Find Steam copy", 14, 88, 140, 28, false);
            findSteam.TabIndex = 2;
            findSteam.Click += delegate { FindSource(true); };
            sourceStatus.SetBounds(164, 88, 414, 28);
            sourceStatus.TextAlign = ContentAlignment.MiddleLeft;
            sourceStatus.ForeColor = muted;
            game.Controls.Add(sourceStatus);

            Panel screen = AddSection("DISPLAY", 274, 116);
            AddLabel(screen, "Motion smoothness", 14, 30, 276, 20);
            AddLabel(screen, "Display mode", 304, 30, 274, 20);
            ConfigureCombo(screen, motion, 14, 52, 276);
            motion.Items.AddRange(new object[] { "30 FPS (original)", "60 FPS (recommended)", "120 FPS (experimental)" });
            motion.TabIndex = 3;
            motion.SelectedIndexChanged += delegate { UpdateNote(); };
            ConfigureCombo(screen, display, 304, 52, 274);
            display.Items.AddRange(new object[] { "Fullscreen", "Windowed (1600 x 900)" });
            display.TabIndex = 4;
            note.SetBounds(14, 86, 564, 22);
            note.ForeColor = muted;
            screen.Controls.Add(note);

            Panel graphics = AddSection("GRAPHICS", 402, 84);
            upscale.Text = "AI-upscaled icons, interface and backgrounds (recommended)";
            upscale.SetBounds(14, 30, 564, 24);
            upscale.FlatStyle = FlatStyle.Flat;
            upscale.ForeColor = ForeColor;
            upscale.Cursor = Cursors.Hand;
            upscale.TabIndex = 5;
            graphics.Controls.Add(upscale);
            AddLabel(graphics, "4x sharper art upscaled from the original game. Untick to keep the original bitmaps.", 32, 56, 546, 20).ForeColor = muted;

            status.SetBounds(24, 496, 592, 36);
            status.ForeColor = muted;
            Controls.Add(status);
            progress.SetBounds(24, 532, 592, 4);
            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 25;
            progress.Visible = false;
            Controls.Add(progress);
            ConfigureButton(this, restore, "Restore original EXE", 24, 544, 190, 36, false);
            restore.TabIndex = 6;
            restore.Click += RestoreClicked;
            ConfigureButton(this, play, "Save & install", 426, 542, 190, 40, true);
            play.TabIndex = 7;
            play.Click += PlayClicked;
            AcceptButton = play;
            Shown += delegate { ActiveControl = play; };
            FormClosing +=delegate(object sender, FormClosingEventArgs args) { if (busy) args.Cancel = true; };
            LauncherSettings settings = LauncherSettings.Load(root);
            motion.SelectedIndex = settings.Fps == 30 ? 0 : settings.Fps == 120 ? 2 : 1;
            display.SelectedIndex = settings.Fullscreen ? 0 : 1;
            upscale.Checked = settings.Upscale;
            if (!String.IsNullOrEmpty(originalArgument) && File.Exists(originalArgument)) SetSource(originalArgument, "Game copy selected.");
            else if (!String.IsNullOrEmpty(settings.OriginalExe) && File.Exists(Path.Combine(root, settings.OriginalExe))) SetSource(Path.Combine(root, settings.OriginalExe), "Saved game copy.");
            else FindSource(false);
            UpdateInstallationStatus();
            using (Graphics surface = CreateGraphics()) {
                float factor = surface.DpiX / 96F;
                if (Math.Abs(factor - 1F) > 0.01F) {
                    Scale(new SizeF(factor, factor));
                    motion.ItemHeight = display.ItemHeight = (int)Math.Round(22 * factor);  // owner-drawn rows
                }
            }
        }
        private Panel AddSection(string heading, int y, int height) {
            Panel section = new Panel { BackColor = card };
            section.SetBounds(24, y, 592, height);
            Label header = AddLabel(section, heading, 14, 8, 300, 18);
            header.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold);
            header.ForeColor = Color.FromArgb(110, 168, 245);
            Controls.Add(section);
            return section;
        }
        private Label AddLabel(Control parent, string text, int x, int y, int width, int height) {
            Label label = new Label { Text = text };
            label.SetBounds(x, y, width, height);
            parent.Controls.Add(label);
            return label;
        }
        private void ConfigureButton(Control parent, Button button, string text, int x, int y, int width, int height, bool primary) {
            button.Text = text;
            button.SetBounds(x, y, width, height);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(65, 149, 241) : Color.FromArgb(67, 79, 98);
            button.BackColor = primary ? Color.FromArgb(44, 115, 205) : input;
            button.ForeColor = ForeColor;
            button.Font = new Font(Font, primary ? FontStyle.Bold : FontStyle.Regular);
            button.Cursor = Cursors.Hand;
            parent.Controls.Add(button);
        }
        private void ConfigureCombo(Control parent, ComboBox combo, int x, int y, int width) {
            combo.SetBounds(x, y, width, 28);
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Flat;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 22;
            combo.BackColor = input;
            combo.ForeColor = ForeColor;
            combo.DrawItem += delegate(object sender, DrawItemEventArgs args) {
                if (args.Index < 0) return;
                Color background = (args.State & DrawItemState.Selected) != 0 ? Color.FromArgb(44, 115, 205) : combo.BackColor;
                using (SolidBrush brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, args.Bounds);
                Rectangle textBounds = new Rectangle(args.Bounds.X + 7, args.Bounds.Y, args.Bounds.Width - 10, args.Bounds.Height);
                TextRenderer.DrawText(args.Graphics, combo.GetItemText(combo.Items[args.Index]), combo.Font, textBounds, combo.ForeColor,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            parent.Controls.Add(combo);
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
            bannerStripe.BackColor = installed ? Color.FromArgb(70, 190, 120) : Color.FromArgb(232, 172, 64);
            bannerText.Text = installed ? "Mod installed. Start the game through Steam or with StickRPG4K.exe."
                                        : "Not installed yet. Extract the full ZIP beside the game EXE, then click Save & install.";
            status.Text = "";
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
            browse.Enabled = findSteam.Enabled = motion.Enabled = display.Enabled = upscale.Enabled = play.Enabled = !value;
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
                Fullscreen = display.SelectedIndex == 0, OriginalExe = source.Text, Upscale = upscale.Checked
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
                    if (Array.IndexOf(args, "--no-upscale") >= 0) settings.Upscale = false;
                    if (Array.IndexOf(args, "--upscale") >= 0) settings.Upscale = true;
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
