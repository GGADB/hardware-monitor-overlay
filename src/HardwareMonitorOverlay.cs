using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace HardwareMonitorOverlay
{
    internal static class Program
    {
        private const string MutexName = "Local\\HardwareMonitorOverlay_SingleInstance";

        [STAThread]
        private static void Main(string[] args)
        {
            bool openSettings = args != null && args.Length > 0 && string.Equals(args[0], "--settings", StringComparison.OrdinalIgnoreCase);
            bool openColorPicker = args != null && args.Length > 0 && string.Equals(args[0], "--colorpicker", StringComparison.OrdinalIgnoreCase);
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { OverlayForm.WriteLog(e.Exception); };
                Application.Run(new OverlayForm(openSettings, openColorPicker));
            }
        }
    }

    internal sealed class OverlayForm : Form
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;
        private const int ULW_ALPHA = 0x02;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT ppsrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int CX; public int CY; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

        private Computer _computer;
        private WinFormsTimer _timer;
       private NotifyIcon _tray;
        private ToolStripMenuItem _autoStartItem;
       private ToolStripMenuItem _clickThroughItem;
        private WinFormsTimer _hotkeyTimer;
        private HotkeySetting _moveHotkey;
        private bool _moveHotkeyHeld;
       private PresentMonFpsProvider _presentMon;
        private IHardware _ssd1;
        private IHardware _ssd2;
       private readonly bool _openSettingsOnShow;
        private readonly bool _openColorPickerOnShow;
        private OverlaySettings _settings;
        private Font _labelFont;
        private Font _valueFont;
        private Image _backgroundImage;
        private PrivateFontCollection _privateFonts;
        private readonly float[] _labelWidths = new float[6];
        private readonly float[] _valueWidths = new float[6];
        private readonly float[] _blockWidths = new float[6];
        private float _loadWidth;
        private float _labelColumnWidth;
        private float _valueColumnWidth;
        private float _rowHeight;
        private float _contentHeight;
        private float _padding;
        private float _gap;
        private float _separatorGap;

        private readonly string[] _metricNames = new string[] { "CPU", "GPU", "RAM", "SSD1", "SSD2", "FPS" };
        private readonly float?[] _temperatures = new float?[6];
        private readonly float?[] _loads = new float?[6];

        public OverlayForm(bool openSettingsOnShow, bool openColorPickerOnShow)
        {
            _openSettingsOnShow = openSettingsOnShow;
            _openColorPickerOnShow = openColorPickerOnShow;
            _settings = SettingsStore.Load();
            _moveHotkey = HotkeySetting.Parse(_settings.MoveHotkey);
            Text = "硬件温度与占用监控";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            CreateFonts();
            LoadBackgroundImage();
            RecalculateLayout();
            BuildMenu();
            Load += delegate { PlaceTopRight(); ApplyClickThrough(); RenderToLayeredWindow(); };
            Shown += delegate
            {
                if (_openSettingsOnShow) BeginInvoke(new Action(OpenSettings));
                else if (_openColorPickerOnShow) BeginInvoke(new Action(delegate { using (ColorPickerDialog dialog = new ColorPickerDialog(Color.White, "选择文字填充颜色")) { dialog.TopMost = true; dialog.ShowDialog(); } }));
            };
            MouseDown += DragWindow;
            FormClosed += delegate { DisposeAll(); };

            StartMonitoring();

            _hotkeyTimer = new WinFormsTimer();
            _hotkeyTimer.Interval = 100;
            _hotkeyTimer.Tick += delegate { PollMoveHotkey(); };
            _hotkeyTimer.Start();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style = unchecked((int)0x80000000); // WS_POPUP
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                if (_settings != null && _settings.ClickThrough) cp.ExStyle |= WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                RenderToLayeredWindow();
            }
            catch (Exception ex)
            {
                WriteLog(ex);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Do not render from OnResize: UpdateLayeredWindow changes the window size and can cause a resize feedback loop.
        }

        private void BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("刷新间隔 1 秒", null, delegate { SetInterval(1000); });
            menu.Items.Add("刷新间隔 2 秒", null, delegate { SetInterval(2000); });
            menu.Items.Add("刷新间隔 3 秒", null, delegate { SetInterval(3000); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("设置...", null, delegate { OpenSettings(); });
            menu.Items.Add("重新放到右上角", null, delegate { PlaceTopRight(); RenderToLayeredWindow(); });
            _clickThroughItem = new ToolStripMenuItem("鼠标穿透（关闭后可拖动面板）");
            _clickThroughItem.CheckOnClick = true;
            _clickThroughItem.Click += delegate
            {
                _settings.ClickThrough = _clickThroughItem.Checked;
                SettingsStore.Save(_settings);
                ApplyClickThrough();
            };
            menu.Items.Add(_clickThroughItem);
            menu.Items.Add(new ToolStripSeparator());
            _autoStartItem = new ToolStripMenuItem("开机自动启动");
            _autoStartItem.CheckOnClick = true;
            _autoStartItem.Click += delegate { ToggleAutoStart(); };
            menu.Items.Add(_autoStartItem);
            menu.Opening += delegate { _autoStartItem.Checked = AutoStartTaskExists(); _clickThroughItem.Checked = _settings.ClickThrough; };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Close(); });
            ContextMenuStrip = menu;

            _tray = new NotifyIcon();
            Icon appIcon = null;
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            _tray.Icon = appIcon == null ? SystemIcons.Application : appIcon;
            _tray.Text = "硬件温度与占用监控";
            _tray.Visible = true;
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowPanel(); };
        }

        private void SetInterval(int milliseconds)
        {
            if (_timer != null) _timer.Interval = milliseconds;
        }

        private void OpenSettings()
        {
            OverlaySettings original = _settings.Clone();
            using (SettingsForm form = new SettingsForm(_settings, delegate(OverlaySettings preview) { ApplyToOverlay(preview, false); }))
            {
                form.TopMost = true;
                if (form.ShowDialog() == DialogResult.OK)
                {
                    ApplyToOverlay(form.ResultSettings, true);
                }
                else
                {
                    ApplyToOverlay(original, false);
                }
            }
        }

        private void ApplyToOverlay(OverlaySettings settings, bool save)
        {
            bool reloadBackground = _settings == null || _settings.BackgroundMode != settings.BackgroundMode || !string.Equals(_settings.BackgroundImagePath, settings.BackgroundImagePath, StringComparison.OrdinalIgnoreCase);
            bool reloadFonts = _settings == null || _settings.FontSize != settings.FontSize || !string.Equals(_settings.FontFamilyName, settings.FontFamilyName, StringComparison.OrdinalIgnoreCase) || !string.Equals(_settings.FontFilePath, settings.FontFilePath, StringComparison.OrdinalIgnoreCase);
            string oldHotkey = _settings == null ? null : _settings.MoveHotkey;

            _settings = settings.Clone();
            if (save) SettingsStore.Save(_settings);
            if (!string.Equals(oldHotkey, _settings.MoveHotkey, StringComparison.OrdinalIgnoreCase))
            {
                _moveHotkey = HotkeySetting.Parse(_settings.MoveHotkey);
                _moveHotkeyHeld = false;
            }
            if (reloadFonts) CreateFonts();
            if (reloadBackground) LoadBackgroundImage();
            RecalculateLayout();
            ClampToScreen();
            ApplyClickThrough();
            RenderToLayeredWindow();
        }

        private void CreateFonts()
        {
            if (_labelFont != null) _labelFont.Dispose();
            if (_valueFont != null) _valueFont.Dispose();
            if (_privateFonts != null) { _privateFonts.Dispose(); _privateFonts = null; }

            float size = Math.Max(4f, Math.Min(48f, _settings.FontSize));
            FontFamily importedFamily = null;
            if (!string.IsNullOrEmpty(_settings.FontFilePath) && File.Exists(_settings.FontFilePath))
            {
                try
                {
                    _privateFonts = new PrivateFontCollection();
                    _privateFonts.AddFontFile(_settings.FontFilePath);
                    importedFamily = _privateFonts.Families.FirstOrDefault(f => string.Equals(f.Name, _settings.FontFamilyName, StringComparison.OrdinalIgnoreCase));
                    if (importedFamily == null) importedFamily = _privateFonts.Families.FirstOrDefault();
                }
                catch
                {
                    if (_privateFonts != null) { _privateFonts.Dispose(); _privateFonts = null; }
                    importedFamily = null;
                }
            }

            string familyName = string.IsNullOrEmpty(_settings.FontFamilyName) ? "Microsoft YaHei UI" : _settings.FontFamilyName;
            _labelFont = CreateFont(importedFamily, familyName, size, FontStyle.Bold);
            _valueFont = CreateFont(importedFamily, familyName, size, FontStyle.Bold);
        }

        private static Font CreateFont(FontFamily importedFamily, string familyName, float size, FontStyle style)
        {
            try
            {
                if (importedFamily != null) return new Font(importedFamily, size, style, GraphicsUnit.Point);
                return new Font(familyName, size, style, GraphicsUnit.Point);
            }
            catch
            {
                try
                {
                    if (importedFamily != null) return new Font(importedFamily, size, FontStyle.Regular, GraphicsUnit.Point);
                    return new Font(familyName, size, FontStyle.Regular, GraphicsUnit.Point);
                }
                catch
                {
                    return new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Point);
                }
            }
        }
        private void RecalculateLayout()
        {
            using (Bitmap bitmap = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                StringFormat format = StringFormat.GenericTypographic;
                float spacing = Math.Max(0f, Math.Min(30f, _settings.MetricSpacing));
                _padding = Math.Max(4f, spacing + 1f);
                _gap = spacing;
                _separatorGap = Math.Max(4f, spacing + 3f);
                _contentHeight = Math.Max(_labelFont.Height, _valueFont.Height);
                _loadWidth = graphics.MeasureString("100%", _valueFont, PointF.Empty, format).Width;
                _labelColumnWidth = 0f;
                _valueColumnWidth = 0f;
                for (int i = 0; i < _metricNames.Length; i++)
                {
                    _labelWidths[i] = graphics.MeasureString(_metricNames[i], _labelFont, PointF.Empty, format).Width;
                    string sample = i == 5 ? "0000" : "000.0\u00B0C";
                    _valueWidths[i] = graphics.MeasureString(sample, _valueFont, PointF.Empty, format).Width;
                    _blockWidths[i] = _labelWidths[i] + _gap + _valueWidths[i];
                    if (i < 3) _blockWidths[i] += _gap + _loadWidth;
                    _labelColumnWidth = Math.Max(_labelColumnWidth, _labelWidths[i]);
                    _valueColumnWidth = Math.Max(_valueColumnWidth, _valueWidths[i]);
                }

                int width;
                int height;
                if (_settings.LayoutOrientation == LayoutOrientation.Vertical)
                {
                    _rowHeight = _contentHeight + Math.Max(2f, spacing * 0.8f);
                    width = (int)Math.Ceiling(_padding * 2f + _labelColumnWidth + _gap + _valueColumnWidth + _gap + _loadWidth);
                    height = (int)Math.Ceiling(_padding * 2f + _rowHeight * _metricNames.Length);
                }
                else
                {
                    _rowHeight = 0f;
                    float total = _padding * 2f;
                    for (int i = 0; i < _metricNames.Length; i++) total += _blockWidths[i];
                    total += (_metricNames.Length - 1) * (_separatorGap * 2f + 1f);
                    width = (int)Math.Ceiling(total);
                    height = (int)Math.Ceiling(_padding * 2f + _contentHeight + 2f);
                }
                ClientSize = new Size(Math.Max(80, width), Math.Max(28, height));
            }
        }

        private void LoadBackgroundImage()
        {
            if (_backgroundImage != null)
            {
                _backgroundImage.Dispose();
                _backgroundImage = null;
            }
            if (_settings.BackgroundMode != BackgroundMode.Image) return;
            if (string.IsNullOrEmpty(_settings.BackgroundImagePath) || !File.Exists(_settings.BackgroundImagePath)) return;
            try
            {
                using (Image image = Image.FromFile(_settings.BackgroundImagePath))
                    _backgroundImage = new Bitmap(image);
            }
            catch (Exception ex)
            {
                WriteLog(ex);
            }
        }

        private void RenderToLayeredWindow()
        {
            if (!IsHandleCreated) return;
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            using (Bitmap bitmap = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.Clear(Color.Transparent);
                    DrawBackground(graphics, bitmap.Size);
                    DrawMetrics(graphics);
                }
                UpdateLayeredWindowFromBitmap(bitmap);
            }
        }

        private void DrawBackground(Graphics graphics, Size size)
        {
            if (_settings.BackgroundMode == BackgroundMode.Transparent) return;
            Rectangle rectangle = new Rectangle(0, 0, size.Width, size.Height);
            using (GraphicsPath path = RoundedRect(rectangle, 10))
            {
                if (_settings.BackgroundMode == BackgroundMode.Solid)
                {
                    int alpha = (_settings.BackgroundColor.A * _settings.BackgroundOpacity) / 255;
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, _settings.BackgroundColor.R, _settings.BackgroundColor.G, _settings.BackgroundColor.B)))
                        graphics.FillPath(brush, path);
                }
                else if (_settings.BackgroundMode == BackgroundMode.Image && _backgroundImage != null)
                {
                    GraphicsState state = graphics.Save();
                    graphics.SetClip(path);
                    DrawImageCover(graphics, _backgroundImage, rectangle, _settings.BackgroundOpacity);
                    graphics.Restore(state);
                }
            }
        }

        private static void DrawImageCover(Graphics graphics, Image image, Rectangle destination, int opacity)
        {
            float scale = Math.Max((float)destination.Width / image.Width, (float)destination.Height / image.Height);
            int width = (int)Math.Ceiling(image.Width * scale);
            int height = (int)Math.Ceiling(image.Height * scale);
            int x = destination.Left + (destination.Width - width) / 2;
            int y = destination.Top + (destination.Height - height) / 2;
            ColorMatrix matrix = new ColorMatrix();
            matrix.Matrix33 = opacity / 255f;
            using (ImageAttributes attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                graphics.DrawImage(image, new Rectangle(x, y, width, height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        private void DrawMetrics(Graphics graphics)
        {
            if (_settings.LayoutOrientation == LayoutOrientation.Vertical) DrawMetricsVertical(graphics);
            else DrawMetricsHorizontal(graphics);
        }

        private void DrawMetricsHorizontal(Graphics graphics)
        {
            float yLabel = _padding + (_contentHeight - _labelFont.Height) / 2f;
            float yValue = _padding + (_contentHeight - _valueFont.Height) / 2f;
            float x = _padding;
            for (int i = 0; i < _metricNames.Length; i++)
            {
                DrawStyledText(graphics, _metricNames[i], x, yLabel, _labelFont, _settings.FillColor);
                x += _labelWidths[i] + _gap;
                string valueText = i == 5 ? FormatFps(_temperatures[i]) : FormatTemperature(_temperatures[i]);
                Color valueColor = i == 5 ? _settings.FillColor : TemperatureColor(_temperatures[i], i);
                DrawStyledText(graphics, valueText, x, yValue, _valueFont, valueColor);
                x += _valueWidths[i];
                if (i < 3)
                {
                    x += _gap;
                    DrawStyledText(graphics, FormatLoad(_loads[i]), x, yValue, _valueFont, LoadColor(_loads[i]));
                    x += _loadWidth;
                }
                if (i < _metricNames.Length - 1)
                {
                    x += _separatorGap;
                    using (Pen pen = new Pen(Color.FromArgb(80, _settings.FillColor), 1f))
                        graphics.DrawLine(pen, x, _padding, x, ClientSize.Height - _padding);
                    x += 1f + _separatorGap;
                }
            }
        }

        private void DrawMetricsVertical(Graphics graphics)
        {
            float y = _padding;
            for (int i = 0; i < _metricNames.Length; i++)
            {
                float yLabel = y + (_contentHeight - _labelFont.Height) / 2f;
                float yValue = y + (_contentHeight - _valueFont.Height) / 2f;
                DrawStyledText(graphics, _metricNames[i], _padding, yLabel, _labelFont, _settings.FillColor);
                float valueX = _padding + _labelColumnWidth + _gap;
                string valueText = i == 5 ? FormatFps(_temperatures[i]) : FormatTemperature(_temperatures[i]);
                Color valueColor = i == 5 ? _settings.FillColor : TemperatureColor(_temperatures[i], i);
                DrawStyledText(graphics, valueText, valueX, yValue, _valueFont, valueColor);
                if (i < 3)
                    DrawStyledText(graphics, FormatLoad(_loads[i]), valueX + _valueColumnWidth + _gap, yValue, _valueFont, LoadColor(_loads[i]));
                y += _rowHeight;
            }
        }

        private void DrawStyledText(Graphics graphics, string text, float x, float y, Font font, Color fillColor)
        {
            int opacity = Math.Max(0, Math.Min(255, _settings.TextOpacity));
            if (opacity >= 255 && _settings.OuterOutlineColor.A >= 255 && _settings.InnerOutlineColor.A >= 255 && fillColor.A >= 255)
            {
                DrawTextRing(graphics, text, x, y, font, _settings.OuterOutlineColor, 2);
                DrawTextRing(graphics, text, x, y, font, _settings.InnerOutlineColor, 1);
                using (SolidBrush brush = new SolidBrush(fillColor))
                    graphics.DrawString(text, font, brush, new PointF(x, y), StringFormat.GenericTypographic);
                return;
            }

            const int pad = 3;
            SizeF measured = graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
            int width = (int)Math.Ceiling(measured.Width) + pad * 2;
            int height = (int)Math.Ceiling(measured.Height) + pad * 2;
            if (width <= 0 || height <= 0) return;

            using (Bitmap textBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics textGraphics = Graphics.FromImage(textBitmap))
                {
                    textGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    textGraphics.SmoothingMode = SmoothingMode.AntiAlias;
                    textGraphics.Clear(Color.Transparent);
                    DrawTextRing(textGraphics, text, pad, pad, font, _settings.OuterOutlineColor, 2);
                    DrawTextRing(textGraphics, text, pad, pad, font, _settings.InnerOutlineColor, 1);
                    using (SolidBrush brush = new SolidBrush(fillColor))
                        textGraphics.DrawString(text, font, brush, new PointF(pad, pad), StringFormat.GenericTypographic);
                }

                ColorMatrix matrix = new ColorMatrix();
                matrix.Matrix33 = opacity / 255f;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    graphics.DrawImage(textBitmap, new Rectangle((int)Math.Floor(x) - pad, (int)Math.Floor(y) - pad, width, height), 0, 0, width, height, GraphicsUnit.Pixel, attributes);
                }
            }
        }

        private static void DrawTextRing(Graphics graphics, string text, float x, float y, Font font, Color color, int radius)
        {
            using (SolidBrush brush = new SolidBrush(color))
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;
                        graphics.DrawString(text, font, brush, new PointF(x + dx, y + dy), StringFormat.GenericTypographic);
                    }
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle rectangle, int radius)
        {
            int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static string FormatTemperature(float? value)
        {
            return value.HasValue ? value.Value.ToString("0.0") + "\u00B0C" : "--";
        }

        private static string FormatLoad(float? value)
        {
            return value.HasValue ? value.Value.ToString("0") + "%" : "--";
        }
        private static string FormatFps(float? value)
        {
            return value.HasValue ? value.Value.ToString("0") : "--";
        }

        private Color TemperatureColor(float? value, int index)
        {
            int alpha = _settings.FillColor.A;
            if (!value.HasValue) return Color.FromArgb(alpha, 235, 235, 235);
            float warn = index == 2 ? 50 : index >= 3 ? 55 : 70;
            float hot = index == 2 ? 65 : index >= 3 ? 70 : 85;
            if (value.Value >= hot) return Color.FromArgb(alpha, 255, 110, 100);
            if (value.Value >= warn) return Color.FromArgb(alpha, 255, 186, 90);
            return _settings.FillColor;
        }

        private Color LoadColor(float? value)
        {
            int alpha = _settings.FillColor.A;
            if (!value.HasValue) return Color.FromArgb(alpha, 235, 235, 235);
            if (value.Value >= 90) return Color.FromArgb(alpha, 255, 110, 100);
            if (value.Value >= 70) return Color.FromArgb(alpha, 255, 186, 90);
            return _settings.FillColor;
        }

        private void PlaceTopRight()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 14, area.Top + 14);
        }

        private void ClampToScreen()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int x = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            int y = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            Location = new Point(x, y);
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
            }
        }

        private void ApplyClickThrough()
        {
            if (!IsHandleCreated) return;
            try
            {
                int style = GetWindowLong(Handle, GWL_EXSTYLE);
                int updated = (_settings.ClickThrough && !_moveHotkeyHeld) ? (style | WS_EX_TRANSPARENT) : (style & ~WS_EX_TRANSPARENT);
                if (updated != style) SetWindowLong(Handle, GWL_EXSTYLE, updated);
            }
            catch (Exception ex)
            {
                WriteLog(ex);
            }
        }

        private void PollMoveHotkey()
        {
            bool held = false;
            if (_moveHotkey != null && _moveHotkey.IsValid)
            {
                held = IsKeyDown((int)_moveHotkey.VirtualKey);
                if ((_moveHotkey.Modifiers & HotkeySetting.ModControl) != 0) held &= IsKeyDown(0x11);
                if ((_moveHotkey.Modifiers & HotkeySetting.ModAlt) != 0) held &= IsKeyDown(0x12);
                if ((_moveHotkey.Modifiers & HotkeySetting.ModShift) != 0) held &= IsKeyDown(0x10);
                if ((_moveHotkey.Modifiers & HotkeySetting.ModWin) != 0) held &= IsKeyDown(0x5B) || IsKeyDown(0x5C);
            }
            if (held != _moveHotkeyHeld)
            {
                _moveHotkeyHeld = held;
                ApplyClickThrough();
            }
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private void ShowPanel()
        {
            if (!Visible)
            {
                ShowQuietly();
                Visible = true;
                TopMost = true;
                RenderToLayeredWindow();
            }
        }

        private void ShowQuietly()
        {
            try { Show(); } catch { }
        }

        private void DisposeAll()
        {
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); _timer = null; }
            if (_hotkeyTimer != null) { _hotkeyTimer.Stop(); _hotkeyTimer.Dispose(); _hotkeyTimer = null; }
            if (_computer != null) { try { _computer.Close(); } catch { } _computer = null; }
            if (_presentMon != null) { _presentMon.Dispose(); _presentMon = null; }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            if (_backgroundImage != null) { _backgroundImage.Dispose(); _backgroundImage = null; }
            if (_labelFont != null) { _labelFont.Dispose(); _labelFont = null; }
            if (_valueFont != null) { _valueFont.Dispose(); _valueFont = null; }
        }

        private void UpdateLayeredWindowFromBitmap(Bitmap bitmap)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memoryDc, hBitmap);
                POINT destination = new POINT { X = Left, Y = Top };
                SIZE size = new SIZE { CX = bitmap.Width, CY = bitmap.Height };
                POINT source = new POINT { X = 0, Y = 0 };
                BLENDFUNCTION blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
                UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
        public static void WriteLog(Exception ex)
        {
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                string file = Path.Combine(dir, "overlay-error.log");
                if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) File.Delete(file);
                File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + ex + Environment.NewLine);
            }
            catch { }
        }
        private void StartMonitoring()
        {
            try
            {
                _presentMon = new PresentMonFpsProvider();
                _presentMon.Start();
                _computer = new Computer();
                _computer.IsCpuEnabled = true;
                _computer.IsGpuEnabled = true;
                _computer.IsMotherboardEnabled = true;
                _computer.IsMemoryEnabled = true;
                _computer.IsStorageEnabled = true;
                _computer.Open();
                UpdateHardware();
                UpdateValues();

                _timer = new WinFormsTimer();
                _timer.Interval = 2000;
                _timer.Tick += delegate { SafeUpdate(); };
                _timer.Start();
            }
            catch (Exception ex)
            {
                WriteLog(ex);
            }
        }

        private void SafeUpdate()
        {
            try
            {
                UpdateHardware();
                UpdateValues();
                RenderToLayeredWindow();
            }
            catch (Exception ex)
            {
                WriteLog(ex);
            }
        }

        private void UpdateHardware()
        {
            if (_computer == null) return;
            foreach (IHardware hardware in _computer.Hardware)
            {
                try
                {
                    hardware.Update();
                    foreach (IHardware sub in hardware.SubHardware) sub.Update();
                }
                catch (Exception ex) { WriteLog(ex); }
            }
        }

        private IHardware FindHardware(HardwareType type, string namePart)
        {
            if (_computer == null) return null;
            IEnumerable<IHardware> matches = _computer.Hardware.Where(h => h.HardwareType == type);
            if (!string.IsNullOrEmpty(namePart))
            {
                IHardware exact = matches.FirstOrDefault(h => h.Name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0);
                if (exact != null) return exact;
            }
            return matches.FirstOrDefault();
        }

        private static float? Value(IHardware hardware, SensorType type, string exactName)
        {
            if (hardware == null) return null;
            ISensor sensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == exactName && s.Value.HasValue);
            if (sensor != null) return sensor.Value;
            sensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == type && s.Value.HasValue);
            return sensor == null ? (float?)null : sensor.Value;
        }

        private float? CpuTemperature()
        {
            IHardware cpu = FindHardware(HardwareType.Cpu, null);
            if (cpu == null) return null;
            string[] names = new string[] { "Core (Tctl/Tdie)", "CPU Package", "Package" };
            foreach (string name in names)
            {
                ISensor sensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == name && s.Value.HasValue);
                if (sensor != null) return sensor.Value;
            }
            ISensor fallback = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0 && s.Value.HasValue);
            if (fallback != null) return fallback.Value;
            fallback = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value.HasValue);
            return fallback == null ? (float?)null : fallback.Value;
        }

        private float? CpuLoad()
        {
            IHardware cpu = FindHardware(HardwareType.Cpu, null);
            if (cpu == null) return null;
            ISensor sensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name == "CPU Total" && s.Value.HasValue);
            if (sensor == null) sensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0 && s.Value.HasValue);
            return sensor == null ? (float?)null : sensor.Value;
        }

        private IHardware PrimaryGpu()
        {
            if (_computer == null) return null;
            IHardware gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia);
            if (gpu == null) gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd);
            if (gpu == null) gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
            return gpu;
        }

        private float? MemoryLoad()
        {
            if (_computer == null) return null;
            IHardware memory = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && h.Name.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0);
            if (memory == null) memory = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && h.Identifier.ToString().EndsWith("/ram"));
            if (memory == null) memory = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
            return Value(memory, SensorType.Load, "Memory");
        }

        private float? MemoryTemperature()
        {
            if (_computer == null) return null;
            float max = float.MinValue;
            bool found = false;
            foreach (IHardware memory in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Memory))
            {
                foreach (ISensor sensor in memory.Sensors)
                {
                    if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue) continue;
                    if (sensor.Name.IndexOf("DIMM", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (sensor.Value.Value > max) max = sensor.Value.Value;
                    found = true;
                }
            }
            return found ? (float?)max : null;
        }

        private void FindSsdHardware()
        {
            if (_computer == null) return;
            List<IHardware> storage = _computer.Hardware
                .Where(h => h.HardwareType == HardwareType.Storage)
                .Where(h => h.Sensors.Any(s => s.SensorType == SensorType.Temperature && s.Value.HasValue))
                .ToList();
            _ssd1 = storage.FirstOrDefault();
            _ssd2 = storage.Skip(1).FirstOrDefault();
        }
        private static float? StorageTemperature(IHardware hardware)
        {
            if (hardware == null) return null;
            ISensor sensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == "Composite Temperature" && s.Value.HasValue);
            if (sensor == null) sensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == "Temperature" && s.Value.HasValue);
            if (sensor == null) sensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Name.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) < 0 && s.Name.IndexOf("Critical", StringComparison.OrdinalIgnoreCase) < 0 && s.Name.IndexOf("Limit", StringComparison.OrdinalIgnoreCase) < 0 && s.Name.IndexOf("Resolution", StringComparison.OrdinalIgnoreCase) < 0);
            return sensor == null ? (float?)null : sensor.Value;
        }

        private float? ReadFps()
        {
            uint processId = 0;
            IntPtr foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero) GetWindowThreadProcessId(foreground, out processId);

            if (_presentMon != null)
            {
                _presentMon.UpdateForegroundPid(processId);
                float? fps = _presentMon.CurrentFps;
                if (fps.HasValue) return fps;
            }

            return null;
        }
        private void UpdateValues()
        {
            FindSsdHardware();
            _temperatures[0] = CpuTemperature();
            _loads[0] = CpuLoad();
            IHardware gpu = PrimaryGpu();
            _temperatures[1] = Value(gpu, SensorType.Temperature, "GPU Core");
            _loads[1] = Value(gpu, SensorType.Load, "GPU Core");
            _temperatures[2] = MemoryTemperature();
            _loads[2] = MemoryLoad();
            _temperatures[3] = StorageTemperature(_ssd1);
            _temperatures[4] = StorageTemperature(_ssd2);
            _temperatures[5] = ReadFps();
            _loads[5] = null;
        }

        private const string StartupTaskName = "HardwareMonitorOverlay";

        private void ToggleAutoStart()
        {
            bool enable = _autoStartItem.Checked;
            bool ok = enable ? CreateAutoStartTask() : DeleteAutoStartTask();
            if (!ok) _autoStartItem.Checked = !enable;
        }

        private static bool AutoStartTaskExists()
        {
            string output;
            return RunSchTasks("/Query /TN \"" + StartupTaskName + "\"", out output);
        }

        private static bool CreateAutoStartTask()
        {
            string user = SecurityElement.Escape(Environment.UserDomainName + "\\" + Environment.UserName);
            string exe = SecurityElement.Escape(Application.ExecutablePath);
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <Triggers>\r\n" +
                "    <LogonTrigger>\r\n" +
                "      <Enabled>true</Enabled>\r\n" +
                "      <UserId>" + user + "</UserId>\r\n" +
                "    </LogonTrigger>\r\n" +
                "  </Triggers>\r\n" +
                "  <Principals>\r\n" +
                "    <Principal id=\"Author\">\r\n" +
                "      <UserId>" + user + "</UserId>\r\n" +
                "      <LogonType>InteractiveToken</LogonType>\r\n" +
                "      <RunLevel>HighestAvailable</RunLevel>\r\n" +
                "    </Principal>\r\n" +
                "  </Principals>\r\n";            xml += "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                "    <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
                "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
                "    <IdleSettings>\r\n" +
                "      <StopOnIdleEnd>false</StopOnIdleEnd>\r\n" +
                "      <RestartOnIdle>false</RestartOnIdle>\r\n" +
                "    </IdleSettings>\r\n" +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "    <Hidden>false</Hidden>\r\n" +
                "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
                "    <WakeToRun>false</WakeToRun>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Priority>7</Priority>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\">\r\n" +
                "    <Exec>\r\n" +
                "      <Command>" + exe + "</Command>\r\n" +
                "    </Exec>\r\n" +
                "  </Actions>\r\n" +
                "</Task>";
            string file = Path.Combine(Path.GetTempPath(), "HardwareMonitorOverlay.task.xml");
            File.WriteAllText(file, xml, Encoding.Unicode);
            string output;
            bool ok = RunSchTasks("/Create /TN \"" + StartupTaskName + "\" /XML \"" + file + "\" /F", out output);
            try { File.Delete(file); } catch { }
            if (!ok) MessageBox.Show("无法设置开机自动启动。\r\n\r\n" + output, "硬件监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return ok;
        }
        private static bool DeleteAutoStartTask()
        {
            string output;
            bool ok = RunSchTasks("/Delete /TN \"" + StartupTaskName + "\" /F", out output);
            if (!ok) MessageBox.Show("无法取消开机自动启动。\r\n\r\n" + output, "硬件监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return ok;
        }

        private static bool RunSchTasks(string arguments, out string output)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
            info.Arguments = arguments;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            using (Process process = Process.Start(info))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                output = stdout + stderr;
                return process.ExitCode == 0;
            }
        }

    }
}
