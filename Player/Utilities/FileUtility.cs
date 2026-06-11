using System;
using System.IO;
using System.Threading.Tasks;

namespace ContentDistributionPlayer.Utilities
{
    class FileUtility
    {
        public static string DOWNLOADING_FILE_POSTFIX = "_downloading";

        public static async void PurgeAllUnterminatedDownloadFiles(string folder)
        {
            // now purge all local file having the postfix FileUtility.DOWNLOADING_FILE_POSTFIX
            if (Directory.Exists(folder))
            {
                LogTracer.Instance.Trace(string.Format("Delete all the local file inside the folder {0} having the name ended with {1}", folder, DOWNLOADING_FILE_POSTFIX));

                string[] files = Directory.GetFiles(folder, "*" + DOWNLOADING_FILE_POSTFIX);
                if (files != null)
                {
                    foreach (string file in files)
                    {
                        int retry = 0;
                        bool exit = false;

                        do
                        {
                            try
                            {
                                LogTracer.Instance.Trace(string.Format("Deleting file {0}...", file));

                                File.Delete(file);

                                LogTracer.Instance.Trace(string.Format("File {0} deleted!", file));

                                exit = true;
                            }
                            catch (Exception)
                            {
                                retry++;
                                await Task.Delay(1000);
                            }
                        } while (!exit && retry < 100);
                    }
                }
            }
        }
    }
}
