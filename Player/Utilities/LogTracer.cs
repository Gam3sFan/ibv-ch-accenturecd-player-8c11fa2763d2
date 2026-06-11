using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ContentDistributionPlayer.Utilities
{
    public sealed class LogTracer
    {
        private static string logFolder = @"log";
        private static string logFilePrefix = @"log";
        private static bool logRotation = true;
        private static int holdDays = 30;
        private static SourceLevels _minimumLevel = SourceLevels.All;

        private static string _baseLogFolder;
        public static void Init(string baseLogFolder)
        {
            _baseLogFolder = baseLogFolder;
        }

        public static void SetMinimumLevel(string level)
        {
            switch ((level ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "critical":
                    _minimumLevel = SourceLevels.Critical;
                    break;
                case "error":
                    _minimumLevel = SourceLevels.Error;
                    break;
                case "warning":
                    _minimumLevel = SourceLevels.Warning;
                    break;
                case "information":
                case "info":
                    _minimumLevel = SourceLevels.Information;
                    break;
                case "verbose":
                    _minimumLevel = SourceLevels.Verbose;
                    break;
                case "off":
                    _minimumLevel = SourceLevels.Off;
                    break;
                default:
                    _minimumLevel = SourceLevels.All;
                    break;
            }

            if (instance != null && instance._logTraceSource != null)
                instance._logTraceSource.Switch.Level = _minimumLevel;
        }

        private static readonly object _instanceLock = new object();
        private static volatile LogTracer instance = null;
        public static LogTracer Instance
        {
            get
            {
                // double-checked locking: l'inizializzazione veniva chiamata da più thread
                // (UI, callback MQTT, download) creando potenzialmente istanze multiple.
                if (instance != null)
                    return instance;

                lock (_instanceLock)
                {
                    if (instance != null)
                        return instance;

                    var newInstance = new LogTracer();

                    // Initialize the log tracer
                    // The single string passed into the constructor here is the name of the trace source
                    newInstance._logTraceSource = new TraceSource(string.Format("Content Distribution v{0}", MainForm.APP_VERSION));
                    newInstance._logTraceSource.Switch.Level = _minimumLevel;

                    // create the log folder if doesn't exist
                    string logPath = Path.Combine((string.IsNullOrEmpty(_baseLogFolder) ? AppDomain.CurrentDomain.BaseDirectory : _baseLogFolder), logFolder);
                    if (!Directory.Exists(logPath))
                    {
                        // create the log directory
                        Directory.CreateDirectory(logPath);
                    }

                    string strToday = DateTime.Now.ToString("yyyyMMdd");
                    var enCultureInfo = new CultureInfo("en-US");
                    DateTime now = DateTime.ParseExact(strToday, "yyyyMMdd", enCultureInfo);

                    string fileName = Path.Combine(logPath, logFilePrefix);
                    if (logRotation)
                    {
                        fileName = fileName + "_" + strToday;
                    }

                    newInstance._logTraceSource.Listeners.Add(new TextWriterTraceListener(fileName + ".txt"));

                    if (logRotation)
                    {
                        PurgeOldLogFiles(logPath, now, enCultureInfo);
                    }

                    // l'istanza viene pubblicata SOLO ora, a costruzione completata
                    instance = newInstance;
                    return instance;
                }
            }
        }

        private static void PurgeOldLogFiles(string logPath, DateTime now, CultureInfo enCultureInfo)
        {
            // remove all log files older than holdDays.
            // robusto: un file che non rispetta lo schema "<prefix>_yyyyMMdd" viene ignorato
            // invece di far crashare l'avvio dell'app (DateTime.ParseExact -> eccezione).
            try
            {
                string[] files = Directory.GetFiles(logPath);
                if (files == null)
                    return;

                foreach (string file in files)
                {
                    try
                    {
                        // Path.GetFileNameWithoutExtension gestisce il separatore di OS (no '\\' hardcoded)
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrEmpty(name))
                            continue;

                        int pos = name.IndexOf(logFilePrefix, StringComparison.Ordinal);
                        if (pos == -1)
                            continue;

                        // parte data dopo il prefisso (es. "log_20260611" -> "20260611")
                        string datePart = name.Substring(pos + logFilePrefix.Length).TrimStart('_');

                        if (!DateTime.TryParseExact(datePart, "yyyyMMdd", enCultureInfo, DateTimeStyles.None, out DateTime fileDate))
                            continue;

                        if ((now - fileDate).TotalDays > holdDays)
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        // un singolo file problematico non deve bloccare l'avvio
                        System.Diagnostics.Debug.WriteLine("Log rotation error on file: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Log rotation error: " + ex.Message);
            }
        }

        private TraceSource _logTraceSource;

        public void Trace(string message, TraceEventType type = TraceEventType.Information)
        {
            if (_logTraceSource != null)
            {
                string timeStamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss:fff]");
                _logTraceSource.TraceEvent(type, 0, timeStamp + " - " + message);
                _logTraceSource.Flush();
            }
        }

        public void Close()
        {
            // Close the log tracer (if exists)
            if (_logTraceSource != null)
            {
                _logTraceSource.Flush();
                _logTraceSource.Close();
            }
        }

        public void Flush()
        {
            if (_logTraceSource != null)
            {
                _logTraceSource.Flush();
            }
        }
    }
}
