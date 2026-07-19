using System;
using System.Windows.Forms;
namespace MeshPatcherProject
{

    public class MainForm : Form
    {
        readonly TextBox _inputBox = new() { Left = 120, Width = 400 };
        readonly TextBox _settingsBox = new() { Left = 120, Width = 400 };
        readonly TextBox _outputBox = new() { Left = 120, Width = 400 };
        readonly CheckBox _dryRunBox = new() { Text = "Dry run (preview only, don't write files)", Left = 120, AutoSize = true, Checked = true };
        readonly Button _runButton = new() { Text = "Run", Left = 120, Width = 100, Height = 32 };
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
            Height = 480;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            AddRow("Input folder:", _inputBox, 20, () => BrowseFolder(_inputBox));
            AddRow("Settings.json:", _settingsBox, 55, () => BrowseFile(_settingsBox));
            AddRow("Output folder:", _outputBox, 90, () => BrowseFolder(_outputBox));

            _dryRunBox.Top = 130;
            Controls.Add(_dryRunBox);

            _runButton.Top = 165;
            _runButton.Click += RunButton_Click;
            Controls.Add(_runButton);

            _logBox.Top = 210;
            Controls.Add(_logBox);
        }

        void AddRow(string labelText, TextBox box, int top, Action browseAction)
        {
            var label = new Label { Text = labelText, Left = 12, Top = top + 3, Width = 100 };
            box.Top = top;
            var browse = new Button { Text = "Browse...", Left = 530, Top = top - 1, Width = 78 };
            browse.Click += (s, e) => browseAction();
            Controls.Add(label);
            Controls.Add(box);
            Controls.Add(browse);
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

            if (string.IsNullOrEmpty(input) || !Directory.Exists(input))
            {
                MessageBox.Show(this, "Please choose a valid input folder.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(settingsPath) || !File.Exists(settingsPath))
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

            try
            {
                // Run on a background thread so the UI (and the log box updating live) stays responsive.
                await Task.Run(() =>
                    MeshPatcherLogic.Run(input, settingsPath, output, dryRun, Log));
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
            }
        }
    }
}