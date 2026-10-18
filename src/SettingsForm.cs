using System;
using System.Drawing;
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
        private NumericUpDown _fontSizeBox;
        private ColorSwatchPanel _fillPreview;
        private ColorSwatchPanel _innerPreview;
        private ColorSwatchPanel _outerPreview;

        public OverlaySettings ResultSettings
        {
            get { return _settings; }
        }

        public SettingsForm(OverlaySettings settings)
        {
            _settings = settings.Clone();
            Text = "设置 - 硬件温度与占用监控";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            ClientSize = new Size(520, 610);
            BackColor = Color.FromArgb(246, 247, 250);
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            BuildUI();
            LoadFromSettings();
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
            _transparentRadio.CheckedChanged += delegate { UpdateControlState(); };
            backgroundGroup.Controls.Add(_transparentRadio);

            _solidRadio = new RadioButton();
            _solidRadio.Text = "纯色背景";
            _solidRadio.Location = new Point(16, 54);
            _solidRadio.AutoSize = true;
            _solidRadio.CheckedChanged += delegate { UpdateControlState(); };
            backgroundGroup.Controls.Add(_solidRadio);

            _imageRadio = new RadioButton();
            _imageRadio.Text = "图片背景";
            _imageRadio.Location = new Point(16, 82);
            _imageRadio.AutoSize = true;
            _imageRadio.CheckedChanged += delegate { UpdateControlState(); };
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
            _clearImageButton.Click += delegate { _settings.BackgroundImagePath = string.Empty; _imagePathBox.Text = string.Empty; };
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
            _backgroundOpacityBar.ValueChanged += delegate { _backgroundOpacityLabel.Text = _backgroundOpacityBar.Value + "%"; };
            backgroundGroup.Controls.Add(_backgroundOpacityBar);

            _backgroundOpacityLabel = new Label();
            _backgroundOpacityLabel.Location = new Point(398, 198);
            _backgroundOpacityLabel.Size = new Size(60, 20);
            _backgroundOpacityLabel.TextAlign = ContentAlignment.MiddleLeft;
            backgroundGroup.Controls.Add(_backgroundOpacityLabel);

            GroupBox textGroup = new GroupBox();
            textGroup.Text = "文字";
            textGroup.Location = new Point(12, 274);
            textGroup.Size = new Size(496, 250);
            Controls.Add(textGroup);

            Label fontSizeLabel = new Label();
            fontSizeLabel.Text = "字体大小";
            fontSizeLabel.Location = new Point(38, 30);
            fontSizeLabel.AutoSize = true;
            textGroup.Controls.Add(fontSizeLabel);

            _fontSizeBox = new NumericUpDown();
            _fontSizeBox.Location = new Point(112, 26);
            _fontSizeBox.Size = new Size(90, 24);
            _fontSizeBox.Minimum = 6m;
            _fontSizeBox.Maximum = 20m;
            _fontSizeBox.DecimalPlaces = 1;
            _fontSizeBox.Increment = 0.5m;
            textGroup.Controls.Add(_fontSizeBox);

            Label ptLabel = new Label();
            ptLabel.Text = "pt";
            ptLabel.Location = new Point(208, 30);
            ptLabel.AutoSize = true;
            textGroup.Controls.Add(ptLabel);

            AddColorRow(textGroup, "文字填充", 72, out _fillPreview, delegate { PickColor(_settings.FillColor, "选择文字填充颜色", delegate(Color c) { _settings.FillColor = c; UpdatePreviews(); }); });
            AddColorRow(textGroup, "内圈颜色", 110, out _innerPreview, delegate { PickColor(_settings.InnerOutlineColor, "选择内圈颜色", delegate(Color c) { _settings.InnerOutlineColor = c; UpdatePreviews(); }); });
            AddColorRow(textGroup, "外圈颜色", 148, out _outerPreview, delegate { PickColor(_settings.OuterOutlineColor, "选择外圈颜色", delegate(Color c) { _settings.OuterOutlineColor = c; UpdatePreviews(); }); });

            Label hint = new Label();
            hint.Text = "颜色选择器支持常用调色板、屏幕取色，并可调整透明度。";
            hint.ForeColor = Color.FromArgb(90, 90, 90);
            hint.Location = new Point(38, 202);
            hint.Size = new Size(430, 22);
            textGroup.Controls.Add(hint);

            Button resetButton = new Button();
            resetButton.Text = "恢复默认";
            resetButton.Location = new Point(16, 555);
            resetButton.Size = new Size(100, 32);
            resetButton.Click += delegate { ResetSettings(); };
            Controls.Add(resetButton);

            Button cancelButton = new Button();
            cancelButton.Text = "取消";
            cancelButton.Location = new Point(310, 555);
            cancelButton.Size = new Size(86, 32);
            cancelButton.DialogResult = DialogResult.Cancel;
            Controls.Add(cancelButton);
            CancelButton = cancelButton;

            Button okButton = new Button();
            okButton.Text = "确定";
            okButton.Location = new Point(406, 555);
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
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "无法读取图片：\r\n" + ex.Message, "选择图片", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void LoadFromSettings()
        {
            _transparentRadio.Checked = _settings.BackgroundMode == BackgroundMode.Transparent;
            _solidRadio.Checked = _settings.BackgroundMode == BackgroundMode.Solid;
            _imageRadio.Checked = _settings.BackgroundMode == BackgroundMode.Image;
            _backgroundOpacityBar.Value = Math.Max(0, Math.Min(100, (int)Math.Round(_settings.BackgroundOpacity * 100.0 / 255.0)));
            _backgroundOpacityLabel.Text = _backgroundOpacityBar.Value + "%";
            _imagePathBox.Text = _settings.BackgroundImagePath ?? string.Empty;
            _fontSizeBox.Value = (decimal)Math.Max(6f, Math.Min(20f, _settings.FontSize));
            UpdatePreviews();
            UpdateControlState();
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
        }

        private void ResetSettings()
        {
            OverlaySettings defaults = new OverlaySettings();
            _settings.BackgroundMode = defaults.BackgroundMode;
            _settings.BackgroundColor = defaults.BackgroundColor;
            _settings.BackgroundImagePath = defaults.BackgroundImagePath;
            _settings.BackgroundOpacity = defaults.BackgroundOpacity;
            _settings.FontSize = defaults.FontSize;
            _settings.FillColor = defaults.FillColor;
            _settings.InnerOutlineColor = defaults.InnerOutlineColor;
            _settings.OuterOutlineColor = defaults.OuterOutlineColor;
            LoadFromSettings();
        }
    }
}