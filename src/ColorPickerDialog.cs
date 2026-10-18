using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HardwareMonitorOverlay
{
    internal sealed class ColorSwatchPanel : Panel
    {
        private Color _selectedColor = Color.Transparent;

        public Color SelectedColor
        {
            get { return _selectedColor; }
            set { _selectedColor = value; Invalidate(); }
        }

        public ColorSwatchPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawCheckerboard(e.Graphics, ClientRectangle);
            using (SolidBrush brush = new SolidBrush(_selectedColor))
                e.Graphics.FillRectangle(brush, ClientRectangle);
            using (Pen pen = new Pen(Color.FromArgb(140, 80, 80, 80)))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        public static void DrawCheckerboard(Graphics graphics, Rectangle rectangle)
        {
            const int cell = 8;
            using (SolidBrush light = new SolidBrush(Color.FromArgb(235, 235, 235)))
            using (SolidBrush dark = new SolidBrush(Color.FromArgb(205, 205, 205)))
            {
                for (int y = 0; y < rectangle.Height; y += cell)
                {
                    for (int x = 0; x < rectangle.Width; x += cell)
                    {
                        Rectangle r = new Rectangle(rectangle.Left + x, rectangle.Top + y, cell, cell);
                        graphics.FillRectangle(((x / cell + y / cell) % 2 == 0) ? light : dark, r);
                    }
                }
            }
        }
    }

    internal sealed class ColorPickerDialog : Form
    {
        private Color _selectedColor;
        private ColorSwatchPanel _preview;
        private TrackBar _alphaBar;
        private Label _alphaLabel;
        private TextBox _hexBox;
        private bool _updating;

        public Color SelectedColor
        {
            get { return _selectedColor; }
        }

        public ColorPickerDialog(Color initialColor, string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(500, 540);
            BackColor = Color.FromArgb(246, 247, 250);
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

            Label paletteLabel = new Label();
            paletteLabel.Text = "常用颜色";
            paletteLabel.Location = new Point(16, 12);
            paletteLabel.AutoSize = true;
            Controls.Add(paletteLabel);

            TableLayoutPanel palette = new TableLayoutPanel();
            palette.Location = new Point(16, 36);
            palette.Size = new Size(468, 228);
            palette.ColumnCount = 8;
            palette.RowCount = 6;
            palette.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
            for (int i = 0; i < 8; i++) palette.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));
            for (int i = 0; i < 6; i++) palette.RowStyles.Add(new RowStyle(SizeType.Percent, 16.6667f));
            Color[] colors = GetPalette();
            foreach (Color color in colors)
            {
                Button button = new Button();
                button.Dock = DockStyle.Fill;
                button.Margin = new Padding(1);
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Color.FromArgb(150, 150, 150);
                button.BackColor = color;
                button.UseVisualStyleBackColor = false;
                button.Tag = color;
                button.Click += delegate(object sender, EventArgs e) { SetColor((Color)((Button)sender).Tag); };
                palette.Controls.Add(button);
            }
            Controls.Add(palette);

            Label previewLabel = new Label();
            previewLabel.Text = "预览";
            previewLabel.Location = new Point(16, 280);
            previewLabel.AutoSize = true;
            Controls.Add(previewLabel);

            _preview = new ColorSwatchPanel();
            _preview.Location = new Point(60, 276);
            _preview.Size = new Size(120, 36);
            Controls.Add(_preview);

            Label alphaLabelTitle = new Label();
            alphaLabelTitle.Text = "透明度";
            alphaLabelTitle.Location = new Point(200, 282);
            alphaLabelTitle.AutoSize = true;
            Controls.Add(alphaLabelTitle);

            _alphaBar = new TrackBar();
            _alphaBar.Minimum = 0;
            _alphaBar.Maximum = 255;
            _alphaBar.TickFrequency = 32;
            _alphaBar.LargeChange = 16;
            _alphaBar.SmallChange = 1;
            _alphaBar.Location = new Point(258, 272);
            _alphaBar.Size = new Size(170, 45);
            _alphaBar.ValueChanged += delegate
            {
                if (_updating) return;
                SetColor(Color.FromArgb(_alphaBar.Value, _selectedColor.R, _selectedColor.G, _selectedColor.B));
            };
            Controls.Add(_alphaBar);

            _alphaLabel = new Label();
            _alphaLabel.Location = new Point(432, 282);
            _alphaLabel.Size = new Size(52, 20);
            _alphaLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_alphaLabel);

            Label hexLabel = new Label();
            hexLabel.Text = "颜色值";
            hexLabel.Location = new Point(16, 326);
            hexLabel.AutoSize = true;
            Controls.Add(hexLabel);

            _hexBox = new TextBox();
            _hexBox.Location = new Point(78, 323);
            _hexBox.Size = new Size(160, 24);
            _hexBox.CharacterCasing = CharacterCasing.Upper;
            Controls.Add(_hexBox);

            Button applyHex = new Button();
            applyHex.Text = "应用";
            applyHex.Location = new Point(246, 322);
            applyHex.Size = new Size(70, 26);
            applyHex.Click += delegate
            {
                Color parsed = SettingsStore.ColorFromHex(_hexBox.Text, _selectedColor);
                SetColor(parsed);
            };
            Controls.Add(applyHex);

            Button pickScreen = new Button();
            pickScreen.Text = "屏幕取色";
            pickScreen.Location = new Point(330, 322);
            pickScreen.Size = new Size(154, 28);
            pickScreen.Click += delegate { PickFromScreen(); };
            Controls.Add(pickScreen);

            Label hint = new Label();
            hint.Text = "取色时请点击屏幕上的目标颜色，按 Esc 可取消取色。";
            hint.ForeColor = Color.FromArgb(90, 90, 90);
            hint.Location = new Point(16, 360);
            hint.Size = new Size(468, 20);
            Controls.Add(hint);

            Button okButton = new Button();
            okButton.Text = "确定";
            okButton.Location = new Point(300, 490);
            okButton.Size = new Size(80, 30);
            okButton.DialogResult = DialogResult.OK;
            Controls.Add(okButton);
            AcceptButton = okButton;

            Button cancelButton = new Button();
            cancelButton.Text = "取消";
            cancelButton.Location = new Point(400, 490);
            cancelButton.Size = new Size(80, 30);
            cancelButton.DialogResult = DialogResult.Cancel;
            Controls.Add(cancelButton);
            CancelButton = cancelButton;

            SetColor(initialColor);
        }

        private void SetColor(Color color)
        {
            _selectedColor = color;
            _updating = true;
            _alphaBar.Value = color.A;
            _alphaLabel.Text = ((int)Math.Round(color.A * 100.0 / 255.0)) + "%";
            _hexBox.Text = SettingsStore.ColorToHex(color);
            _preview.SelectedColor = color;
            _updating = false;
        }

        private void PickFromScreen()
        {
            Hide();
            Application.DoEvents();
            try
            {
                using (EyedropperForm form = new EyedropperForm())
                {
                    if (form.ShowDialog() == DialogResult.OK)
                        SetColor(Color.FromArgb(_selectedColor.A, form.PickedColor.R, form.PickedColor.G, form.PickedColor.B));
                }
            }
            finally
            {
                Show();
                Activate();
            }
        }

        private static Color[] GetPalette()
        {
            return new Color[]
            {
                Color.FromArgb(0, 0, 0), Color.FromArgb(45, 45, 45), Color.FromArgb(80, 80, 80), Color.FromArgb(120, 120, 120),
                Color.FromArgb(170, 170, 170), Color.FromArgb(210, 210, 210), Color.FromArgb(240, 240, 240), Color.White,

                Color.FromArgb(120, 0, 0), Color.FromArgb(220, 0, 0), Color.FromArgb(255, 80, 80), Color.FromArgb(255, 150, 120),
                Color.FromArgb(255, 180, 60), Color.FromArgb(255, 220, 70), Color.FromArgb(255, 245, 150), Color.FromArgb(255, 255, 255),

                Color.FromArgb(180, 120, 0), Color.FromArgb(230, 170, 0), Color.FromArgb(190, 210, 0), Color.FromArgb(120, 190, 40),
                Color.FromArgb(40, 170, 80), Color.FromArgb(0, 190, 150), Color.FromArgb(0, 210, 210), Color.FromArgb(120, 235, 235),

                Color.FromArgb(0, 90, 160), Color.FromArgb(0, 130, 220), Color.FromArgb(70, 170, 255), Color.FromArgb(140, 205, 255),
                Color.FromArgb(100, 100, 220), Color.FromArgb(150, 100, 220), Color.FromArgb(210, 100, 220), Color.FromArgb(250, 150, 230),

                Color.FromArgb(90, 40, 20), Color.FromArgb(140, 80, 40), Color.FromArgb(200, 140, 90), Color.FromArgb(235, 190, 150),
                Color.FromArgb(120, 20, 60), Color.FromArgb(180, 50, 100), Color.FromArgb(230, 110, 150), Color.FromArgb(255, 180, 210),

                Color.FromArgb(30, 50, 70), Color.FromArgb(55, 85, 115), Color.FromArgb(80, 120, 160), Color.FromArgb(120, 170, 210),
                Color.FromArgb(60, 90, 60), Color.FromArgb(100, 140, 90), Color.FromArgb(150, 190, 130), Color.FromArgb(200, 225, 180)
            };
        }
    }

    internal sealed class EyedropperForm : Form
    {
        public Color PickedColor { get; private set; }

        public EyedropperForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Opacity = 0.01;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            PickedColor = Color.White;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Focus();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Point point = Cursor.Position;
                using (Bitmap bitmap = new Bitmap(1, 1))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(point, Point.Empty, new Size(1, 1));
                    Color sampled = bitmap.GetPixel(0, 0);
                    int r = Math.Min(255, (int)Math.Round(sampled.R / 0.99));
                    int g = Math.Min(255, (int)Math.Round(sampled.G / 0.99));
                    int b = Math.Min(255, (int)Math.Round(sampled.B / 0.99));
                    PickedColor = Color.FromArgb(255, r, g, b);
                }
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            base.OnKeyDown(e);
        }
    }
}