using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ContentDistributionPlayer.Utilities
{
    class RemoteFileDownloader
    {
        public static async Task DownloadAsync(string sourceUrl, string destinationPath, bool overwrite, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl))
                throw new ArgumentException("Source URL is empty.", nameof(sourceUrl));

            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new ArgumentException("Destination path is empty.", nameof(destinationPath));

            if (File.Exists(destinationPath) && !overwrite)
            {
                LogTracer.Instance.Trace(string.Format("A local file version already exists ({0}) and it will not download again", destinationPath));
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

            string tempPath = destinationPath + FileUtility.DOWNLOADING_FILE_POSTFIX;
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            if (File.Exists(destinationPath) && overwrite)
                File.Delete(destinationPath);

            LogTracer.Instance.Trace(string.Format("Download process started for {0}", sourceUrl));

            using (var client = new WebClient())
            using (cancellationToken.Register(() => client.CancelAsync()))
            {
                await client.DownloadFileTaskAsync(new Uri(sourceUrl), tempPath);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(tempPath))
                throw new FileNotFoundException("Unable to find the downloaded temporary file.", tempPath);

            if (File.Exists(destinationPath))
                File.Delete(destinationPath);

            File.Move(tempPath, destinationPath);

            if (!File.Exists(destinationPath))
                throw new IOException("Unable to download the file locally: " + destinationPath);
        }
    }
}
