using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ContentDistributionPlayer.Utilities
{
    class DocumentsUtility
    {
        public enum DocumentTypes
        {
            None,
            PowerPoint,
            Word,
            Excel,
            PDF,
            Image,
            Video,
            Website
        };

        public static string POWERPOINT_RESOURCE_TYPE = "powerpoint";
        public static string WORD_RESOURCE_TYPE = "word";
        public static string EXCEL_RESOURCE_TYPE = "excel";
        public static string IMAGE_RESOURCE_TYPE = "image";
        public static string VIDEO_RESOURCE_TYPE = "video";
        public static string PDF_RESOURCE_TYPE = "pdf";
        public static string WEBSITE_RESOURCE_TYPE = "website";

        public static string[] PowerPointExtensions { get; } = new string[] { "ppt", "pptx" };
        public static string[] WordExtensions { get; } = new string[] { "doc", "docx" };
        public static string[] ExcelExtensions { get; } = new string[] { "xls", "xlsx" };
        public static string[] ImageExtensions { get; } = new string[] { "jpg", "jpeg", "png", "gif" };
        public static string[] PDFExtensions { get; } = new string[] { "pdf" };
        public static string[] VideoExtensions { get; } = new string[] { "mp4", "mkv", "ogg", "flv", "mov" };

        private static string[] _allDocumentsExtensions
        {
            get
            {
                List<string> l = new List<string>();
                l.AddRange(PowerPointExtensions);
                l.AddRange(WordExtensions);
                l.AddRange(ExcelExtensions);
                l.AddRange(ImageExtensions);
                l.AddRange(PDFExtensions);
                l.AddRange(VideoExtensions);
                return l.ToArray();
            }
        }
        public static string[] AllDocumentsExtensions { get; } = _allDocumentsExtensions;

        private static string GetFileExtension(string file)
        {
            if (!string.IsNullOrEmpty(file))
            {
                var extension = Path.GetExtension(file);
                if (!string.IsNullOrEmpty(extension))
                {
                    extension = extension.Substring(1).ToLower(); // remove the dot char
                    return extension;
                }
            }
            return null;
        }

        public static bool IsDocumentFile(string file)
        {
            var extension = GetFileExtension(file);
            if (!string.IsNullOrEmpty(extension))
            {
               string element = Array.Find<string>(AllDocumentsExtensions, (x) => x.Equals(extension));

                // check if the file has a valid document extension
                return !string.IsNullOrEmpty(element);
            }

            return false;
        }

        public static bool IsPowerPoint(string file)
        {
            var extension = GetFileExtension(file);
            if (!string.IsNullOrEmpty(extension))
            {
                string element = Array.Find<string>(PowerPointExtensions, (x) => x.Equals(extension));

                // check if the file has a valid document extension
                return !string.IsNullOrEmpty(element);
            }

            return false;
        }

        public static bool IsWord(string file)
        {
            var extension = GetFileExtension(file);
            if (!string.IsNullOrEmpty(extension))
            {
                string element = Array.Find<string>(WordExtensions, (x) => x.Equals(extension));

                // check if the file has a valid document extension
                return !string.IsNullOrEmpty(element);
            }

            return false;
        }

        public static bool IsExcel(string file)
        {
            var extension = GetFileExtension(file);
            if (!string.IsNullOrEmpty(extension))
            {
                string element = Array.Find<string>(ExcelExtensions, (x) => x.Equals(extension));

                // check if the file has a valid document extension
                return !string.IsNullOrEmpty(element);
            }

            return false;
        }

        public static bool IsImage(string file)
        {
            var extension = GetFileExtension(file);
            if (!string.IsNullOrEmpty(extension))
            {
                string element = Array.Find<string>(ImageExtensions, (x) => x.Equals(extension));

                // check if the file has a valid document extension
                return !string.IsNullOrEmpty(element);
            }

            return false;
        }

        public static bool IsPDF(string file)
        {
            var extension = GetFileExtension(file);
            if (!string.IsNullOrEmpty(extension))
            {
                string element = Array.Find<string>(PDFExtensions, (x) => x.Equals(extension));

                // check if the file has a valid document extension
                return !string.IsNullOrEmpty(element);
            }

            return false;
        }

        public static bool IsVideo(string file)
        {
            var extension = GetFileExtension(file);
            if (!string.IsNullOrEmpty(extension))
            {
                string element = Array.Find<string>(VideoExtensions, (x) => x.Equals(extension));

                // check if the file has a valid document extension
                return !string.IsNullOrEmpty(element);
            }

            return false;
        }

        public static bool IsWebsite(string file)
        {
            return GetDocumentTypeByFileName(file) == DocumentTypes.Website;
        }

        public static bool IsOfficeDocument(string file)
        {
            return IsPowerPoint(file) || IsWord(file) || IsExcel(file);
        }

        public static bool IsOfficeDocument(DocumentTypes type)
        {
            return type == DocumentTypes.PowerPoint || type == DocumentTypes.Word || type == DocumentTypes.Excel;
        }

        public static bool IsPowerPoint(DocumentTypes type)
        {
            return type == DocumentTypes.PowerPoint;
        }

        public static bool IsVideo(DocumentTypes type)
        {
            return type == DocumentTypes.Video;
        }

        public static bool IsWebsite(DocumentTypes type)
        {
            return type == DocumentTypes.Website;
        }

        public static bool IsImage(DocumentTypes type)
        {
            return type == DocumentTypes.Image;
        }

        public static DocumentTypes GetDocumentTypeByFileName(string fileName)
        {
            DocumentTypes docType = DocumentTypes.None;

            if (IsPowerPoint(fileName))
            {
                docType = DocumentTypes.PowerPoint;
            }
            else if (IsWord(fileName))
            {
                docType = DocumentTypes.Word;
            }
            else if (IsExcel(fileName))
            {
                docType = DocumentTypes.Excel;
            }
            else if (IsVideo(fileName))
            {
                docType = DocumentTypes.Video;
            }
            else if (IsImage(fileName))
            {
                docType = DocumentTypes.Image;
            }
            else if (!string.IsNullOrEmpty(fileName) &&
                    (fileName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     fileName.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                     fileName.StartsWith("file://", StringComparison.OrdinalIgnoreCase)))
            {
                docType = DocumentTypes.Website;
            }

            return docType;
        }

        public static void KillAllOfficeProcesses()
        {
            try
            {
                Process[] ppt = Process.GetProcessesByName("POWERPNT");
                foreach (Process temp in ppt)
                    temp.Kill();
            }
            catch(Exception ex)
            {
                LogTracer.Instance.Trace(string.Format("Error killing Office processes: {0}", ex.Message), TraceEventType.Error);
            }
        }
    }
}
