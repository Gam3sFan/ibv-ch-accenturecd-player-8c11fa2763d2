using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ContentDistributionPlayer.Utilities;

namespace ContentDistributionPlayer
{
    class SettingsForm : Form
    {
        private readonly string _configPath;
        private readonly Dictionary<string, Control> _fields = new Dictionary<string, Control>();
        private readonly RuntimeSettingsService _settingsService;
        private readonly RuntimeStatusSnapshot _status;
        private readonly AutoUpdateService _autoUpdateService;
        private readonly Action<string> _installUpdate;
        private Label _currentVersionLabel;
        private Label _updateStatusLabel;
        private Button _checkUpdateButton;

        private static readonly string[] SettingNames =
        {
            "NodeJSHost",
            "NodeJSPort",
            "NodeJSProtocol",
            "Room",
            "Monitor",
            "ContentsFolder",
            "UseFullScreen",
            "ScreenResolutionWidth",
            "ScreenResolutionHeight",
            "PurgePresentationData",
            "LogMinimumLevel",
            "AutoUpdateEnabled",
            "AutoUpdateManifestUrl"
        };

        public SettingsForm(RuntimeStatusSnapshot status = null, AutoUpdateService autoUpdateService = null, Action<string> installUpdate = null)
        {
            _status = status;
            _autoUpdateService = autoUpdateService ?? new AutoUpdateService();
            _installUpdate = installUpdate;
            _settingsService = new RuntimeSettingsService();
            _configPath = _settingsService.ConfigPath;

            Text = "Player settings";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(620, 600);
            Font = new Font("Segoe UI", 9F);

            BuildUi();
            LoadSettings();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(16)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var configLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 38,
                Text = _configPath,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 0,
                AutoScroll = true
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddTextRow(grid, "NodeJSHost", "NodeJS host");
            AddNumberRow(grid, "NodeJSPort", "NodeJS port");
            AddProtocolRow(grid);
            AddNumberRow(grid, "Room", "Room");
            AddNumberRow(grid, "Monitor", "Monitor");
            AddFolderRow(grid);
            AddBooleanRow(grid, "UseFullScreen", "Use full screen");
            AddNumberRow(grid, "ScreenResolutionWidth", "Screen width");
            AddNumberRow(grid, "ScreenResolutionHeight", "Screen height");
            AddBooleanRow(grid, "PurgePresentationData", "Purge presentation data");
            AddLogLevelRow(grid);
            AddBooleanRow(grid, "AutoUpdateEnabled", "Updates enabled");
            AddTextRow(grid, "AutoUpdateManifestUrl", "Auto-update manifest URL");

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(0, 14, 0, 0)
            };

            var saveButton = new Button
            {
                Text = "Save",
                Width = 96,
                DialogResult = DialogResult.None
            };
            saveButton.Click += (sender, args) => SaveSettings();

            var cancelButton = new Button
            {
                Text = "Cancel",
                Width = 96,
                DialogResult = DialogResult.Cancel
            };

            buttons.Controls.Add(saveButton);
            buttons.Controls.Add(cancelButton);

            root.Controls.Add(configLabel, 0, 0);
            root.Controls.Add(grid, 0, 1);
            root.Controls.Add(CreateUpdatePanel(), 0, 2);
            root.Controls.Add(CreateStatusPanel(), 0, 3);
            root.Controls.Add(buttons, 0, 4);

            Controls.Add(root);
            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        private void AddTextRow(TableLayoutPanel grid, string key, string label)
        {
            var textBox = new TextBox { Dock = DockStyle.Fill };
            AddRow(grid, key, label, textBox);
        }

        private void AddNumberRow(TableLayoutPanel grid, string key, string label)
        {
            var numeric = new NumericUpDown
            {
                Dock = DockStyle.Left,
                Width = 120,
                Minimum = 0,
                Maximum = 65535
            };
            AddRow(grid, key, label, numeric);
        }

