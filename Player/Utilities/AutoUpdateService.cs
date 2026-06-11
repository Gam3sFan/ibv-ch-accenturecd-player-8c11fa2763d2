using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ContentDistributionPlayer.Utilities
{
    class AutoUpdateService
    {
        public string LastState { get; private set; } = "Not checked";
        public string LastInstallScriptPath { get; private set; }

        public async Task<string> CheckAndStageAsync(string currentVersion, string manifestUrl, string contentsFolder, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                LastState = "Manifest URL is empty";
                return null;
            }

            try
            {
                LastState = "Checking";
                LastInstallScriptPath = null;
                LogTracer.Instance.Trace("Auto-update check started: " + manifestUrl);

                string xml;
                using (var client = new WebClient())
                using (cancellationToken.Register(() => client.CancelAsync()))
                {
                    xml = await client.DownloadStringTaskAsync(new Uri(manifestUrl));
                }

                var manifest = XDocument.Parse(xml).Root;
                string versionText = manifest?.Element("version")?.Value;
                string zipUrl = manifest?.Element("zipUrl")?.Value;
                string sha256 = manifest?.Element("sha256")?.Value;

                if (string.IsNullOrWhiteSpace(versionText) || string.IsNullOrWhiteSpace(zipUrl))
                {
                    LastState = "Invalid manifest";
                    LogTracer.Instance.Trace("Auto-update manifest must contain version and zipUrl", TraceEventType.Warning);
                    return null;
                }

                var latest = new Version(versionText);
                var current = new Version(currentVersion);
                if (latest <= current)
                {
                    LastState = "Already current";
                    LogTracer.Instance.Trace(string.Format("Auto-update skipped. Current version {0}, latest {1}", current, latest));
                    return null;
                }

                string updateRoot = Path.Combine(contentsFolder, "updates", latest.ToString());
                Directory.CreateDirectory(updateRoot);

                string zipPath = Path.Combine(updateRoot, "Player-" + latest + ".zip");
                await RemoteFileDownloader.DownloadAsync(zipUrl, zipPath, true, cancellationToken);

                if (!string.IsNullOrWhiteSpace(sha256))
                    VerifySha256(zipPath, sha256);

                string extractPath = Path.Combine(updateRoot, "package");
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                ZipFile.ExtractToDirectory(zipPath, extractPath);
                LastInstallScriptPath = WriteInstallScript(updateRoot, extractPath);

                LastState = "Staged " + latest;
                LogTracer.Instance.Trace("Auto-update staged at " + updateRoot);
                return LastInstallScriptPath;
            }
            catch (Exception ex)
            {
                LastState = "Error: " + ex.Message;
                LogTracer.Instance.Trace("Auto-update error: " + ex.Message, TraceEventType.Error);
                return null;
            }
        }

        private static void VerifySha256(string path, string expected)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(actual, expected.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Auto-update package SHA256 mismatch.");
            }
        }

        private static string WriteInstallScript(string updateRoot, string extractPath)
        {
            string scriptPath = Path.Combine(updateRoot, "install-update.cmd");
            string appPath = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            int currentPid = Process.GetCurrentProcess().Id;
            string content = string.Format(CultureInfo.InvariantCulture,
@"@echo off
setlocal
set ""SRC={0}""
set ""DST={1}""
set ""PLAYER_PID={2}""
echo Waiting for Player.exe to exit...
:wait
tasklist /FI ""PID eq %PLAYER_PID%"" | find /I ""%PLAYER_PID%"" >nul
if not errorlevel 1 (
  timeout /t 2 /nobreak >nul
  goto wait
)
xcopy ""%SRC%\*"" ""%DST%\"" /E /Y /I
start """" ""%DST%\Player.exe""
", extractPath, appPath, currentPid);
            File.WriteAllText(scriptPath, content);
            return scriptPath;
        }
    }
}
