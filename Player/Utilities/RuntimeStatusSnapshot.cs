using System;

namespace ContentDistributionPlayer.Utilities
{
    class RuntimeStatusSnapshot
    {
        public string AppVersion { get; set; }
        public string ConfigPath { get; set; }
        public string ContentsFolder { get; set; }
        public string NodeEndpoint { get; set; }
        public string ApiUri { get; set; }
        public string ClientIdentity { get; set; }
        public bool TopMostEnabled { get; set; }
        public bool RtcConnected { get; set; }
        public int PresentationId { get; set; }
        public int SceneIndex { get; set; }
        public int SubSceneIndex { get; set; }
        public float WindowsScaleFactor { get; set; }
        public string DpiAwareness { get; set; }
        public string AutoUpdateState { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
