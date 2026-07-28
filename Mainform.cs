using System;
using System.Windows.Forms;
namespace MeshPatcherProject
{

    public class MainForm : Form
    {
        readonly TextBox _inputBox = new() { Left = 120, Width = 400 };
        readonly TextBox _settingsBox = new() { Left = 120, Width = 400 };
        readonly TextBox _outputBox = new() { Left = 120, Width = 400 };
        readonly CheckBox _automaticModeBox = new() { Text = "Advanced mode (assign presets by folder/file name)", Left = 120, AutoSize = true, Checked = true };
        readonly CheckBox _dryRunBox = new() { Text = "Dry run (preview only, don't write files)", Left = 120, AutoSize = true, Checked = true };
        readonly CheckBox _envMapOnlyBox = new() { Text = "Only patch shapes with Environment Map shaders", Left = 120, AutoSize = true, Checked = true };
        readonly Button _runButton = new() { Text = "Run", Left = 120, Width = 100, Height = 32 };
        readonly Button _stopButton = new() { Text = "Stop", Left = 230, Width = 100, Height = 32, Enabled = false };
        readonly Button _managePresetsButton = new() { Text = "Manage Presets", Left = 345, Width = 150, Height = 32 };
        readonly Button _helpButton = new() { Left = 505, Width = 32, Height = 32 };
        readonly ToolTip _toolTip = new();
        Button? _settingsBrowseButton;
        CancellationTokenSource? _cts;
        readonly TextBox _logBox = new()
        {
            Left = 12,
            Width = 560,
            Height = 260,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 8.5f)
        };

        public MainForm()
        {
            Text = "Mesh Patcher";
            Width = 640;
            Height = 560;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // The csproj's <ApplicationIcon> sets the exe's Explorer/file icon, but the running
            // window/taskbar icon needs to be set here separately - WinForms doesn't pick up the
            // embedded exe icon for that automatically. Missing file shouldn't stop the app opening.
            try
            {
                Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Resources", "icon.ico"));
            }
            catch
            {
                // Falls back to the default WinForms icon.
            }

            AddRow("Input folder:", _inputBox, 20, () => BrowseFolder(_inputBox));
            _settingsBrowseButton = AddRow("Settings.json:", _settingsBox, 55, () => BrowseFile(_settingsBox));
            AddRow("Output folder:", _outputBox, 90, () => BrowseFolder(_outputBox));

            _automaticModeBox.Top = 128;
            _automaticModeBox.CheckedChanged += (s, e) => UpdateSettingsFieldEnabled();
            Controls.Add(_automaticModeBox);
            UpdateSettingsFieldEnabled();

            _envMapOnlyBox.Top = 151;
            Controls.Add(_envMapOnlyBox);

            _dryRunBox.Top = 174;
            Controls.Add(_dryRunBox);

            _runButton.Top = 205;
            _runButton.Click += RunButton_Click;
            Controls.Add(_runButton);

            _stopButton.Top = 205;
            _stopButton.Click += StopButton_Click;
            Controls.Add(_stopButton);

            _managePresetsButton.Top = 205;
            _managePresetsButton.Click += (s, e) =>
            {
                using var editor = new PresetEditorForm();
                editor.ShowDialog(this);
            };
            Controls.Add(_managePresetsButton);

            _helpButton.Top = 205;
            var helpIcon = IconLoader.LoadIcon("help.png", 20);
            if (helpIcon is not null)
                _helpButton.Image = helpIcon;
            else
                _helpButton.Text = "?";
            _toolTip.SetToolTip(_helpButton, "Help");
            _helpButton.Click += (s, e) => ShowHelp();
            Controls.Add(_helpButton);

            _logBox.Top = 245;
            Controls.Add(_logBox);
        }

        void ShowHelp()
        {
            MessageBox.Show(this,
                "Input folder: searched recursively for .nif files.\n\n" +
                "Settings.json: a single preset applied to every file. Only used when Automatic " +
                "mode is off.\n\n" +
                "Advanced mode: instead of one Settings.json, each file gets a preset chosen by " +
                "matching its folder names and file name against a preset name in the " +
                "Presets folder (managed via \"Manage Presets\"). The longest match wins." +
                "Files that match nothing use the 'default' preset.\n\n" +
                "Only patch shapes with Environment Map shaders: skips any shape whose shader type " +
                "isn't set to Environment Map, leaving everything else untouched.\n\n" +
                "Dry run: preview what would happen (shown in the log) without writing any files.\n\n" +
                "Stop: cancels the run after it finishes whatever file it's currently on.",
                "Mesh Patcher Help", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void UpdateSettingsFieldEnabled()
        {
            var automatic = _automaticModeBox.Checked;
            _settingsBox.Enabled = !automatic;
            _settingsBrowseButton!.Enabled = !automatic;
        }

        Button AddRow(string labelText, TextBox box, int top, Action browseAction)
        {
            var label = new Label { Text = labelText, Left = 12, Top = top + 3, Width = 100 };
            box.Top = top;
            var browse = new Button { Text = "Browse...", Left = 530, Top = top - 1, Width = 78 };
            browse.Click += (s, e) => browseAction();
            Controls.Add(label);
            Controls.Add(box);
            Controls.Add(browse);
            return browse;
        }

        void BrowseFolder(TextBox target)
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
                target.Text = dialog.SelectedPath;
        }

        void BrowseFile(TextBox target)
        {
            using var dialog = new OpenFileDialog { Filter = "Settings JSON (*.json)|*.json|All files (*.*)|*.*" };
            if (dialog.ShowDialog() == DialogResult.OK)
                target.Text = dialog.FileName;
        }

        void Log(string message)
        {
            if (_logBox.InvokeRequired)
            {
                _logBox.Invoke(new Action<string>(Log), message);
                return;
            }
            _logBox.AppendText(message + Environment.NewLine);
        }

        private void InitializeComponent()
        {

        }

        async void RunButton_Click(object? sender, EventArgs e)
        {
            var input = _inputBox.Text.Trim();
            var settingsPath = _settingsBox.Text.Trim();
            var output = _outputBox.Text.Trim();
            var dryRun = _dryRunBox.Checked;
            var environmentMapOnly = _envMapOnlyBox.Checked;
            var automaticMode = _automaticModeBox.Checked;

            if (string.IsNullOrEmpty(input) || !Directory.Exists(input))
            {
                MessageBox.Show(this, "Please choose a valid input folder.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!automaticMode && (string.IsNullOrEmpty(settingsPath) || !File.Exists(settingsPath)))
            {
                MessageBox.Show(this, "Please choose a valid Settings.json file.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(output))
            {
                MessageBox.Show(this, "Please choose an output folder.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _logBox.Clear();
            _runButton.Enabled = false;
            _runButton.Text = "Running...";
            _stopButton.Enabled = true;
            _stopButton.Text = "Stop";

            _cts = new CancellationTokenSource();

            try
            {
                // Run on a background thread so the UI (and the log box updating live) stays responsive.
                await Task.Run(() =>
                    MeshPatcherLogic.Run(input, settingsPath, output, dryRun, environmentMapOnly, automaticMode, _cts.Token, Log));
            }
            catch (Exception ex)
            {
                Log($"[FATAL] {ex.GetType().Name}: {ex.Message}");
                MessageBox.Show(this, $"Something went wrong:\n\n{ex.Message}", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _runButton.Enabled = true;
                _runButton.Text = "Run";
                _stopButton.Enabled = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        void StopButton_Click(object? sender, EventArgs e)
        {
            _cts?.Cancel();
            _stopButton.Enabled = false;
            _stopButton.Text = "Stopping...";
        }
    }
}