        private void AddProtocolRow(TableLayoutPanel grid)
        {
            var combo = new ComboBox
            {
                Dock = DockStyle.Left,
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            combo.Items.AddRange(new object[] { "wss", "ws" });
            AddRow(grid, "NodeJSProtocol", "NodeJS protocol", combo);
        }

        private void AddLogLevelRow(TableLayoutPanel grid)
        {
            var combo = new ComboBox
            {
                Dock = DockStyle.Left,
                Width = 140,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            combo.Items.AddRange(new object[] { "Information", "Warning", "Error", "Verbose", "Off" });
            AddRow(grid, "LogMinimumLevel", "Log minimum level", combo);
        }

        private void AddBooleanRow(TableLayoutPanel grid, string key, string label)
        {
            var checkBox = new CheckBox { Dock = DockStyle.Left, AutoSize = true };
            if (key == "AutoUpdateEnabled")
                checkBox.CheckedChanged += (sender, args) => UpdateCheckButtonState();
            AddRow(grid, key, label, checkBox);
        }

        private void AddFolderRow(TableLayoutPanel grid)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var textBox = new TextBox { Dock = DockStyle.Fill };
            var browseButton = new Button { Text = "...", Width = 34, Dock = DockStyle.Right };
            browseButton.Click += (sender, args) =>
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.SelectedPath = Directory.Exists(textBox.Text) ? textBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        textBox.Text = dialog.SelectedPath;
                }
            };

            panel.Controls.Add(textBox, 0, 0);
            panel.Controls.Add(browseButton, 1, 0);
            _fields["ContentsFolder"] = textBox;
            AddLabeledControl(grid, "Contents folder", panel);
        }

        private void AddRow(TableLayoutPanel grid, string key, string label, Control control)
        {
            _fields[key] = control;
            AddLabeledControl(grid, label, control);
        }

