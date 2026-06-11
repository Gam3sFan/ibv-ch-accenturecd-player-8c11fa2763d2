using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ContentDistributionPlayer.Components
{
    class SceneContentFile
    {
        public string FileName { get; set; }
        public int ContentIndex { get; set; }
        public JObject ContentData { get; set; }
        public int SceneIndex { get; set; }
    }
}
