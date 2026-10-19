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
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
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

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int CX; public int CY; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

        private Computer _computer;
        private WinFormsTimer _timer;
        private WinFormsTimer _fullscreenTimer;
        private NotifyIcon _tray;
        private ToolStripMenuItem _autoStartItem;
        private ToolStripMenuItem _rtssStatusItem;
        private RtssOsdWriter _rtss;
        private PresentMonFpsProvider _presentMon;
        private IHardware _ssd1;
        private IHardware _ssd2;
        private bool _panelHidden;
        private readonly bool _openSettingsOnShow;
        private readonly bool _openColorPickerOnShow;
        private OverlaySettings _settings;
        private Font _labelFont;
        private Font _valueFont;
        private Image _backgroundImage;
        private PrivateFontCollection _privateFonts;
        private float _labelWidth;
        private float _tempWidth;
        private float _loadWidth;
        private float _rowHeight;
        private float _padding;
        private float _gap;

        private readonly string[] _metricNames = new string[] { "CPU", "GPU", "RAM", "SSD1", "SSD2", "FPS" };
        private readonly float?[] _temperatures = new float?[6];
        private readonly float?[] _loads = new float?[6];

        public OverlayForm(bool openSettingsOnShow, bool openColorPickerOnShow)
        {
            _openSettingsOnShow = openSettingsOnShow;
            _openColorPickerOnShow = openColorPickerOnShow;
            _settings = SettingsStore.Load();
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
            Load += delegate { PlaceTopRight(); RenderToLayeredWindow(); };
            Shown += delegate
            {
                if (_openSettingsOnShow) BeginInvoke(new Action(OpenSettings));
                else if (_openColorPickerOnShow) BeginInvoke(new Action(delegate { using (ColorPickerDialog dialog = new ColorPickerDialog(Color.White, "选择文字填充颜色")) { dialog.TopMost = true; dialog.ShowDialog(); } }));
            };
            MouseDown += DragWindow;
            FormClosed += delegate { DisposeAll(); };

            StartMonitoring();
            _fullscreenTimer = new WinFormsTimer();
            _fullscreenTimer.Interval = 1000;
            _fullscreenTimer.Tick += delegate { CheckFullscreen(); };
            _fullscreenTimer.Start();
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
            menu.Items.Add(new ToolStripSeparator());
            _autoStartItem = new ToolStripMenuItem("开机自动启动");
            _autoStartItem.CheckOnClick = true;
            _autoStartItem.Click += delegate { ToggleAutoStart(); };
            menu.Items.Add(_autoStartItem);
            _rtssStatusItem = new ToolStripMenuItem("游戏内覆盖：检测中…");
            _rtssStatusItem.Enabled = false;
            menu.Items.Add(_rtssStatusItem);
            menu.Opening += delegate { _autoStartItem.Checked = AutoStartTaskExists(); };
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
            using (SettingsForm form = new SettingsForm(_settings))
            {
                form.TopMost = true;
                if (form.ShowDialog() == DialogResult.OK)
                {
                    ApplySettings(form.ResultSettings);
                }
            }
        }

        private void ApplySettings(OverlaySettings settings)
        {
            _settings = settings.Clone();
            SettingsStore.Save(_settings);
            CreateFonts();
            LoadBackgroundImage();
            RecalculateLayout();
            ClampToScreen();
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
                _labelWidth = 0f;
                foreach (string name in _metricNames)
                    _labelWidth = Math.Max(_labelWidth, graphics.MeasureString(name, _labelFont, PointF.Empty, format).Width);
                _tempWidth = graphics.MeasureString("000.0\u00B0C", _valueFont, PointF.Empty, format).Width;
                _loadWidth = graphics.MeasureString("100%", _valueFont, PointF.Empty, format).Width;
                _padding = 5f;
                _gap = 8f;
                _rowHeight = Math.Max(_labelFont.Height, _valueFont.Height) + 4f;
                int width = (int)Math.Ceiling(_padding + _labelWidth + _gap + _tempWidth + _gap + _loadWidth + _padding);
                int height = (int)Math.Ceiling(_padding + _rowHeight * _metricNames.Length + _padding);
                ClientSize = new Size(Math.Max(80, width), Math.Max(40, height));
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
            float y = _padding;
            for (int i = 0; i < _metricNames.Length; i++)
            {
                DrawStyledText(graphics, _metricNames[i], _padding, y, _labelFont, _settings.FillColor);
                string valueText = i == 5 ? FormatFps(_temperatures[i]) : FormatTemperature(_temperatures[i]);
                Color valueColor = i == 5 ? _settings.FillColor : TemperatureColor(_temperatures[i], i);
                DrawStyledText(graphics, valueText, _padding + _labelWidth + _gap, y, _valueFont, valueColor);
                if (i < 3)
                    DrawStyledText(graphics, FormatLoad(_loads[i]), _padding + _labelWidth + _gap + _tempWidth + _gap, y, _valueFont, LoadColor(_loads[i]));
                y += _rowHeight;
            }
        }
        private void DrawStyledText(Graphics graphics, string text, float x, float y, Font font, Color fillColor)
        {
            DrawTextRing(graphics, text, x, y, font, _settings.OuterOutlineColor, 2);
            DrawTextRing(graphics, text, x, y, font, _settings.InnerOutlineColor, 1);
            using (SolidBrush brush = new SolidBrush(fillColor))
                graphics.DrawString(text, font, brush, new PointF(x, y), StringFormat.GenericTypographic);
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

        private void CheckFullscreen()
        {
            if (_rtss == null || !_rtss.IsConnected)
            {
                ShowPanel();
                return;
            }

            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == Handle)
            {
                ShowPanel();
                return;
            }

            RECT rect;
            bool gotBounds = DwmGetWindowAttribute(foreground, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(RECT))) == 0;
            if (!gotBounds && !GetWindowRect(foreground, out rect))
            {
                ShowPanel();
                return;
            }

            Screen target = Screen.FromHandle(foreground);
            Rectangle screen = target == null ? Screen.PrimaryScreen.Bounds : target.Bounds;
            bool fullscreen = rect.Left <= screen.Left + 2 && rect.Top <= screen.Top + 2 && rect.Right >= screen.Right - 2 && rect.Bottom >= screen.Bottom - 2;
            if (fullscreen)
            {
                StringBuilder className = new StringBuilder(128);
                GetClassName(foreground, className, className.Capacity);
                string cls = className.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "NotifyIconOverflowWindow") fullscreen = false;
            }

            if (fullscreen) HidePanel(); else ShowPanel();
        }

        private void ShowPanel()
        {
            if (_panelHidden || !Visible)
            {
                _panelHidden = false;
                ShowQuietly();
                Visible = true;
                TopMost = true;
                RenderToLayeredWindow();
            }
        }

        private void HidePanel()
        {
            if (!_panelHidden)
            {
                _panelHidden = true;
                Visible = false;
            }
        }

        private void ShowQuietly()
        {
            try { Show(); } catch { }
        }

        private void DisposeAll()
        {
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); _timer = null; }
            if (_fullscreenTimer != null) { _fullscreenTimer.Stop(); _fullscreenTimer.Dispose(); _fullscreenTimer = null; }
            if (_computer != null) { try { _computer.Close(); } catch { } _computer = null; }
            if (_rtss != null) { _rtss.Dispose(); _rtss = null; }
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
                _rtss = new RtssOsdWriter();
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

            if (_rtss != null) return _rtss.ReadForegroundFps(processId);
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
            UpdateRtssOsd();
        }

        private void UpdateRtssOsd()
        {
            if (_rtss == null) return;
            StringBuilder text = new StringBuilder();
            text.AppendLine("CPU " + FormatOsdTemperature(_temperatures[0]) + " " + FormatLoad(_loads[0]));
            text.AppendLine("GPU " + FormatOsdTemperature(_temperatures[1]) + " " + FormatLoad(_loads[1]));
            text.AppendLine("RAM " + FormatOsdTemperature(_temperatures[2]) + " " + FormatLoad(_loads[2]));
            text.AppendLine("SSD1 " + FormatOsdTemperature(_temperatures[3]));
            text.AppendLine("SSD2 " + FormatOsdTemperature(_temperatures[4]));
            text.Append("FPS " + FormatFps(_temperatures[5]));
            _rtss.Update(text.ToString());
            if (_rtssStatusItem != null)
            {
                string status = _rtss.IsConnected ? "游戏内覆盖：已连接" : "游戏内覆盖：等待 RTSS";
                if (_rtssStatusItem.Text != status) _rtssStatusItem.Text = status;
            }
        }

        private static string FormatOsdTemperature(float? value)
        {
            return value.HasValue ? value.Value.ToString("0.0") + "C" : "--";
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

    internal sealed class RtssOsdWriter : IDisposable
    {
        private const uint FileMapAllAccess = 0x000F001F;
        private const string MapName = "RTSSSharedMemoryV2";
        private const string Owner = "HardwareMonitorOverlay";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenFileMapping(uint desiredAccess, bool inheritHandle, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr fileMapping, uint desiredAccess, uint offsetHigh, uint offsetLow, UIntPtr bytesToMap);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UnmapViewOfFile(IntPtr baseAddress);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private IntPtr _mapping;
        private IntPtr _view;
        private int _slot;
        private bool _connected;

        public bool IsConnected { get { return _connected; } }

        public void Update(string text)
        {
            if (!_connected && !TryConnect()) return;
            try
            {
                int version = Marshal.ReadInt32(_view, 4);
                int entrySize = Marshal.ReadInt32(_view, 20);
                int arrayOffset = Marshal.ReadInt32(_view, 24);
                int arraySize = Marshal.ReadInt32(_view, 28);
                if (version < 0x00020000 || entrySize < 512 || arraySize <= 1) { Disconnect(); return; }

                if (_slot == 0)
                {
                    for (int i = 1; i < arraySize; i++)
                    {
                        IntPtr entry = new IntPtr(_view.ToInt64() + arrayOffset + i * entrySize);
                        string owner = ReadAnsi(entry, 256, 256);
                        if (owner.Length == 0)
                        {
                            WriteAnsi(entry, 256, 256, Owner);
                            _slot = i;
                            break;
                        }
                        if (owner == Owner)
                        {
                            _slot = i;
                            break;
                        }
                    }
                }

                if (_slot == 0) return;
                IntPtr osdEntry = new IntPtr(_view.ToInt64() + arrayOffset + _slot * entrySize);
                int textOffset = version >= 0x00020007 ? 512 : 0;
                int textLength = version >= 0x00020007 ? 4095 : 255;
                WriteAnsi(osdEntry, textOffset, textLength, text);
                int frame = Marshal.ReadInt32(_view, 32);
                Marshal.WriteInt32(_view, 32, frame + 1);
            }
            catch { Disconnect(); }
        }
        public float? ReadForegroundFps(uint foregroundProcessId)
        {
            if (!_connected && !TryConnect()) return null;
            try
            {
                int appEntrySize = Marshal.ReadInt32(_view, 8);
                int appArrayOffset = Marshal.ReadInt32(_view, 12);
                int appArraySize = Marshal.ReadInt32(_view, 16);
                if (appEntrySize < 300 || appEntrySize > 200000 || appArraySize <= 0 || appArraySize > 4096) return null;
                uint target = foregroundProcessId;
                uint ownProcessId = (uint)Process.GetCurrentProcess().Id;
                if (target == 0 || target == ownProcessId)
                {
                    int lastForegroundProcessId = Marshal.ReadInt32(_view, 68);
                    if (lastForegroundProcessId > 0) target = (uint)lastForegroundProcessId;
                }
                if (target == 0) return null;
                long baseAddress = _view.ToInt64();
                for (int i = 0; i < appArraySize; i++)
                {
                    IntPtr entry = new IntPtr(baseAddress + appArrayOffset + (long)i * appEntrySize);
                    int processId = Marshal.ReadInt32(entry, 0);
                    if (processId <= 0 || (uint)processId != target) continue;
                    int time0 = Marshal.ReadInt32(entry, 268);
                    int time1 = Marshal.ReadInt32(entry, 272);
                    int frames = Marshal.ReadInt32(entry, 276);
                    int frameTime = Marshal.ReadInt32(entry, 280);
                    if (frameTime > 0) return 1000000f / frameTime;
                    if (time0 > 0 && time1 > time0 && frames > 0) return 1000f * frames / (time1 - time0);
                    return null;
                }
            }
            catch { Disconnect(); }
            return null;
        }

        private bool TryConnect()
        {
            Disconnect();
            IntPtr mapping = OpenFileMapping(FileMapAllAccess, false, MapName);
            if (mapping == IntPtr.Zero) return false;
            IntPtr view = MapViewOfFile(mapping, FileMapAllAccess, 0, 0, UIntPtr.Zero);
            if (view == IntPtr.Zero) { CloseHandle(mapping); return false; }
            int signature = Marshal.ReadInt32(view, 0);
            int version = Marshal.ReadInt32(view, 4);
            if (signature != 0x53535452 || version < 0x00020000)
            {
                UnmapViewOfFile(view);
                CloseHandle(mapping);
                return false;
            }
            _mapping = mapping;
            _view = view;
            _slot = 0;
            _connected = true;
            return true;
        }

        private static string ReadAnsi(IntPtr baseAddress, int offset, int maxLength)
        {
            List<byte> bytes = new List<byte>();
            for (int i = 0; i < maxLength; i++)
            {
                byte b = Marshal.ReadByte(baseAddress, offset + i);
                if (b == 0) break;
                bytes.Add(b);
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        private static void WriteAnsi(IntPtr baseAddress, int offset, int maxLength, string text)
        {
            for (int i = 0; i < maxLength; i++) Marshal.WriteByte(baseAddress, offset + i, 0);
            byte[] bytes = Encoding.ASCII.GetBytes(text ?? "");
            int count = Math.Min(bytes.Length, maxLength - 1);
            for (int i = 0; i < count; i++) Marshal.WriteByte(baseAddress, offset + i, bytes[i]);
        }
        private void Disconnect()
        {
            _connected = false;
            _slot = 0;
            if (_view != IntPtr.Zero) { try { UnmapViewOfFile(_view); } catch { } _view = IntPtr.Zero; }
            if (_mapping != IntPtr.Zero) { try { CloseHandle(_mapping); } catch { } _mapping = IntPtr.Zero; }
        }

        public void Dispose()
        {
            if (_connected && _slot > 0 && _view != IntPtr.Zero)
            {
                try
                {
                    int entrySize = Marshal.ReadInt32(_view, 20);
                    int arrayOffset = Marshal.ReadInt32(_view, 24);
                    int version = Marshal.ReadInt32(_view, 4);
                    IntPtr entry = new IntPtr(_view.ToInt64() + arrayOffset + _slot * entrySize);
                    WriteAnsi(entry, 0, version >= 0x00020007 ? 4096 : 256, "");
                    WriteAnsi(entry, 256, 256, "");
                    int frame = Marshal.ReadInt32(_view, 32);
                    Marshal.WriteInt32(_view, 32, frame + 1);
                }
                catch { }
            }
            Disconnect();
        }
    }
}
