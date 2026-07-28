using System.Text.Json;
using SkyrimShaderPropertyFlags1 = NiflySharp.Enums.SkyrimShaderPropertyFlags1;
using SkyrimShaderPropertyFlags2 = NiflySharp.Enums.SkyrimShaderPropertyFlags2;

namespace MeshPatcherProject
{
    /// <summary>
    /// A CheckedListBox whose mouse-wheel scrolling moves exactly one row per notch. The stock
    /// control scrolls by SystemInformation.MouseWheelScrollLines rows per notch (usually 3),
    /// which reads as a smooth/fast multi-row jump - this makes it a deliberate, discrete step.
    /// </summary>
    public class SteppedCheckedListBox : CheckedListBox
    {
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Items.Count > 0)
            {
                var step = e.Delta > 0 ? -1 : 1;
                TopIndex = Math.Max(0, Math.Min(Items.Count - 1, TopIndex + step));
            }

            // Not calling base.OnMouseWheel(e) - that's what drives the default multi-row scroll.
            if (e is HandledMouseEventArgs handled)
                handled.Handled = true;
        }
    }

    /// <summary>
    /// Lets the user create, rename, delete, and edit the JSON preset files that automatic
    /// mode matches against. Flag checklists are populated via reflection off the real
    /// SkyrimShaderPropertyFlags1/2 enums rather than a hardcoded list, so they can never
    /// drift out of sync with whatever NiflySharp version is actually referenced.
    /// </summary>
    public class PresetEditorForm : Form
    {
        readonly string _presetsFolder;
        string? _currentKeyword;

        readonly ListBox _presetList = new();
        readonly Button _newButton = new() { Text = "New" };
        readonly Button _renameButton = new() { Text = "Rename" };
        readonly Button _duplicateButton = new() { Text = "Duplicate" };
        readonly Button _deleteButton = new() { Text = "Delete" };
        readonly Button _saveButton = new() { Text = "Save" };

        readonly Panel _detailPanel = new();

        readonly TextBox _presetNameBox = new() { Width = 300 };

        readonly TextBox _diffuseBox = new() { Width = 300 };
        readonly TextBox _normalBox = new() { Width = 300 };
        readonly TextBox _opacityBox = new() { Width = 300 };
        readonly TextBox _roughnessBox = new() { Width = 300 };
        readonly TextBox _metalBox = new() { Width = 300 };
        readonly TextBox _aoBox = new() { Width = 300 };
        readonly TextBox _heightBox = new() { Width = 300 };
        readonly TextBox _emissiveBox = new() { Width = 300 };
        readonly TextBox _transmissiveBox = new() { Width = 300 };

        readonly NumericUpDown _glossinessBox = new() { Width = 100, Minimum = 0, Maximum = 999, DecimalPlaces = 2, Increment = 1 };
        readonly NumericUpDown _specularStrengthBox = new() { Width = 100, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Increment = 0.05M };
        readonly NumericUpDown _lightingEffect1Box = new() { Width = 100, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Increment = 0.05M };
        readonly NumericUpDown _lightingEffect2Box = new() { Width = 100, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Increment = 0.05M };
        readonly NumericUpDown _environmentScaleBox = new() { Width = 100, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Increment = 0.05M };
        readonly Button _emissiveColorButton = new() { Width = 40, Height = 23, FlatStyle = FlatStyle.Popup };
        readonly TextBox _emissiveColorHexBox = new() { Width = 110 };
        readonly Button _emissiveColorResetButton = new() { Width = 28, Height = 23 };
        readonly NumericUpDown _emissiveMultipleBox = new() { Width = 100, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Increment = 0.05M };

        readonly SteppedCheckedListBox _flags1List = new() { CheckOnClick = true };
        readonly SteppedCheckedListBox _flags2List = new() { CheckOnClick = true };

        static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true };

        public PresetEditorForm()
        {
            _presetsFolder = Path.Combine(AppContext.BaseDirectory, "Presets");
            Directory.CreateDirectory(_presetsFolder);

            Text = "Manage Presets";
            Width = 780;
            Height = 590;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            _presetList.Left = 12;
            _presetList.Top = 12;
            _presetList.Width = 170;
            _presetList.Height = 388;
            _presetList.SelectedIndexChanged += (s, e) =>
            {
                if (_presetList.SelectedItem is string keyword)
                    LoadPresetIntoFields(keyword);
            };
            _presetList.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                {
                    DeleteCurrentPreset();
                    e.Handled = true;
                }
            };
            Controls.Add(_presetList);

            _newButton.Left = 12; _newButton.Top = 408; _newButton.Width = 82; _newButton.Height = 28;
            _newButton.Click += NewButton_Click;
            Controls.Add(_newButton);

            _renameButton.Left = 100; _renameButton.Top = 408; _renameButton.Width = 82; _renameButton.Height = 28;
            _renameButton.Click += RenameButton_Click;
            Controls.Add(_renameButton);

            _duplicateButton.Left = 12; _duplicateButton.Top = 440; _duplicateButton.Width = 170; _duplicateButton.Height = 28;
            _duplicateButton.Click += DuplicateButton_Click;
            Controls.Add(_duplicateButton);

            _deleteButton.Left = 12; _deleteButton.Top = 472; _deleteButton.Width = 82; _deleteButton.Height = 28;
            _deleteButton.Click += DeleteButton_Click;
            Controls.Add(_deleteButton);

            _saveButton.Left = 100; _saveButton.Top = 472; _saveButton.Width = 82; _saveButton.Height = 28;
            _saveButton.Click += SaveButton_Click;
            Controls.Add(_saveButton);

            foreach (var name in Enum.GetNames(typeof(SkyrimShaderPropertyFlags1)))
                _flags1List.Items.Add(name);
            foreach (var name in Enum.GetNames(typeof(SkyrimShaderPropertyFlags2)))
                _flags2List.Items.Add(name);

            _emissiveColorButton.Click += (s, e) =>
            {
                using var dialog = new ColorDialog { Color = _emissiveColorButton.BackColor, FullOpen = true };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    SetEmissiveColorButtonAppearance(dialog.Color);
            };

            _emissiveColorHexBox.Leave += (s, e) => TryApplyHexInput();
            _emissiveColorHexBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    TryApplyHexInput();
                    e.SuppressKeyPress = true;
                }
            };

            _emissiveColorResetButton.Click += (s, e) => SetEmissiveColorButtonAppearance(Color.Black);

            var iconErrors = new List<string>();

            var colorPickerIcon = IconLoader.LoadIcon("color.png", 16, iconErrors);
            if (colorPickerIcon is not null)
                _emissiveColorButton.Image = colorPickerIcon;

            var resetIcon = IconLoader.LoadIcon("reset.png", 14, iconErrors);
            if (resetIcon is not null)
                _emissiveColorResetButton.Image = resetIcon;
            else
                _emissiveColorResetButton.Text = "X";

            if (iconErrors.Count > 0)
                MessageBox.Show(this,
                    "Couldn't load one or more icons (falling back to text where needed):\n\n" + string.Join("\n\n", iconErrors),
                    "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            SetEmissiveColorButtonAppearance(Color.Black);

            _detailPanel.Left = 200;
            _detailPanel.Top = 12;
            _detailPanel.Width = 560;
            _detailPanel.Height = 530;
            _detailPanel.AutoScroll = true;
            _detailPanel.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_detailPanel);

            BuildDetailPanel();
            ClearFields();
            LoadPresetList();
        }

        void BuildDetailPanel()
        {
            int y = 8;

            AddField(_presetNameBox, "Preset Name:", ref y);

            y += 6;
            AddSectionHeader("Textures (game-relative paths, e.g. armor\\ironarmor\\ironarmor_d.dds)", ref y);
            AddField(_diffuseBox, "Diffuse:", ref y);
            AddField(_normalBox, "Normal:", ref y);
            AddField(_opacityBox, "Opacity:", ref y);
            AddField(_roughnessBox, "Roughness:", ref y);
            AddField(_metalBox, "Metal (cubemap):", ref y);
            AddField(_aoBox, "AO:", ref y);
            AddField(_heightBox, "Height:", ref y);
            AddField(_emissiveBox, "Emissive:", ref y);
            AddField(_transmissiveBox, "Transmissive (not yet applied):", ref y);

            y += 6;
            AddSectionHeader("Shader Values", ref y);
            y += 2;
            AddField(_glossinessBox, "Glossiness:", ref y);
            AddField(_specularStrengthBox, "Specular Strength:", ref y);
            AddField(_lightingEffect1Box, "Lighting Effect 1:", ref y);
            AddField(_lightingEffect2Box, "Lighting Effect 2:", ref y);
            AddField(_environmentScaleBox, "Environment Scale:", ref y);
            AddEmissiveColorRow(ref y);
            AddField(_emissiveMultipleBox, "Emissive Multiple:", ref y);

            y += 6;
            AddSectionHeader("Shader Flags", ref y);
            _detailPanel.Controls.Add(new Label { Text = "Flags1", Left = 8, Top = y, Width = 250 });
            y += 6;
            _detailPanel.Controls.Add(new Label { Text = "Flags2", Left = 270, Top = y - 4, Width = 250 });
            y += 6 + 14;

            _flags1List.Left = 8; _flags1List.Top = y; _flags1List.Width = 250; _flags1List.Height = 220;
            _flags2List.Left = 270; _flags2List.Top = y; _flags2List.Width = 250; _flags2List.Height = 220;
            _detailPanel.Controls.Add(_flags1List);
            _detailPanel.Controls.Add(_flags2List);
            y += 220 + 12; // 12px of breathing room after the flag lists

            // AutoScroll sizes the panel's scrollable content from its child controls' actual
            // bounds, not from this y tracker - so a trailing spacer is what actually reserves
            // the gap below the lists rather than just bookkeeping a number nothing reads.
            _detailPanel.Controls.Add(new Panel { Left = 8, Top = y, Width = 1, Height = 1 });
        }

        void AddEmissiveColorRow(ref int y)
        {
            var label = new Label { Text = "Emissive Color:", Left = 8, Top = y + 3, Width = 190 };
            _emissiveColorButton.Left = 200; _emissiveColorButton.Top = y;
            _emissiveColorHexBox.Left = 246; _emissiveColorHexBox.Top = y;
            _emissiveColorResetButton.Left = 362; _emissiveColorResetButton.Top = y;

            _detailPanel.Controls.Add(label);
            _detailPanel.Controls.Add(_emissiveColorButton);
            _detailPanel.Controls.Add(_emissiveColorHexBox);
            _detailPanel.Controls.Add(_emissiveColorResetButton);

            y += 28;
        }

        void AddSectionHeader(string text, ref int y)
        {
            _detailPanel.Controls.Add(new Label { Text = text, Left = 8, Top = y, Width = 500, Font = new Font(Font, FontStyle.Bold) });
            y += 22;
        }

        void AddField(Control field, string labelText, ref int y)
        {
            var label = new Label { Text = labelText, Left = 8, Top = y + 3, Width = 190 };
            field.Left = 200;
            field.Top = y;
            _detailPanel.Controls.Add(label);
            _detailPanel.Controls.Add(field);
            y += 28;
        }

        void SetEmissiveColorButtonAppearance(Color color)
        {
            _emissiveColorButton.BackColor = color;
            _emissiveColorHexBox.Text = ToHexString(color);
        }

        // ColorTranslator.ToHtml returns a color name ("Black", "Red", ...) instead of hex for any
        // color that matches a known named/system color - not what we want in a hex field.
        static string ToHexString(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        // Commits whatever's typed in the hex box: accepts "#RRGGBB" or "RRGGBB". On success, updates
        // the swatch (and re-normalizes the text). On failure, reverts the box to the swatch's current
        // color rather than leaving an invalid value sitting in the field.
        bool TryApplyHexInput()
        {
            var text = _emissiveColorHexBox.Text.Trim();
            if (!text.StartsWith('#'))
                text = "#" + text;

            try
            {
                var color = ColorTranslator.FromHtml(text);
                SetEmissiveColorButtonAppearance(color);
                return true;
            }
            catch
            {
                _emissiveColorHexBox.Text = ToHexString(_emissiveColorButton.BackColor);
                return false;
            }
        }

        string GetPresetPath(string keyword) => Path.Combine(_presetsFolder, keyword + ".json");

        static bool IsValidKeyword(string keyword) =>
            !string.IsNullOrWhiteSpace(keyword) && keyword.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        void LoadPresetList()
        {
            var previouslySelected = _currentKeyword;

            _presetList.Items.Clear();
            foreach (var file in Directory.EnumerateFiles(_presetsFolder, "*.json").OrderBy(f => f))
                _presetList.Items.Add(Path.GetFileNameWithoutExtension(file));

            if (previouslySelected is not null)
                SelectKeywordInList(previouslySelected);
        }

        void SelectKeywordInList(string keyword)
        {
            for (int i = 0; i < _presetList.Items.Count; i++)
            {
                if (((string)_presetList.Items[i]).Equals(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    _presetList.SelectedIndex = i;
                    return;
                }
            }
        }

        void LoadPresetIntoFields(string keyword)
        {
            Settings settings;
            try
            {
                settings = Settings.LoadFromFile(GetPresetPath(keyword));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Couldn't load '{keyword}.json': {ex.Message}", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _currentKeyword = keyword;

            _presetNameBox.Text = settings.PresetName;
            _diffuseBox.Text = settings.Textures.Diffuse;
            _normalBox.Text = settings.Textures.Normal;
            _opacityBox.Text = settings.Textures.Opacity;
            _roughnessBox.Text = settings.Textures.Roughness;
            _metalBox.Text = settings.Textures.Metal;
            _aoBox.Text = settings.Textures.AO;
            _heightBox.Text = settings.Textures.Height;
            _emissiveBox.Text = settings.Textures.Emissive;
            _transmissiveBox.Text = settings.Textures.Transmissive;

            _glossinessBox.Value = ClampToRange(_glossinessBox, (decimal)settings.Shader.Glossiness);
            _specularStrengthBox.Value = ClampToRange(_specularStrengthBox, (decimal)settings.Shader.SpecularStrength);
            _lightingEffect1Box.Value = ClampToRange(_lightingEffect1Box, (decimal)settings.Shader.LightingEffect1);
            _lightingEffect2Box.Value = ClampToRange(_lightingEffect2Box, (decimal)settings.Shader.LightingEffect2);
            _environmentScaleBox.Value = ClampToRange(_environmentScaleBox, (decimal)settings.Shader.EnvironmentScale);

            Color emissiveColor;
            try
            {
                emissiveColor = ColorTranslator.FromHtml(settings.Shader.EmissiveColor);
            }
            catch
            {
                emissiveColor = Color.Black;
            }
            SetEmissiveColorButtonAppearance(emissiveColor);
            _emissiveMultipleBox.Value = ClampToRange(_emissiveMultipleBox, (decimal)settings.Shader.EmissiveMultiple);

            SetCheckedFlags(_flags1List, settings.Flags1);
            SetCheckedFlags(_flags2List, settings.Flags2);
        }

        static decimal ClampToRange(NumericUpDown box, decimal value) => Math.Max(box.Minimum, Math.Min(box.Maximum, value));

        void SetCheckedFlags(CheckedListBox list, List<string> flagNames)
        {
            var remaining = new HashSet<string>(flagNames, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < list.Items.Count; i++)
            {
                var name = (string)list.Items[i];
                list.SetItemChecked(i, remaining.Contains(name));
                remaining.Remove(name);
            }

            if (remaining.Count > 0)
                MessageBox.Show(this,
                    $"This preset lists flag(s) that don't match a currently known name and were left unchecked: {string.Join(", ", remaining)}",
                    "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void ClearFields()
        {
            _currentKeyword = null;

            _presetNameBox.Clear();
            _diffuseBox.Clear(); _normalBox.Clear(); _opacityBox.Clear(); _roughnessBox.Clear();
            _metalBox.Clear(); _aoBox.Clear(); _heightBox.Clear(); _emissiveBox.Clear();
            _transmissiveBox.Clear();

            _glossinessBox.Value = _glossinessBox.Minimum;
            _specularStrengthBox.Value = _specularStrengthBox.Minimum;
            _lightingEffect1Box.Value = _lightingEffect1Box.Minimum;
            _lightingEffect2Box.Value = _lightingEffect2Box.Minimum;
            _environmentScaleBox.Value = _environmentScaleBox.Minimum;
            SetEmissiveColorButtonAppearance(Color.Black);
            _emissiveMultipleBox.Value = _emissiveMultipleBox.Minimum;

            for (int i = 0; i < _flags1List.Items.Count; i++) _flags1List.SetItemChecked(i, false);
            for (int i = 0; i < _flags2List.Items.Count; i++) _flags2List.SetItemChecked(i, false);
        }

        Settings GatherFieldsIntoSettings()
        {
            TryApplyHexInput();

            return new()
            {
                PresetName = _presetNameBox.Text.Trim(),
                Textures = new TextureSettings
                {
                    Diffuse = _diffuseBox.Text.Trim(),
                    Normal = _normalBox.Text.Trim(),
                    Opacity = _opacityBox.Text.Trim(),
                    Roughness = _roughnessBox.Text.Trim(),
                    Metal = _metalBox.Text.Trim(),
                    AO = _aoBox.Text.Trim(),
                    Height = _heightBox.Text.Trim(),
                    Emissive = _emissiveBox.Text.Trim(),
                    Transmissive = _transmissiveBox.Text.Trim(),
                },
                Shader = new ShaderSettings
                {
                    Glossiness = (float)_glossinessBox.Value,
                    SpecularStrength = (float)_specularStrengthBox.Value,
                    LightingEffect1 = (float)_lightingEffect1Box.Value,
                    LightingEffect2 = (float)_lightingEffect2Box.Value,
                    EnvironmentScale = (float)_environmentScaleBox.Value,
                    EmissiveColor = ToHexString(_emissiveColorButton.BackColor),
                    EmissiveMultiple = (float)_emissiveMultipleBox.Value,
                },
                Flags1 = _flags1List.CheckedItems.Cast<string>().ToList(),
                Flags2 = _flags2List.CheckedItems.Cast<string>().ToList(),
            };
        }

        static void SaveSettingsToFile(Settings settings, string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(settings, SaveOptions));

        void NewButton_Click(object? sender, EventArgs e)
        {
            var keyword = PromptForText(this, "New Preset",
                "Preset keyword (this becomes the file name, and the substring automatic mode matches against folder/file names):");
            if (keyword is null)
                return;

            if (!IsValidKeyword(keyword))
            {
                MessageBox.Show(this, "That's not a valid file name.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var path = GetPresetPath(keyword);
            if (File.Exists(path))
            {
                MessageBox.Show(this, $"A preset named '{keyword}' already exists.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveSettingsToFile(new Settings { PresetName = keyword }, path);
            LoadPresetList();
            SelectKeywordInList(keyword);
        }

        void RenameButton_Click(object? sender, EventArgs e)
        {
            if (_currentKeyword is null)
                return;

            if (_currentKeyword.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this,
                    "The 'default' preset can't be renamed - automatic mode requires a preset with exactly that name as its fallback.",
                    "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var newKeyword = PromptForText(this, "Rename Preset", "New keyword:", _currentKeyword);
            if (newKeyword is null || newKeyword.Equals(_currentKeyword, StringComparison.OrdinalIgnoreCase))
                return;

            if (!IsValidKeyword(newKeyword))
            {
                MessageBox.Show(this, "That's not a valid file name.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var newPath = GetPresetPath(newKeyword);
            if (File.Exists(newPath))
            {
                MessageBox.Show(this, $"A preset named '{newKeyword}' already exists.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var oldPath = GetPresetPath(_currentKeyword);
            SaveSettingsToFile(GatherFieldsIntoSettings(), newPath);
            File.Delete(oldPath);

            LoadPresetList();
            SelectKeywordInList(newKeyword);
        }

        void DuplicateButton_Click(object? sender, EventArgs e)
        {
            if (_currentKeyword is null)
            {
                MessageBox.Show(this, "Select a preset to duplicate first.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var newKeyword = PromptForText(this, "Duplicate Preset",
                $"New keyword for the copy of '{_currentKeyword}':", $"{_currentKeyword}_copy");
            if (newKeyword is null)
                return;

            if (!IsValidKeyword(newKeyword))
            {
                MessageBox.Show(this, "That's not a valid file name.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var newPath = GetPresetPath(newKeyword);
            if (File.Exists(newPath))
            {
                MessageBox.Show(this, $"A preset named '{newKeyword}' already exists.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Duplicates whatever's currently in the form (including any unsaved edits), not
            // necessarily what's still on disk for the original - same approach Rename uses.
            SaveSettingsToFile(GatherFieldsIntoSettings(), newPath);

            LoadPresetList();
            SelectKeywordInList(newKeyword);
        }

        void DeleteButton_Click(object? sender, EventArgs e) => DeleteCurrentPreset();

        void DeleteCurrentPreset()
        {
            if (_currentKeyword is null)
                return;

            if (_currentKeyword.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this,
                    "The 'default' preset can't be deleted - automatic mode requires it as its fallback.",
                    "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(this, $"Delete preset '{_currentKeyword}'? This can't be undone.",
                "Mesh Patcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            File.Delete(GetPresetPath(_currentKeyword));
            ClearFields();
            LoadPresetList();
        }

        void SaveButton_Click(object? sender, EventArgs e)
        {
            if (_currentKeyword is null)
            {
                MessageBox.Show(this, "Select or create a preset first.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveSettingsToFile(GatherFieldsIntoSettings(), GetPresetPath(_currentKeyword));
            MessageBox.Show(this, $"Saved '{_currentKeyword}.json'.", "Mesh Patcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static string? PromptForText(IWin32Window owner, string title, string prompt, string initialValue = "")
        {
            using var dialog = new Form
            {
                Text = title,
                Width = 380,
                Height = 160,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
            };

            var label = new Label { Text = prompt, Left = 12, Top = 12, Width = 340, Height = 40 };
            var textBox = new TextBox { Left = 12, Top = 56, Width = 340, Text = initialValue };
            var okButton = new Button { Text = "OK", Left = 195, Top = 88, Width = 75, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "Cancel", Left = 277, Top = 88, Width = 75, DialogResult = DialogResult.Cancel };

            dialog.Controls.Add(label);
            dialog.Controls.Add(textBox);
            dialog.Controls.Add(okButton);
            dialog.Controls.Add(cancelButton);
            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;

            return dialog.ShowDialog(owner) == DialogResult.OK ? textBox.Text.Trim() : null;
        }
    }
}