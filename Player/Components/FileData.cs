using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ContentDistributionPlayer.Components
{
    class FileData
    {
        public int ResourceId { get; }
        public string FileName { get; }
        public int Version { get; }
        public string Type { get; set; }

        public string LocalFile { get; set; }

        public FileData(int resourceId, string fileName, int version, string type)
        {
            ResourceId = resourceId;
            FileName = fileName;
            Version = version;
            Type = type;
        }
    }
}
