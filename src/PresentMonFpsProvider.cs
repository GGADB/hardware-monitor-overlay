using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace HardwareMonitorOverlay
{
    internal sealed class PresentMonFpsProvider : IDisposable
    {
        private readonly object _sync = new object();
        private Process _process;
        private Thread _stdoutThread;
        private Thread _stderrThread;
        private volatile bool _disposed;
        private volatile uint _targetPid;
        private float? _fps;
        private DateTime _lastSampleUtc = DateTime.MinValue;
        private const string SessionName = "HardwareMonitorOverlay";

        public float? CurrentFps
        {
            get
            {
                lock (_sync)
                {
                    if (_fps.HasValue && (DateTime.UtcNow - _lastSampleUtc).TotalSeconds > 2.5) return null;
                    return _fps;
                }
            }
        }

        public void Start()
        {
            string path = EnsurePresentMon();
            if (path == null)
            {
                OverlayForm.WriteLog(new Exception("PresentMon resource could not be extracted."));
                return;
            }
            StartProcess();
        }

        public void UpdateForegroundPid(uint processId)
        {
            if (_disposed) return;

            if (processId == 0 || processId == (uint)Process.GetCurrentProcess().Id)
            {
                lock (_sync)
                {
                    _targetPid = 0;
                    _fps = null;
                    _lastSampleUtc = DateTime.MinValue;
                }
                return;
            }

            string processName = null;
            try { processName = Process.GetProcessById((int)processId).ProcessName; }
            catch { }
            if (IsShellProcess(processName))
            {
                lock (_sync)
                {
                    _targetPid = 0;
                    _fps = null;
                    _lastSampleUtc = DateTime.MinValue;
                }
                return;
            }

            lock (_sync)
            {
                if (processId != _targetPid)
                {
                    _targetPid = processId;
                    _fps = null;
                    _lastSampleUtc = DateTime.MinValue;
                }
            }
            StartProcess();
        }

        private static bool IsShellProcess(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower == "explorer" || lower == "shellexperiencehost" || lower == "startmenuexperiencehost" || lower == "searchhost" || lower == "textinputhost" || lower == "lockapp";
        }

        private void StartProcess()
        {
            if (_disposed) return;
            lock (_sync)
            {
                if (_process != null && !_process.HasExited) return;
            }
            StopProcess();
            try
            {
                string exe = EnsurePresentMon();
                if (exe == null) return;
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = exe;
                info.Arguments = "--output_stdout --no_console_stats --v1_metrics --session_name " + SessionName + " --stop_existing_session";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;

                Process process = new Process();
                process.StartInfo = info;
                process.Start();
                lock (_sync) _process = process;

                _stdoutThread = new Thread(delegate() { ReadStdout(process); });
                _stdoutThread.IsBackground = true;
                _stdoutThread.Name = "PresentMon FPS reader";
                _stdoutThread.Start();

                _stderrThread = new Thread(delegate() { DrainStderr(process); });
                _stderrThread.IsBackground = true;
                _stderrThread.Name = "PresentMon stderr reader";
                _stderrThread.Start();
            }
            catch (Exception ex)
            {
                OverlayForm.WriteLog(ex);
            }
        }

        private void ReadStdout(Process process)
        {
            try
            {
                using (TextFieldParser parser = new TextFieldParser(process.StandardOutput))
                {
                    parser.TextFieldType = FieldType.Delimited;
                    parser.SetDelimiters(new string[] { "," });
                    parser.HasFieldsEnclosedInQuotes = true;
                    parser.TrimWhiteSpace = false;

                    string[] header = null;
                    int pidIndex = -1;
                    int presentMsIndex = -1;
                    int displayMsIndex = -1;
                    while (!_disposed)
                    {
                        string[] first;
                        try { first = parser.ReadFields(); }
                        catch (MalformedLineException) { continue; }
                        if (first == null) return;
                        pidIndex = Array.FindIndex(first, delegate(string s) { return string.Equals(s, "ProcessID", StringComparison.OrdinalIgnoreCase); });
                        presentMsIndex = Array.FindIndex(first, delegate(string s) { return string.Equals(s, "MsBetweenPresents", StringComparison.OrdinalIgnoreCase); });
                        displayMsIndex = Array.FindIndex(first, delegate(string s) { return string.Equals(s, "MsBetweenDisplayChange", StringComparison.OrdinalIgnoreCase); });
                        if (pidIndex >= 0 && presentMsIndex >= 0)
                        {
                            header = first;
                            break;
                        }
                    }
                    if (header == null) return;

                    while (!_disposed)
                    {
                        string[] fields;
                        try { fields = parser.ReadFields(); }
                        catch (MalformedLineException) { continue; }
                        if (fields == null) break;
                        if (fields.Length <= Math.Max(pidIndex, presentMsIndex)) continue;

                        int pid;
                        if (!int.TryParse(fields[pidIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out pid)) continue;
                        if ((uint)pid != _targetPid) continue;
                        double milliseconds = 0;
                        if (!double.TryParse(fields[presentMsIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out milliseconds) || milliseconds <= 0)
                        {
                            if (displayMsIndex >= 0 && fields.Length > displayMsIndex)
                                double.TryParse(fields[displayMsIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out milliseconds);
                        }
                        if (milliseconds <= 0 || milliseconds > 5000) continue;

                        double instantaneous = 1000.0 / milliseconds;
                        if (instantaneous < 1 || instantaneous > 1000) continue;
                        lock (_sync)
                        {
                            _fps = _fps.HasValue ? (float)(_fps.Value * 0.8 + instantaneous * 0.2) : (float)instantaneous;
                            _lastSampleUtc = DateTime.UtcNow;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_disposed) OverlayForm.WriteLog(ex);
            }
        }

        private static void DrainStderr(Process process)
        {
            try
            {
                using (StreamReader reader = process.StandardError)
                {
                    int logged = 0;
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (line.IndexOf("is already running", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (line.IndexOf("Use --session_name", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (logged < 3)
                        {
                            OverlayForm.WriteLog(new Exception("PresentMon: " + line));
                            logged++;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static string EnsurePresentMon()
        {
            string directory = Path.GetDirectoryName(Application.ExecutablePath);
            string path = Path.Combine(directory, "PresentMon.exe");
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string resource = assembly.GetManifestResourceNames().FirstOrDefault(delegate(string name) { return name.EndsWith("PresentMon.exe", StringComparison.OrdinalIgnoreCase); });
                if (resource == null) return File.Exists(path) ? path : null;
                using (Stream input = assembly.GetManifestResourceStream(resource))
                {
                    if (input == null) return File.Exists(path) ? path : null;
                    FileInfo existing = new FileInfo(path);
                    if (existing.Exists && existing.Length == input.Length) return path;
                    try
                    {
                        using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                            input.CopyTo(output);
                    }
                    catch
                    {
                        if (existing.Exists && existing.Length > 500000) return path;
                        throw;
                    }
                }
                return path;
            }
            catch (Exception ex)
            {
                OverlayForm.WriteLog(ex);
                return File.Exists(path) && new FileInfo(path).Length > 500000 ? path : null;
            }
        }

        private void StopProcess()
        {
            Process process;
            lock (_sync)
            {
                process = _process;
                _process = null;
                _fps = null;
                _lastSampleUtc = DateTime.MinValue;
            }
            if (process == null) return;
            try
            {
                if (!process.HasExited) process.Kill();
                process.WaitForExit(2000);
            }
            catch
            {
            }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            StopProcess();
        }
    }
}
