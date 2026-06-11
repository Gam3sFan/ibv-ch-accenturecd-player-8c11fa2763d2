using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ContentDistributionPlayer.Utilities
{
    public delegate void ImageUtility_EventResult(Bitmap image);

    class ImageUtility
    {
        /// <summary>
        /// Carica un Bitmap SENZA tenere il file bloccato. Image.FromFile mantiene invece
        /// il file aperto per tutta la vita dell'immagine, impedendone la cancellazione o
        /// il ri-download (causa dei lock e dei retry sparsi nel codice).
        /// </summary>
        public static Bitmap LoadBitmapUnlocked(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var tmp = Image.FromStream(fs))
            {
                return new Bitmap(tmp);
            }
        }

        public static async void GetImageFromURL(string imageURL, string destinationPath, bool forceDownload = false, ImageUtility_EventResult callback = null)
        {
            try
            {
                await RemoteFileDownloader.DownloadAsync(imageURL, destinationPath, forceDownload, CancellationToken.None);
                Bitmap bmp = LoadBitmapUnlocked(destinationPath);
                callback?.Invoke(bmp);
            }
            catch (Exception ex)
            {
                LogTracer.Instance.Trace(string.Format("Error downloading or loading the image {0}: {1}!", imageURL, ex.Message), System.Diagnostics.TraceEventType.Error);
                callback?.Invoke(null);
            }
        }
    }
}
