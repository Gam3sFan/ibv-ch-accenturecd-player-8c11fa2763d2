using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
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

        public static void GetImageFromURL(string imageURL, string destinationPath, bool forceDownload = false, ImageUtility_EventResult callback = null)
        {
            // check if the file already exists in the destination folder
            bool fileAlreadyExists = File.Exists(destinationPath);
            if (forceDownload || !fileAlreadyExists)
            {
                if (fileAlreadyExists)
                {
                    try
                    {
                        // delete the existing file
                        File.Delete(destinationPath);
                    }
                    catch (Exception)
                    {
                        // file can't be downloaded because it's in use!!
                        callback?.Invoke(null);
                        return;
                    }

                    LogTracer.Instance.Trace("Force the download of the file even if it already exists! It will be downloaded and saved locally");
                }
                else
                {
                    LogTracer.Instance.Trace("The local file not exists! It will be downloaded and saved locally");
                }

                // download the image from URL
                WebClient client = new WebClient();
                client.DownloadFileCompleted += (sender, e) =>
                {
                    if (e.Error != null)
                    {
                        LogTracer.Instance.Trace(string.Format("Error downloading the remote file {0}: {1}!", imageURL, e.Error.Message), System.Diagnostics.TraceEventType.Error);
                        callback?.Invoke(null);
                    }
                    else
                    {
                        if (!e.Cancelled)
                        {
                            // rename the file downloaded
                            File.Move(destinationPath + FileUtility.DOWNLOADING_FILE_POSTFIX, destinationPath);

                            // check if the download is completed
                            if (!File.Exists(destinationPath))
                            {
                                LogTracer.Instance.Trace(string.Format("The image {0} is not saved locally!", imageURL), System.Diagnostics.TraceEventType.Error);
                                callback?.Invoke(null);
                                return;
                            }

                            Bitmap bmp = LoadBitmapUnlocked(destinationPath);
                            callback?.Invoke(bmp);
                        }
                        else
                        {
                            callback?.Invoke(null);
                        }
                    }
                };
                client.DownloadFileAsync(new Uri(imageURL), destinationPath + FileUtility.DOWNLOADING_FILE_POSTFIX);
            }
            else
            {
                Bitmap bmp = LoadBitmapUnlocked(destinationPath);
                callback?.Invoke(bmp);
            }
        }
    }
}