        private void AddLabeledControl(TableLayoutPanel grid, string label, Control control)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var labelControl = new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 5, 8, 5)
            };

            control.Margin = new Padding(0, 5, 0, 5);

            grid.Controls.Add(labelControl, 0, row);
            grid.Controls.Add(control, 1, row);
        }

        private void LoadSettings()
        {
            var values = _settingsService.ReadAll();
            foreach (string key in SettingNames)
            {
                string value = values.ContainsKey(key) ? values[key] : string.Empty;
                SetControlValue(key, value);
            }

            UpdateCheckButtonState();
        }

        private void SaveSettings()
        {
            string validationError = ValidateFields();
            if (!string.IsNullOrEmpty(validationError))
            {
                MessageBox.Show(this, validationError, "Invalid settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var values = new Dictionary<string, string>();
            foreach (string key in SettingNames)
                values[key] = GetControlValue(key);

            _settingsService.Save(values);
            DialogResult = DialogResult.OK;
            Close();
        }

        private string ValidateFields()
        {
            if (string.IsNullOrWhiteSpace(GetControlValue("NodeJSHost")))
                return "NodeJS host is required.";

            if (GetNumericValue("NodeJSPort") <= 0)
                return "NodeJS port must be greater than zero.";

            string protocol = GetControlValue("NodeJSProtocol");
            if (protocol != "ws" && protocol != "wss")
                return "NodeJS protocol must be ws or wss.";

            if (GetNumericValue("Room") <= 0)
                return "Room must be greater than zero.";

            if (GetNumericValue("Monitor") <= 0)
                return "Monitor must be greater than zero.";

            if (!Directory.Exists(GetControlValue("ContentsFolder")))
                return "Contents folder must exist.";

            return null;
        }

        private Control CreateUpdatePanel()
        {
            var group = new GroupBox
            {
                Text = "Updates",
                Dock = DockStyle.Top,
                Height = 112,
                Padding = new Padding(10)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _currentVersionLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Current player version: " + (_status != null ? _status.AppVersion : MainForm.APP_VERSION)
            };

            _updateStatusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Update status: " + (_autoUpdateService != null ? _autoUpdateService.LastState : "Unavailable")
            };

            _checkUpdateButton = new Button
            {
                Text = "Check for updates",
                Width = 130,
                Height = 28,
                Dock = DockStyle.Right
            };
            _checkUpdateButton.Click += CheckUpdateButton_Click;

            layout.Controls.Add(_currentVersionLabel, 0, 0);
            layout.SetColumnSpan(_currentVersionLabel, 2);
            layout.Controls.Add(_updateStatusLabel, 0, 1);
            layout.Controls.Add(_checkUpdateButton, 1, 1);
            group.Controls.Add(layout);
            return group;
        }

        private async void CheckUpdateButton_Click(object sender, EventArgs e)
        {
            if (_autoUpdateService == null)
                return;

            if (!UpdatesEnabled)
            {
                MessageBox.Show(this, "Updates are disabled.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string manifestUrl = GetControlValue("AutoUpdateManifestUrl");
            string contentsFolder = GetControlValue("ContentsFolder");

            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                MessageBox.Show(this, "Auto-update manifest URL is required.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Directory.Exists(contentsFolder))
            {
                MessageBox.Show(this, "Contents folder must exist before checking updates.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _checkUpdateButton.Enabled = false;
            _updateStatusLabel.Text = "Update status: Checking...";

            try
            {
                string installScriptPath = await _autoUpdateService.CheckAndStageAsync(
                    _status != null ? _status.AppVersion : MainForm.APP_VERSION,
                    manifestUrl,
                    contentsFolder,
                    CancellationToken.None);

                _updateStatusLabel.Text = "Update status: " + _autoUpdateService.LastState;

                if (string.IsNullOrWhiteSpace(installScriptPath))
                {
                    MessageBox.Show(this, _autoUpdateService.LastState, "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var result = MessageBox.Show(
                    this,
                    "A new version has been downloaded and is ready to install. Install it now? The player will close and restart automatically.",
                    "Install update",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    _installUpdate?.Invoke(installScriptPath);
                    Close();
                }
            }
            finally
            {
                if (!IsDisposed)
                    UpdateCheckButtonState();
            }
        }

        private bool UpdatesEnabled
        {
            get
            {
                if (!_fields.ContainsKey("AutoUpdateEnabled"))
                    return true;

                return !(_fields["AutoUpdateEnabled"] is CheckBox checkBox) || checkBox.Checked;
            }
        }

        private void UpdateCheckButtonState()
        {
            if (_checkUpdateButton == null)
                return;

            _checkUpdateButton.Enabled = UpdatesEnabled;
            if (_updateStatusLabel != null && !UpdatesEnabled)
                _updateStatusLabel.Text = "Update status: Disabled";
            else if (_updateStatusLabel != null && _autoUpdateService != null)
                _updateStatusLabel.Text = "Update status: " + _autoUpdateService.LastState;
        }

        private Control CreateStatusPanel()
        {
            var group = new GroupBox
            {
                Text = "Status",
                Dock = DockStyle.Top,
                Height = 118,
                Padding = new Padding(10)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            if (_status != null)
            {
                AddStatusLabel(layout, "Endpoint", _status.NodeEndpoint);
                AddStatusLabel(layout, "Version", _status.AppVersion);
                AddStatusLabel(layout, "MQTT", _status.RtcConnected ? "Connected" : "Disconnected");
                AddStatusLabel(layout, "Client", _status.ClientIdentity);
                AddStatusLabel(layout, "Scene", string.Format("{0}/{1}", _status.SceneIndex, _status.SubSceneIndex));
                AddStatusLabel(layout, "DPI", string.Format("{0} ({1:0.##}x)", _status.DpiAwareness, _status.WindowsScaleFactor));
                AddStatusLabel(layout, "Update", _status.AutoUpdateState);
                AddStatusLabel(layout, "API", string.IsNullOrEmpty(_status.ApiUri) ? "-" : _status.ApiUri);
                AddStatusLabel(layout, "Config", _status.ConfigPath);
            }

            group.Controls.Add(layout);
            return group;
        }

        private void AddStatusLabel(TableLayoutPanel layout, string label, string value)
        {
            int row = layout.Controls.Count / 2;
            var item = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Text = label + ": " + value,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(item, row % 2, row / 2);
        }

        private void SetControlValue(string key, string value)
        {
            Control control = _fields[key];
            if (control is TextBox textBox)
                textBox.Text = value;
            else if (control is NumericUpDown numeric && decimal.TryParse(value, out decimal number))
                numeric.Value = Math.Max(numeric.Minimum, Math.Min(numeric.Maximum, number));
            else if (control is CheckBox checkBox && bool.TryParse(value, out bool flag))
                checkBox.Checked = flag;
            else if (control is ComboBox comboBox)
                comboBox.SelectedItem = string.IsNullOrEmpty(value) ? "wss" : value;
        }

        private string GetControlValue(string key)
        {
            Control control = _fields[key];
            if (control is TextBox textBox)
                return textBox.Text.Trim();
            if (control is NumericUpDown numeric)
                return ((int)numeric.Value).ToString();
            if (control is CheckBox checkBox)
                return checkBox.Checked.ToString();
            if (control is ComboBox comboBox)
                return (comboBox.SelectedItem ?? string.Empty).ToString();

            return string.Empty;
        }

        private int GetNumericValue(string key)
        {
            return int.TryParse(GetControlValue(key), out int value) ? value : 0;
        }
    }
}
