using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace HardwareMonitorOverlay
{
    internal sealed class SettingsForm : Form
    {
        private readonly OverlaySettings _settings;
        private RadioButton _transparentRadio;
        private RadioButton _solidRadio;
        private RadioButton _imageRadio;
        private ColorSwatchPanel _backgroundPreview;
        private Button _backgroundColorButton;
        private TextBox _imagePathBox;
        private Button _browseButton;
        private Button _clearImageButton;
        private TrackBar _backgroundOpacityBar;
        private Label _backgroundOpacityLabel;
        private ComboBox _fontFamilyBox;
        private Button _importFontButton;
        private NumericUpDown _fontSizeBox;
        private ColorSwatchPanel _fillPreview;
        private ColorSwatchPanel _innerPreview;
        private ColorSwatchPanel _outerPreview;
        private PrivateFontCollection _privateFonts;
        private CheckBox _clickThroughBox;
        private RadioButton _horizontalRadio;
        private RadioButton _verticalRadio;
        private TrackBar _spacingBar;
        private Label _spacingLabel;
        private TrackBar _textOpacityBar;
        private Label _textOpacityLabel;
        private TextBox _hotkeyBox;
        private string _hotkeyText = string.Empty;
        private readonly Action<OverlaySettings> _preview;
        private int _suppressPreview;
        private readonly HashSet<string> _importedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public OverlaySettings ResultSettings
        {
            get { return _settings; }
        }

        public SettingsForm(OverlaySettings settings, Action<OverlaySettings> preview)
        {
            _settings = settings.Clone();
            _preview = preview;
            Text = "设置 - 硬件温度与占用监控";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            ClientSize = new Size(520, 810);
            BackColor = Color.FromArgb(246, 247, 250);
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            BuildUI();
            LoadFromSettings();
            FormClosed += delegate
            {
                if (_privateFonts != null)
                {
                    _privateFonts.Dispose();
                    _privateFonts = null;
                }
            };
        }

        private void BeginSuppressPreview()
        {
            _suppressPreview++;
        }

        private void EndSuppressPreview()
        {
            if (_suppressPreview > 0) _suppressPreview--;
        }

        private void NotifyPreview()
        {
            if (_suppressPreview > 0 || _preview == null) return;
            ReadFromUI();
            _preview(_settings.Clone());
        }

        private void BuildUI()
        {
            GroupBox backgroundGroup = new GroupBox();
            backgroundGroup.Text = "背景";
            backgroundGroup.Location = new Point(12, 12);
            backgroundGroup.Size = new Size(496, 250);
            Controls.Add(backgroundGroup);

            _transparentRadio = new RadioButton();
            _transparentRadio.Text = "透明背景";
            _transparentRadio.Location = new Point(16, 26);
            _transparentRadio.AutoSize = true;
            _transparentRadio.CheckedChanged += delegate { UpdateControlState(); NotifyPreview(); };
            backgroundGroup.Controls.Add(_transparentRadio);

            _solidRadio = new RadioButton();
            _solidRadio.Text = "纯色背景";
            _solidRadio.Location = new Point(16, 54);
            _solidRadio.AutoSize = true;
            _solidRadio.CheckedChanged += delegate { UpdateControlState(); NotifyPreview(); };
            backgroundGroup.Controls.Add(_solidRadio);

            _imageRadio = new RadioButton();
            _imageRadio.Text = "图片背景";
            _imageRadio.Location = new Point(16, 82);
            _imageRadio.AutoSize = true;
            _imageRadio.CheckedChanged += delegate { UpdateControlState(); NotifyPreview(); };
            backgroundGroup.Controls.Add(_imageRadio);

            Label colorLabel = new Label();
            colorLabel.Text = "背景颜色";
            colorLabel.Location = new Point(38, 118);
            colorLabel.AutoSize = true;
            backgroundGroup.Controls.Add(colorLabel);

            _backgroundPreview = new ColorSwatchPanel();
            _backgroundPreview.Location = new Point(112, 114);
            _backgroundPreview.Size = new Size(70, 28);
            backgroundGroup.Controls.Add(_backgroundPreview);

            _backgroundColorButton = new Button();
            _backgroundColorButton.Text = "选择颜色...";
            _backgroundColorButton.Location = new Point(192, 112);
            _backgroundColorButton.Size = new Size(120, 30);
            _backgroundColorButton.Click += delegate { PickColor(_settings.BackgroundColor, "选择背景颜色", delegate(Color c) { _settings.BackgroundColor = c; UpdatePreviews(); }); };
            backgroundGroup.Controls.Add(_backgroundColorButton);

            Label imageLabel = new Label();
            imageLabel.Text = "背景图片";
            imageLabel.Location = new Point(38, 156);
            imageLabel.AutoSize = true;
            backgroundGroup.Controls.Add(imageLabel);

            _imagePathBox = new TextBox();
            _imagePathBox.Location = new Point(112, 152);
            _imagePathBox.Size = new Size(250, 24);
            _imagePathBox.ReadOnly = true;
            backgroundGroup.Controls.Add(_imagePathBox);

            _browseButton = new Button();
            _browseButton.Text = "浏览...";
            _browseButton.Location = new Point(370, 150);
            _browseButton.Size = new Size(70, 28);
            _browseButton.Click += delegate { BrowseImage(); };
            backgroundGroup.Controls.Add(_browseButton);

            _clearImageButton = new Button();
            _clearImageButton.Text = "清除";
            _clearImageButton.Location = new Point(446, 150);
            _clearImageButton.Size = new Size(34, 28);
            _clearImageButton.Click += delegate { _settings.BackgroundImagePath = string.Empty; _imagePathBox.Text = string.Empty; NotifyPreview(); };
            backgroundGroup.Controls.Add(_clearImageButton);

            Label opacityLabel = new Label();
            opacityLabel.Text = "背景透明度";
            opacityLabel.Location = new Point(38, 198);
            opacityLabel.AutoSize = true;
            backgroundGroup.Controls.Add(opacityLabel);

            _backgroundOpacityBar = new TrackBar();
            _backgroundOpacityBar.Minimum = 0;
            _backgroundOpacityBar.Maximum = 100;
            _backgroundOpacityBar.TickFrequency = 10;
            _backgroundOpacityBar.LargeChange = 5;
            _backgroundOpacityBar.SmallChange = 1;
            _backgroundOpacityBar.Location = new Point(112, 190);
            _backgroundOpacityBar.Size = new Size(280, 45);
            _backgroundOpacityBar.ValueChanged += delegate { _backgroundOpacityLabel.Text = _backgroundOpacityBar.Value + "%"; NotifyPreview(); };
            backgroundGroup.Controls.Add(_backgroundOpacityBar);

            _backgroundOpacityLabel = new Label();
            _backgroundOpacityLabel.Location = new Point(398, 198);
            _backgroundOpacityLabel.Size = new Size(60, 20);
            _backgroundOpacityLabel.TextAlign = ContentAlignment.MiddleLeft;
            backgroundGroup.Controls.Add(_backgroundOpacityLabel);

            GroupBox layoutGroup = new GroupBox();
            layoutGroup.Text = "布局与快捷键";
            layoutGroup.Location = new Point(12, 274);
            layoutGroup.Size = new Size(496, 150);
            Controls.Add(layoutGroup);

            Label orientationLabel = new Label();
            orientationLabel.Text = "排列方式";
            orientationLabel.Location = new Point(20, 26);
            orientationLabel.AutoSize = true;
            layoutGroup.Controls.Add(orientationLabel);

            _horizontalRadio = new RadioButton();
            _horizontalRadio.Text = "横向单行";
            _horizontalRadio.Location = new Point(112, 22);
            _horizontalRadio.AutoSize = true;
            _horizontalRadio.CheckedChanged += delegate { NotifyPreview(); };
            layoutGroup.Controls.Add(_horizontalRadio);

            _verticalRadio = new RadioButton();
            _verticalRadio.Text = "竖向多行";
            _verticalRadio.Location = new Point(232, 22);
            _verticalRadio.AutoSize = true;
            _verticalRadio.CheckedChanged += delegate { NotifyPreview(); };
            layoutGroup.Controls.Add(_verticalRadio);

            Label spacingLabel = new Label();
            spacingLabel.Text = "间距";
            spacingLabel.Location = new Point(20, 66);
            spacingLabel.AutoSize = true;
            layoutGroup.Controls.Add(spacingLabel);

            _spacingBar = new TrackBar();
            _spacingBar.Minimum = 0;
            _spacingBar.Maximum = 24;
            _spacingBar.TickFrequency = 2;
            _spacingBar.SmallChange = 1;
            _spacingBar.LargeChange = 2;
            _spacingBar.Location = new Point(112, 54);
            _spacingBar.Size = new Size(300, 40);
            _spacingBar.ValueChanged += delegate { _spacingLabel.Text = _spacingBar.Value + " px"; NotifyPreview(); };
            layoutGroup.Controls.Add(_spacingBar);

            _spacingLabel = new Label();
            _spacingLabel.Location = new Point(420, 66);
            _spacingLabel.Size = new Size(56, 20);
            _spacingLabel.TextAlign = ContentAlignment.MiddleLeft;
            layoutGroup.Controls.Add(_spacingLabel);

            Label hotkeyLabel = new Label();
            hotkeyLabel.Text = "移动快捷键";
            hotkeyLabel.Location = new Point(20, 110);
            hotkeyLabel.AutoSize = true;
            layoutGroup.Controls.Add(hotkeyLabel);

            _hotkeyBox = new TextBox();
            _hotkeyBox.Location = new Point(112, 106);
            _hotkeyBox.Size = new Size(150, 24);
            _hotkeyBox.ReadOnly = true;
            _hotkeyBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
                {
                    _hotkeyText = string.Empty;
                    _hotkeyBox.Text = "未设置";
                    NotifyPreview();
                    return;
                }
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) return;
                if (e.Modifiers == Keys.None) return;
                string text = HotkeySetting.Format(e.KeyCode, e.Modifiers);
                if (string.IsNullOrEmpty(text)) return;
                _hotkeyText = text;
                _hotkeyBox.Text = text;
                NotifyPreview();
            };
            layoutGroup.Controls.Add(_hotkeyBox);

            Button clearHotkeyButton = new Button();
            clearHotkeyButton.Text = "清除";
            clearHotkeyButton.Location = new Point(270, 104);
            clearHotkeyButton.Size = new Size(56, 26);
            clearHotkeyButton.Click += delegate { _hotkeyText = string.Empty; _hotkeyBox.Text = "未设置"; NotifyPreview(); };
            layoutGroup.Controls.Add(clearHotkeyButton);

            Label hotkeyHint = new Label();
            hotkeyHint.Text = "按下组合键（需含 Ctrl 等）";
            hotkeyHint.ForeColor = Color.FromArgb(90, 90, 90);
            hotkeyHint.Font = new Font(Font.FontFamily, 8.5f);
            hotkeyHint.Location = new Point(334, 110);
            hotkeyHint.AutoSize = true;
            layoutGroup.Controls.Add(hotkeyHint);

            GroupBox textGroup = new GroupBox();
            textGroup.Text = "文字";
            textGroup.Location = new Point(12, 436);
            textGroup.Size = new Size(496, 316);
            Controls.Add(textGroup);

            Label fontLabel = new Label();
            fontLabel.Text = "字体";
            fontLabel.Location = new Point(38, 30);
            fontLabel.AutoSize = true;
            textGroup.Controls.Add(fontLabel);

            _fontFamilyBox = new ComboBox();
            _fontFamilyBox.Location = new Point(112, 26);
            _fontFamilyBox.Size = new Size(250, 26);
            _fontFamilyBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _fontFamilyBox.SelectedIndexChanged += delegate { NotifyPreview(); };
            textGroup.Controls.Add(_fontFamilyBox);

            _importFontButton = new Button();
            _importFontButton.Text = "导入字体...";
            _importFontButton.Location = new Point(370, 24);
            _importFontButton.Size = new Size(110, 30);
            _importFontButton.Click += delegate { ImportFont(); };
            textGroup.Controls.Add(_importFontButton);

            Label fontSizeLabel = new Label();
            fontSizeLabel.Text = "字体大小";
            fontSizeLabel.Location = new Point(38, 70);
            fontSizeLabel.AutoSize = true;
            textGroup.Controls.Add(fontSizeLabel);

            _fontSizeBox = new NumericUpDown();
            _fontSizeBox.Location = new Point(112, 66);
            _fontSizeBox.Size = new Size(90, 24);
            _fontSizeBox.Minimum = 4m;
            _fontSizeBox.Maximum = 48m;
            _fontSizeBox.DecimalPlaces = 1;
            _fontSizeBox.Increment = 0.5m;
            _fontSizeBox.ValueChanged += delegate { NotifyPreview(); };
            textGroup.Controls.Add(_fontSizeBox);

            Label ptLabel = new Label();
            ptLabel.Text = "pt（4–48）";
            ptLabel.Location = new Point(208, 70);
            ptLabel.AutoSize = true;
            textGroup.Controls.Add(ptLabel);

            AddColorRow(textGroup, "文字填充", 110, out _fillPreview, delegate { PickColor(_settings.FillColor, "选择文字填充颜色", delegate(Color c) { _settings.FillColor = c; UpdatePreviews(); }); });
            AddColorRow(textGroup, "内圈颜色", 148, out _innerPreview, delegate { PickColor(_settings.InnerOutlineColor, "选择内圈颜色", delegate(Color c) { _settings.InnerOutlineColor = c; UpdatePreviews(); }); });
            AddColorRow(textGroup, "外圈颜色", 186, out _outerPreview, delegate { PickColor(_settings.OuterOutlineColor, "选择外圈颜色", delegate(Color c) { _settings.OuterOutlineColor = c; UpdatePreviews(); }); });
            Label textOpacityLabel = new Label();
            textOpacityLabel.Text = "字体透明度";
            textOpacityLabel.Location = new Point(38, 226);
            textOpacityLabel.AutoSize = true;
            textGroup.Controls.Add(textOpacityLabel);

            _textOpacityBar = new TrackBar();
            _textOpacityBar.Minimum = 0;
            _textOpacityBar.Maximum = 100;
            _textOpacityBar.TickFrequency = 10;
            _textOpacityBar.LargeChange = 5;
            _textOpacityBar.SmallChange = 1;
            _textOpacityBar.Location = new Point(112, 214);
            _textOpacityBar.Size = new Size(280, 40);
            _textOpacityBar.ValueChanged += delegate { _textOpacityLabel.Text = _textOpacityBar.Value + "%"; NotifyPreview(); };
            textGroup.Controls.Add(_textOpacityBar);

            _textOpacityLabel = new Label();
            _textOpacityLabel.Location = new Point(398, 226);
            _textOpacityLabel.Size = new Size(60, 20);
            _textOpacityLabel.TextAlign = ContentAlignment.MiddleLeft;
            textGroup.Controls.Add(_textOpacityLabel);

            Label hint = new Label();
            hint.Text = "颜色选择器支持常用调色板、屏幕取色，并可调整透明度。";
            hint.ForeColor = Color.FromArgb(90, 90, 90);
            hint.Location = new Point(38, 262);
            hint.Size = new Size(430, 22);
            textGroup.Controls.Add(hint);
            _clickThroughBox = new CheckBox();
            _clickThroughBox.Text = "鼠标穿透（不挡住游戏点击）";
            _clickThroughBox.Location = new Point(36, 288);
            _clickThroughBox.AutoSize = true;
            _clickThroughBox.CheckedChanged += delegate { NotifyPreview(); };
            textGroup.Controls.Add(_clickThroughBox);

            Button resetButton = new Button();
            resetButton.Text = "恢复默认";
            resetButton.Location = new Point(16, 766);
            resetButton.Size = new Size(100, 32);
            resetButton.Click += delegate { ResetSettings(); };
            Controls.Add(resetButton);

            Button cancelButton = new Button();
            cancelButton.Text = "取消";
            cancelButton.Location = new Point(310, 766);
            cancelButton.Size = new Size(86, 32);
            cancelButton.DialogResult = DialogResult.Cancel;
            Controls.Add(cancelButton);
            CancelButton = cancelButton;

            Button okButton = new Button();
            okButton.Text = "确定";
            okButton.Location = new Point(406, 766);
            okButton.Size = new Size(86, 32);
            okButton.Click += delegate
            {
                ReadFromUI();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(okButton);
            AcceptButton = okButton;
        }

        private void AddColorRow(GroupBox group, string labelText, int y, out ColorSwatchPanel preview, EventHandler clickHandler)
        {
            Label label = new Label();
            label.Text = labelText;
            label.Location = new Point(38, y + 4);
            label.AutoSize = true;
            group.Controls.Add(label);

            ColorSwatchPanel panel = new ColorSwatchPanel();
            panel.Location = new Point(112, y);
            panel.Size = new Size(70, 28);
            group.Controls.Add(panel);
            preview = panel;

            Button button = new Button();
            button.Text = "选择颜色...";
            button.Location = new Point(192, y - 2);
            button.Size = new Size(120, 30);
            button.Click += clickHandler;
            group.Controls.Add(button);
        }

        private void PickColor(Color initial, string title, Action<Color> apply)
        {
            using (ColorPickerDialog dialog = new ColorPickerDialog(initial, title))
            {
                dialog.TopMost = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    apply(dialog.SelectedColor);
                    NotifyPreview();
                }
            }
        }

        private void BrowseImage()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择背景图片";
                dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string extension = Path.GetExtension(dialog.FileName);
                    string destination = SettingsStore.GetBackgroundImagePath(extension);
                    if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        File.Copy(dialog.FileName, destination, true);
                    _settings.BackgroundImagePath = destination;
                    _imagePathBox.Text = destination;
                    _imageRadio.Checked = true;
                    NotifyPreview();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "无法读取图片：\r\n" + ex.Message, "选择图片", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void ImportFont()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入字体";
                dialog.Filter = "字体文件|*.ttf;*.otf;*.ttc|所有文件|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string fileName = Path.GetFileName(dialog.FileName);
                    string destination = SettingsStore.GetFontPath(fileName);
                    if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        File.Copy(dialog.FileName, destination, true);

                    PrivateFontCollection collection = new PrivateFontCollection();
                    collection.AddFontFile(destination);
                    if (collection.Families.Length == 0)
                    {
                        collection.Dispose();
                        MessageBox.Show(this, "这个字体文件没有可用的字体族。", "导入字体", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (_privateFonts != null)
                    {
                        _privateFonts.Dispose();
                        _privateFonts = null;
                    }
                    _privateFonts = collection;
                    _settings.FontFilePath = destination;
                    PopulateFontFamilies(destination);
                    string first = collection.Families[0].Name;
                    _settings.FontFamilyName = first;
                    SelectFontFamily(first);
                    NotifyPreview();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "无法导入字体：\r\n" + ex.Message, "导入字体", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void PopulateFontFamilies(string importPath)
        {
            BeginSuppressPreview();
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _fontFamilyBox.BeginUpdate();
            _fontFamilyBox.Items.Clear();
            _importedFamilies.Clear();

            foreach (FontFamily family in FontFamily.Families)
            {
                if (names.Add(family.Name)) _fontFamilyBox.Items.Add(family.Name);
            }

            if (!string.IsNullOrEmpty(importPath) && File.Exists(importPath))
            {
                try
                {
                    if (_privateFonts == null)
                    {
                        _privateFonts = new PrivateFontCollection();
                        _privateFonts.AddFontFile(importPath);
                    }
                    foreach (FontFamily family in _privateFonts.Families)
                    {
                        if (names.Add(family.Name)) _fontFamilyBox.Items.Add(family.Name);
                        _importedFamilies.Add(family.Name);
                    }
                }
                catch
                {
                    _settings.FontFilePath = string.Empty;
                }
            }
            else if (!string.IsNullOrEmpty(importPath))
            {
                _settings.FontFilePath = string.Empty;
            }

            _fontFamilyBox.EndUpdate();

            string select = _settings.FontFamilyName;
            if (string.IsNullOrEmpty(select) || !_fontFamilyBox.Items.Contains(select))
                select = _fontFamilyBox.Items.Contains("Microsoft YaHei UI") ? "Microsoft YaHei UI" : (_fontFamilyBox.Items.Count > 0 ? (string)_fontFamilyBox.Items[0] : string.Empty);
            SelectFontFamily(select);
            EndSuppressPreview();
        }

        private void SelectFontFamily(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            int index = _fontFamilyBox.FindStringExact(name);
            if (index >= 0) _fontFamilyBox.SelectedIndex = index;
            else if (_fontFamilyBox.Items.Count > 0) _fontFamilyBox.SelectedIndex = 0;
        }

        private void LoadFromSettings()
        {
            BeginSuppressPreview();
            _transparentRadio.Checked = _settings.BackgroundMode == BackgroundMode.Transparent;
            _solidRadio.Checked = _settings.BackgroundMode == BackgroundMode.Solid;
            _imageRadio.Checked = _settings.BackgroundMode == BackgroundMode.Image;
            _backgroundOpacityBar.Value = Math.Max(0, Math.Min(100, (int)Math.Round(_settings.BackgroundOpacity * 100.0 / 255.0)));
            _backgroundOpacityLabel.Text = _backgroundOpacityBar.Value + "%";
            _imagePathBox.Text = _settings.BackgroundImagePath ?? string.Empty;
            _fontSizeBox.Value = (decimal)Math.Max(4f, Math.Min(48f, _settings.FontSize));
            PopulateFontFamilies(_settings.FontFilePath);
            _horizontalRadio.Checked = _settings.LayoutOrientation == LayoutOrientation.Horizontal;
            _verticalRadio.Checked = _settings.LayoutOrientation == LayoutOrientation.Vertical;
            _spacingBar.Value = Math.Max(_spacingBar.Minimum, Math.Min(_spacingBar.Maximum, _settings.MetricSpacing));
            _spacingLabel.Text = _spacingBar.Value + " px";
            _hotkeyText = _settings.MoveHotkey ?? string.Empty;
            _hotkeyBox.Text = string.IsNullOrEmpty(_hotkeyText) ? "未设置" : _hotkeyText;
            _textOpacityBar.Value = Math.Max(0, Math.Min(100, (int)Math.Round(_settings.TextOpacity * 100.0 / 255.0)));
            _textOpacityLabel.Text = _textOpacityBar.Value + "%";
            _clickThroughBox.Checked = _settings.ClickThrough;
            UpdatePreviews();
            UpdateControlState();
            EndSuppressPreview();
        }

        private void UpdatePreviews()
        {
            _backgroundPreview.SelectedColor = _settings.BackgroundColor;
            _fillPreview.SelectedColor = _settings.FillColor;
            _innerPreview.SelectedColor = _settings.InnerOutlineColor;
            _outerPreview.SelectedColor = _settings.OuterOutlineColor;
        }

        private void UpdateControlState()
        {
            bool solid = _solidRadio.Checked;
            bool image = _imageRadio.Checked;
            _backgroundColorButton.Enabled = solid;
            _backgroundPreview.Enabled = solid;
            _browseButton.Enabled = image;
            _clearImageButton.Enabled = image;
            _imagePathBox.Enabled = image;
            _backgroundOpacityBar.Enabled = solid || image;
        }

        private void ReadFromUI()
        {
            if (_transparentRadio.Checked) _settings.BackgroundMode = BackgroundMode.Transparent;
            else if (_solidRadio.Checked) _settings.BackgroundMode = BackgroundMode.Solid;
            else _settings.BackgroundMode = BackgroundMode.Image;
            _settings.BackgroundOpacity = Math.Max(0, Math.Min(255, (int)Math.Round(_backgroundOpacityBar.Value * 255.0 / 100.0)));
            _settings.FontSize = (float)_fontSizeBox.Value;
            _settings.BackgroundImagePath = _imagePathBox.Text;
            _settings.ClickThrough = _clickThroughBox.Checked;
            _settings.LayoutOrientation = _verticalRadio.Checked ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
            _settings.MetricSpacing = _spacingBar.Value;
            _settings.MoveHotkey = _hotkeyText ?? string.Empty;
            _settings.TextOpacity = Math.Max(0, Math.Min(255, (int)Math.Round(_textOpacityBar.Value * 255.0 / 100.0)));
            string selectedFont = _fontFamilyBox.SelectedItem as string;
            _settings.FontFamilyName = string.IsNullOrEmpty(selectedFont) ? "Microsoft YaHei UI" : selectedFont;
            if (!_importedFamilies.Contains(_settings.FontFamilyName))
                _settings.FontFilePath = string.Empty;
        }

        private void ResetSettings()
        {
            OverlaySettings defaults = new OverlaySettings();
            _settings.BackgroundMode = defaults.BackgroundMode;
            _settings.BackgroundColor = defaults.BackgroundColor;
            _settings.BackgroundImagePath = defaults.BackgroundImagePath;
            _settings.BackgroundOpacity = defaults.BackgroundOpacity;
            _settings.FontSize = defaults.FontSize;
            _settings.FontFamilyName = defaults.FontFamilyName;
            _settings.FontFilePath = defaults.FontFilePath;
            _settings.FillColor = defaults.FillColor;
            _settings.InnerOutlineColor = defaults.InnerOutlineColor;
            _settings.OuterOutlineColor = defaults.OuterOutlineColor;
            _settings.ClickThrough = defaults.ClickThrough;
            _settings.LayoutOrientation = defaults.LayoutOrientation;
            _settings.MetricSpacing = defaults.MetricSpacing;
            _settings.MoveHotkey = defaults.MoveHotkey;
            _settings.TextOpacity = defaults.TextOpacity;
            LoadFromSettings();
            NotifyPreview();
        }
    }
}
