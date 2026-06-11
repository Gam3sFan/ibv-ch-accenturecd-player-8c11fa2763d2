using ContentDistributionPlayer.Components;
using ContentDistributionPlayer.Extensions;
using ContentDistributionPlayer.Utilities;
using LibVLCSharp.Shared;
using Microsoft.Office.Core;
using Microsoft.Office.Interop.PowerPoint;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Transitions;

namespace ContentDistributionPlayer
{
    public partial class MainForm : Form
    {
        public static string APP_VERSION = "1.0.0";

        #region Config data
        private string _nodeJSServerHost;
        private int _nodeJSServerPort;
        private string _nodeJSServerProtocol;
        private int _currentRoom;
        private int _currentMonitor;
        private bool _useFullScreen = false;
        private bool _hideMouseAndTopMostWin = false;
        private bool _topMostEnabled = false;
        private bool _purgePresentationData = true;
        private string _contentsFolder;

        private int _presentationId = -1;
        private bool _needToProcessNextInit = true;
        private bool _isPresentationDataDownloadComplete = false;
        private bool _forceUpdateVersion = false;
        private bool _hasSceneContentError = false;

        private bool _mainSettingsInited = false;
        #endregion

        #region API service call routes
        private const string API_MAIN_SETTINGS = "/room/{0}/monitor/{1}";
        private const string API_PRESENTATION_DATA = "/presentation/{0}/monitor/{1}";
        #endregion

        #region Hot keys definition
        //DLL libraries used to  manage hotkeys
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vlc);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);


        private const int ESC_HOTKEY_ID = 1;
        private const int TOGGLE_TOPMOST_HOTKEY_ID = 2;
        private const int SETTINGS_HOTKEY_ID = 3;
        private const int HOTKEY_MOD_CONTROL = 2;
        private bool _hotkeysRegistered = false;
        /*private const int LEFT_HOTKEY_ID = 2;
        private const int RIGHT_HOTKEY_ID = 3;
        private const int UP_HOTKEY_ID = 4;
        private const int DOWN_HOTKEY_ID = 5;
        private const int ENTER_HOTKEY_ID = 6;*/
        #endregion

        #region Force focus on Main Form
        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);


        //Mouse actions
        private const int MOUSEEVENTF_LEFTDOWN = 0x02;
        private const int MOUSEEVENTF_LEFTUP = 0x04;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x08;
        private const int MOUSEEVENTF_RIGHTUP = 0x10;
        #endregion

        #region Manage the Windows screen scale
        [DllImport("gdi32.dll")]
        static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
        public enum DeviceCap
        {
            VERTRES = 10,
            DESKTOPVERTRES = 117,

            // http://pinvoke.net/default.aspx/gdi32/GetDeviceCaps.html
        }

        private float WindowsScaleFactor { get; set; }
        #endregion

        #region UI elements
        private InfoMessage _infoMessage;
        private AutoUpdateService _autoUpdateService = new AutoUpdateService();
        #endregion

        #region Presentation data
        private const string DEFAULT_BACKGROUND_COLOR = "#000000";
        private string _coverBackgroundColor;
        private string _basePresentationsPath;
        private bool _coverShown = false;

        private PresentationManager _presentationManager;
        private int _sceneIndexToGo = -1;
        private int _subSceneIndexToGo = -1;

        private JObject _presentationLiveContentOnInit;
        
        // data used to manage the display mode session
        Microsoft.Office.Interop.PowerPoint.Application _displayModePowerPointApp;
        private Presentation _displayModePowerPointPresentation;
        private Process _displayModeAppProcessId;

        enum DisplayModeClientMode
        {
            PROGRAM = 0,
            FILE = 1,
            SCREEN_SHARE = 2,
        }
        CancellationTokenSource _currentDisplayModeClientDownload;
        string _currentDisplayModeResourceLocalFile;
        string _currentDisplayModeResourceFileName;
        #endregion

        #region Realtime communications
        private const int RTC_CONNECTION_RETRY_SECONDS = 10;
        private RealtimeCommunication _rtc;
        #endregion



        public MainForm()
        {
            // kill all the office app process
            DocumentsUtility.KillAllOfficeProcesses();

            WindowsScaleFactor = GetWindowsScalingFactor();

            if (!DesignMode)
            {
                // initialize the VLC library
                Core.Initialize();
            }
            
            InitializeComponent();

            /*
            // check if the windows zoom factor is different from 100%

            //var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            //var sb = new StringBuilder();
            //sb.Append("Angular\n");
            //sb.Append(string.Join("\n", Display(DpiType.Angular)));
            //sb.Append("\nEffective\n");
            //sb.Append(string.Join("\n", Display(DpiType.Effective)));
            //sb.Append("\nRaw\n");
            //sb.Append(string.Join("\n", Display(DpiType.Raw)));
            //var sss = sb.ToString();

            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                uint x, y;
                screen.GetDpi(DpiType.Angular, out x, out y);
                if (x != 96 || y != 96)
                {
                    DialogResult msgResult = MessageBox.Show("The Windows screen zoom factor is different from 100%. Set it correctly and try again.");
                    if (msgResult == DialogResult.OK)
                    {
                        // Exit from the player app
                        Load += (s, e) => QuitApp();
                        return;
                    }
                    return;
                }
            }*/



            // read the settings variables
            _currentRoom = Properties.Settings.Default.Room;
            _currentMonitor = Properties.Settings.Default.Monitor;
            _nodeJSServerHost = Properties.Settings.Default.NodeJSHost;
            _nodeJSServerPort = Properties.Settings.Default.NodeJSPort;
            _nodeJSServerProtocol = Properties.Settings.Default.NodeJSProtocol;
            _useFullScreen = Properties.Settings.Default.UseFullScreen;
            if (_useFullScreen || (!_useFullScreen && Properties.Settings.Default.ScreenResolutionWidth != 0 && Properties.Settings.Default.ScreenResolutionHeight != 0))
            {
                _hideMouseAndTopMostWin = true;
            }
            _topMostEnabled = _hideMouseAndTopMostWin;
            
            _contentsFolder = Properties.Settings.Default.ContentsFolder;
            _purgePresentationData = Properties.Settings.Default.PurgePresentationData;

            string configErrorMsg = null;
            if (_currentRoom <= 0)
            {
                configErrorMsg = "Room id is missing in the configuration file!";
            }
            else if (_currentMonitor <= 0)
            {
                configErrorMsg = "Monitor id is missing in the configuration file!";
            }
            else if (string.IsNullOrEmpty(_nodeJSServerHost))
            {
                configErrorMsg = "NodeJS server host is missing in the configuration file!";
            }
            else if (_nodeJSServerPort <= 0)
            {
                configErrorMsg = "NodeJS server port is missing in the configuration file!";
            }
             else if (_nodeJSServerProtocol != "ws" && _nodeJSServerProtocol != "wss")
            {
                configErrorMsg = "NodeJS server protocol is missing or wrong in the configuration file!";
            }

            if (string.IsNullOrEmpty(_contentsFolder))
            {
                configErrorMsg = "Contents folder is missing in the configuration file!";
            }
            else
            {
                // check if te contents folder exists
                if (!Directory.Exists(_contentsFolder))
                {
                    configErrorMsg = string.Format(@"Contents folder '{0}' does not exist!", _contentsFolder);
                }
                else
                {
                    LogTracer.Init(_contentsFolder);
                    LogTracer.SetMinimumLevel(Properties.Settings.Default.LogMinimumLevel);
                    LogTracer.Instance.Trace("Starting application");
                }
            }

            

            if (!string.IsNullOrEmpty(configErrorMsg))
            {
                LogTracer.Instance.Trace(configErrorMsg, TraceEventType.Error);

                DialogResult msgResult = MessageBox.Show(configErrorMsg);
                if (msgResult == DialogResult.OK)
                {
                    // Exit from the player app
                    Load += (s, e) => QuitApp();
                    return;
                }
            }


            // set the UI elements  
            _infoMessage = new InfoMessage(this, lblMessage, imgPreload);

            imgBackgroundLogo.Initialize();
            imgBackgroundLogo.Opacity = 0;
            imgBackgroundLogo.Visible = false;

            imgPreload.Initialize();
            imgPreload.Opacity = 0;
            imgPreload.Visible = false;

       
            imgPresentationBackground.Visible = false;

            //imgTransition.Visible = false;



            // force the first resize to adjust all the UI controls inside the form
            MainForm_SizeChanged(this, EventArgs.Empty);


            // init the presentation manager
            _basePresentationsPath = Path.Combine(_contentsFolder, @"presentations");
            if (!Directory.Exists(_basePresentationsPath))
            {
                // create the presentation directory
                LogTracer.Instance.Trace(string.Format("Create the presentation directory: {0}", _basePresentationsPath));

                Directory.CreateDirectory(_basePresentationsPath);
            }

            PresentationManagerInitialize();

            // init the RTC
            _rtc = new RealtimeCommunication(_nodeJSServerHost, _nodeJSServerPort, _nodeJSServerProtocol, _currentRoom, _currentMonitor);
            _rtc.OnConnectionError += OnRealtimeCommunicationConnectionError;
            _rtc.OnClientNotUpdatedError += OnRealtimeCommunicationClientNotUpdatedError;
            _rtc.OnConnectionSuccess += OnRealtimeCommunicationConnectionSuccess;
            _rtc.OnError += OnRealtimeCommunicationError;
            _rtc.OnInitPresentation += OnRealtimeCommunicationInitPresentation;
            _rtc.OnUnloadPresentation += OnRealtimeCommunicationUnloadPresentation;
            _rtc.OnGotoScene += OnRealtimeCommunicationGotoScene;
            _rtc.OnDisconnected += OnRealtimeCommunicationDisconnected;
            _rtc.OnInitLiveContent += OnRealtimeCommunicationInitLiveContent;
            _rtc.OnUnloadLiveContent += OnRealtimeCommunicationUnloadLiveContent;
            _rtc.OnGotoSceneLiveContent += OnRealtimeCommunicationGotoSceneLiveContent;
            _rtc.OnClientDisplayModeStart += OnRealtimeCommunicationClientDisplayModeStart;
            _rtc.OnClientDisplayModeStop += OnRealtimeCommunicationClientDisplayModeStop;
        }

        /*
        private IEnumerable<string> Display(DpiType type)
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                uint x, y;
                screen.GetDpi(type, out x, out y);
                yield return screen.DeviceName + " - dpiX=" + x + ", dpiY=" + y;
            }
        }*/
        private void MainForm_Load(object sender, EventArgs e)
        {
            if (_hideMouseAndTopMostWin)
            {
                // show the app in foreground (above all)
                ApplyTopMostState(true);
            }

            if (_useFullScreen) 
            { 
                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
            }
            else if (Properties.Settings.Default.ScreenResolutionWidth != 0 && Properties.Settings.Default.ScreenResolutionHeight != 0)
            {
                // set the window size
                this.ClientSize = new Size
                {
                    Width = Properties.Settings.Default.ScreenResolutionWidth,
                    Height = Properties.Settings.Default.ScreenResolutionHeight
                };
                this.StartPosition = FormStartPosition.Manual;
                this.Location = new System.Drawing.Point(0, 0);

                this.FormBorderStyle = FormBorderStyle.None;
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            QuitApp(true);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (_hideMouseAndTopMostWin)
            {
                // hide the mouse cursor
                Cursor.Hide();
                WindowUtility.SetCursorPos(ClientRectangle.Width, ClientRectangle.Height);
            }

            // show the background logo
            imgBackgroundLogo.Visible = true;            
            Transition tBackgroundLogo = new Transition(new TransitionType_Deceleration(500));
            tBackgroundLogo.add(imgBackgroundLogo, "Opacity", 1f);
            tBackgroundLogo.run();

            await Task.Delay(2000);

            try
            {
                // get the screen resolution
                Screen currentScreen = Screen.FromControl(this);
                Rectangle screenArea = currentScreen.WorkingArea;

                // show the message area
                await _infoMessage.ShowMessage("Content Distribution initialization", true, false, new Action(() =>
                {
                    this.Invoke(new Action(async () =>
                    {
                        await Task.Delay(2000);

                        // start the rtc connection
                        StartRTCConnection(screenArea.Width, screenArea.Height);
                    }));
                }));

                // show the preload
                imgPreload.Visible = true;
                Transition tPreload = new Transition(new TransitionType_Acceleration(500));
                tPreload.add(imgPreload, "Opacity", 1f);
                tPreload.run();
            }
            catch(Exception)
            {
                // the Main Form should be closed before (or during) the Task.Delay. 
            }
        }             

        private async void QuitApp(bool cameFromFormClose = false)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => QuitApp(cameFromFormClose)));
                return;
            }
           
            LogTracer.Instance.Trace(string.Format("Quit application - room: {0}   monitor: {1}", _currentRoom, _currentMonitor));

            // unregister all the hot keys
            UnregisterApplicationHotkeys();


            // Close the RTC client
            if (_rtc != null)
            {
                _rtc.OnConnectionError -= OnRealtimeCommunicationConnectionError;
                _rtc.OnClientNotUpdatedError -= OnRealtimeCommunicationClientNotUpdatedError;
                _rtc.OnConnectionSuccess -= OnRealtimeCommunicationConnectionSuccess;
                _rtc.OnError -= OnRealtimeCommunicationError;
                _rtc.OnInitPresentation -= OnRealtimeCommunicationInitPresentation;
                _rtc.OnUnloadPresentation -= OnRealtimeCommunicationUnloadPresentation;
                _rtc.OnGotoScene -= OnRealtimeCommunicationGotoScene;
                _rtc.OnDisconnected -= OnRealtimeCommunicationDisconnected;
                _rtc.OnInitLiveContent -= OnRealtimeCommunicationInitLiveContent;
                _rtc.OnUnloadLiveContent -= OnRealtimeCommunicationUnloadLiveContent;
                _rtc.OnGotoSceneLiveContent -= OnRealtimeCommunicationGotoSceneLiveContent;
                _rtc.OnClientDisplayModeStart -= OnRealtimeCommunicationClientDisplayModeStart;
                _rtc.OnClientDisplayModeStop -= OnRealtimeCommunicationClientDisplayModeStop;
                await _rtc.Close();
            }

            // Clean the presentation manager
            if (_presentationManager != null)
            {
                _presentationManager.OnError -= OnPresentationManagerError;
                _presentationManager.OnGotoSceneComplete -= OnPresentationManagerGotoSceneComplete;
                _presentationManager.OnSceneContentError -= OnPresentationManagerSceneContentError;
                _presentationManager.OnLoadScenesStart -= OnPresentationManagerLoadScenesStart;
                _presentationManager.OnLoadScenesEnd -= OnPresentationManagerLoadScenesEnd;
                _presentationManager.OnRealtimeCommunicationReconnect -= OnPresentationManagerRealtimeCommunicationReconnect;
                _presentationManager.OnDownloadLiveContent -= OnPresentationManagerDownloadLiveContent;
                _presentationManager.OnLoadContentLiveContent -= OnPresentationManagerLoadContentLiveContent;
                _presentationManager.OnShowCover -= OnPresentationManagerShowCover;
                _presentationManager.ClosePresentation();
            }

            // Close the log tracer (if exists)
            LogTracer.Instance.Close();

            try
            {
                // kill all the office app process
                DocumentsUtility.KillAllOfficeProcesses();

                // close a display mode application if it's present
                await OnRealtimeCommunicationClientDisplayModeStop();
            }
            catch(Exception e)
            {
                LogTracer.Instance.Trace(string.Format("Error closing Office processes {0}", e.Message), TraceEventType.Error);
            }

            if (!cameFromFormClose)
            {
                // Now close the app
                System.Windows.Forms.Application.Exit();
            }
        }
        
        private void SetFocusOnMainForm()
        {
            //Call the imported function with the cursor's current position
            uint X = (uint)this.Location.X + 10;
            uint Y = (uint)this.Location.Y + 10;
            mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, X, Y, 0, 0);
        }

        private void MainForm_SizeChanged(object sender, EventArgs e)
        {
            // resize the graphical elements

            try
            {
                // adjust the background logo image
                Size sourceLogoSize = Properties.Resources.logo.Size;
                float logoPercW = 0.4f;
                float logoPercH = 0.3f;
                float logoPercTopOffset = 0.1f;
                float logoScale = Math.Min(
                    1f,
                    Math.Min(
                        (ClientRectangle.Width * logoPercW) / sourceLogoSize.Width,
                        (ClientRectangle.Height * logoPercH) / sourceLogoSize.Height));
                imgBackgroundLogo.Size = new Size(
                    (int)(sourceLogoSize.Width * logoScale),
                    (int)(sourceLogoSize.Height * logoScale));
                imgBackgroundLogo.Left = (ClientRectangle.Width - imgBackgroundLogo.Width) / 2;
                imgBackgroundLogo.Top = (ClientRectangle.Height - imgBackgroundLogo.Height) / 2 - (int)(ClientRectangle.Height * logoPercTopOffset);

                // adjust the message
                var fontPercSize = 0.015f;

                var font = new System.Drawing.Font(lblMessage.Font.FontFamily, (int)(ClientRectangle.Height * fontPercSize));
                lblMessage.Font = font;

                lblMessage.Left = 0;
                lblMessage.Width = ClientRectangle.Width;

                var preloaderPercSize = (imgPresentationBackground.Visible ? 0.018f : 0.028f);
                var preloaderPercSizeBig = 0.03f;
                var preloaderSize = (int)(ClientRectangle.Width * preloaderPercSize);
                var preloaderSizeBig = (int)(ClientRectangle.Width * preloaderPercSizeBig);

                using (Graphics g = CreateGraphics())
                {
                    SizeF size = g.MeasureString(lblMessage.Text, lblMessage.Font, lblMessage.Width);
                    var calculatedHeight = (int)Math.Ceiling(size.Height);
                    lblMessage.Height = (imgPresentationBackground.Visible ? Math.Max(preloaderSizeBig, calculatedHeight) : calculatedHeight);
                }


                var bottomMessagMarginPerc = 0.15f;
                var vMessageMargin = (imgPresentationBackground.Visible ? 0 : (int)(ClientRectangle.Height * bottomMessagMarginPerc));

                var horizontalMessagePaddingPerc = 0.01f;
                var hMessagePadding = (int)(ClientRectangle.Width * horizontalMessagePaddingPerc);

                lblMessage.Padding = new Padding(hMessagePadding, 0, hMessagePadding, 0);


                lblMessage.Top = ClientRectangle.Height - lblMessage.Height - vMessageMargin;
                _infoMessage.MessageYPosition = lblMessage.Top;


                // adjust the preloader            
                imgPreload.Width = preloaderSize;
                imgPreload.Height = preloaderSize;
                if (imgPresentationBackground.Visible)
                {
                    imgPreload.Left = ClientRectangle.Width - imgPreload.Width - (lblMessage.Height - imgPreload.Height) / 2;
                    imgPreload.Top = ClientRectangle.Height - imgPreload.Height - (lblMessage.Height - imgPreload.Height) / 2;
                }
                else
                {
                    imgPreload.Left = (ClientRectangle.Width - imgPreload.Width) / 2;
                    imgPreload.Top = lblMessage.Top - imgPreload.Height - (int)(ClientRectangle.Height * 0.03f);
                }


                
                // resize the scenes contents container
                panScenesContentsContainer.Left = 0;
                panScenesContentsContainer.Top = 0;
                panScenesContentsContainer.Width = ClientRectangle.Width;
                panScenesContentsContainer.Height = ClientRectangle.Height;

                // resize the live content container
                panLiveContentContainer.Left = 0;
                panLiveContentContainer.Top = 0;
                panLiveContentContainer.Width = ClientRectangle.Width;
                panLiveContentContainer.Height = ClientRectangle.Height;
            }
            catch(Exception)
            {
                //await Task.Delay(500);

                //MainForm_SizeChanged(this, EventArgs.Empty);
            }
        }


        #region Keyboard interceptor
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterApplicationHotkeys();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterApplicationHotkeys();
            base.OnHandleDestroyed(e);
        }

        private void RegisterApplicationHotkeys()
        {
            if (_hotkeysRegistered || !IsHandleCreated)
                return;

            // Modifier keys codes: Alt = 1, Ctrl = 2, Shift = 4, Win = 8.
            bool escRegistered = RegisterHotKey(this.Handle, ESC_HOTKEY_ID, 0, (int)Keys.Escape);
            bool topMostRegistered = RegisterHotKey(this.Handle, TOGGLE_TOPMOST_HOTKEY_ID, HOTKEY_MOD_CONTROL, (int)Keys.H);
            bool settingsRegistered = RegisterHotKey(this.Handle, SETTINGS_HOTKEY_ID, HOTKEY_MOD_CONTROL, (int)Keys.G);
            _hotkeysRegistered = true;

            if (!escRegistered || !topMostRegistered || !settingsRegistered)
            {
                LogTracer.Instance.Trace(
                    string.Format("Hotkey registration status - ESC: {0}, CTRL+H: {1}, CTRL+G: {2}", escRegistered, topMostRegistered, settingsRegistered),
                    TraceEventType.Warning);
            }
        }

        private void UnregisterApplicationHotkeys()
        {
            if (!_hotkeysRegistered || !IsHandleCreated)
                return;

            UnregisterHotKey(this.Handle, ESC_HOTKEY_ID);
            UnregisterHotKey(this.Handle, TOGGLE_TOPMOST_HOTKEY_ID);
            UnregisterHotKey(this.Handle, SETTINGS_HOTKEY_ID);
            _hotkeysRegistered = false;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.H))
            {
                ToggleTopMost();
                return true;
            }

            if (keyData == (Keys.Control | Keys.G))
            {
                ShowSettingsPanel();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                if (m.WParam.ToInt32() == ESC_HOTKEY_ID)
                {
                    QuitApp();
                }
                else if (m.WParam.ToInt32() == TOGGLE_TOPMOST_HOTKEY_ID)
                {
                    ToggleTopMost();
                }
                else if (m.WParam.ToInt32() == SETTINGS_HOTKEY_ID)
                {
                    ShowSettingsPanel();
                }
            }

            base.WndProc(ref m);
        }
        #endregion

        private void ToggleTopMost()
        {
            _topMostEnabled = !_topMostEnabled;
            ApplyTopMostState(_topMostEnabled);
            LogTracer.Instance.Trace(string.Format("TopMost toggled: {0}", _topMostEnabled));
        }

        private void ApplyTopMostState(bool enabled)
        {
            TopMost = enabled;
            if (enabled)
                BringToFront();
        }

        private void ShowSettingsPanel()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ShowSettingsPanel));
                return;
            }

            using (var form = new SettingsForm(CreateRuntimeStatusSnapshot(), _autoUpdateService, InstallStagedUpdate))
            {
                LogTracer.Instance.Trace("Settings panel opened");
                bool wasTopMost = TopMost;
                TopMost = false;
                try
                {
                    if (form.ShowDialog(this) == DialogResult.OK)
                    {
                        LogTracer.Instance.Trace("Settings panel saved Player.exe.config");
                        MessageBox.Show(
                            "Settings saved. Restart the player to apply connection, room, monitor and window changes.",
                            "Settings saved",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
                finally
                {
                    ApplyTopMostState(_topMostEnabled && wasTopMost);
                }
            }
        }

        private RuntimeStatusSnapshot CreateRuntimeStatusSnapshot()
        {
            return new RuntimeStatusSnapshot
            {
                AppVersion = APP_VERSION,
                ConfigPath = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile,
                ContentsFolder = _contentsFolder,
                NodeEndpoint = string.Format("{0}://{1}:{2}", _nodeJSServerProtocol, _nodeJSServerHost, _nodeJSServerPort),
                ApiUri = APIService.API_URI ?? "",
                ClientIdentity = string.Format("R{0}_M{1}", _currentRoom, _currentMonitor),
                TopMostEnabled = _topMostEnabled,
                RtcConnected = _rtc != null && _rtc.IsConnected,
                PresentationId = _presentationId,
                SceneIndex = _presentationManager != null ? _presentationManager.GetCurrentSceneIndex() : -1,
                SubSceneIndex = _presentationManager != null ? _presentationManager.GetCurrentSubSceneIndex() : -1,
                WindowsScaleFactor = WindowsScaleFactor,
                DpiAwareness = "PerMonitorAware",
                AutoUpdateState = _autoUpdateService != null ? _autoUpdateService.LastState : "Unavailable"
            };
        }


        #region Realtime Communications functions
        private async void StartRTCConnection(int screenWidth, int screenHeight)
        {
            if (_rtc == null)
                return;

            await _infoMessage.ShowMessage("Connecting to server", true, false, new Action(() =>
            {
                _rtc.Connect(RTC_CONNECTION_RETRY_SECONDS, screenWidth, screenHeight);
            }));
        }

        private void OnRealtimeCommunicationConnectionError(string message)
        {
            this.Invoke(new Action(async () =>
            {
                await _infoMessage.ShowErrorMessage("Server connection error: " + message);
            }));
        }

        private void OnRealtimeCommunicationClientNotUpdatedError()
        {
            this.Invoke(new Action(async () =>
            {
                _forceUpdateVersion = true;
                var errMsg = string.Format("The client app version needs to be updated! Current version: {0}", APP_VERSION);
                LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                await _infoMessage.ShowErrorMessage(errMsg);
            }));
        }

        private void OnRealtimeCommunicationConnectionSuccess(JObject settings)
        {
            _ = this.Invoke(new Action(async () =>
            {
                if (_forceUpdateVersion)
                    return;

                if (_presentationId > 0)
                {
                    await _infoMessage.HideMessage();
                    return;
                }

                if (settings == null)
                {
                    var errMsg = @"Realtime communication connection data result cannot be empty!";
                    LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                    await _infoMessage.ShowErrorMessage(errMsg);
                    return;
                }

                
                APIService.API_URI = settings.Get<string>("apiURI");
                if (string.IsNullOrEmpty(APIService.API_URI))
                {
                    var errMsg = @"The API uri of the Laravel server cannot be empty!";
                    LogTracer.Instance.Trace(errMsg, System.Diagnostics.TraceEventType.Error);
                    await _infoMessage.ShowErrorMessage(errMsg);
                    return;
                }

                // check if there is a display mode running on this client
                var displayModeClients = settings.Get<JArray>("displayModeClients");
                JObject displayModeSessionOnInit = null;
                if (displayModeClients != null)
                {
                    var jToken = displayModeClients.SelectToken("$.[?(@.monitorIndex==" + _currentMonitor + ")]");
                    if (jToken != null)
                    {
                        displayModeSessionOnInit = jToken.ToObject<JObject>();
                    }
                }

                GetMainSettings(() =>
                {
                    if (displayModeSessionOnInit != null)
                    {
                        OnRealtimeCommunicationClientDisplayModeStart(displayModeSessionOnInit);
                    }
                });
                
            }));
        }

        private async void GetMainSettings(Action completed = null)
        {
            // call the Laravel API to get mainSettings
            string serviceUrl = string.Format(API_MAIN_SETTINGS, _currentRoom, _currentMonitor);
            _ = await APIService.CallGetAsync(serviceUrl, (result) =>
            {
                _mainSettingsInited = true;

                this.Invoke(new Action(() =>
                {
                    ShowCover(result, () =>
                    {
                        completed?.Invoke();
                    });
                }));
            },
            (message) =>
            {
                _mainSettingsInited = false;

                this.Invoke(new Action(async () =>
                {
                    LogTracer.Instance.Trace(message, TraceEventType.Error);
                    await _infoMessage.ShowErrorMessage(message);
                }));
            });
        }

        private void OnRealtimeCommunicationError(string message)
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                await _infoMessage.ShowErrorMessage(message);
            }));
        }

        private void OnRealtimeCommunicationInitPresentation(JObject result)
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                if (_forceUpdateVersion)
                    return;

                if (!_mainSettingsInited)
                {
                    // trying to complete the first setup
                    GetMainSettings(() =>
                    {
                        OnRealtimeCommunicationInitPresentation(result);
                    });
                    return;
                }

                if (result == null)
                {
                    string errMsg = @"Presentation data cannot be empty!";
                    LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                    await _infoMessage.ShowErrorMessage(errMsg);
                    return;
                }

                int presentationId = result.Get<int>("presentationId", 0);
                if (presentationId <= 0)
                {
                    string errMsg = @"Presentation id not valid!";
                    LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                    await _infoMessage.ShowErrorMessage(errMsg);
                    return;
                }

                bool needDownload = (_presentationId != presentationId);
                _sceneIndexToGo = result.Get<int>("sceneIndex", 0);
                _subSceneIndexToGo = result.Get<int>("subSceneIndex", 0);

                // stop display mode if it's present

                if (_displayModeAppProcessId != null || _displayModePowerPointPresentation != null || _displayModePowerPointApp != null)
                    await OnRealtimeCommunicationClientDisplayModeStop();                

                if (needDownload || _needToProcessNextInit)
                {
                    _needToProcessNextInit = false;
                  
                    // check if there is the live content for this monitor
                    var liveContent = result.Get<JObject>("liveContent");
                    _presentationLiveContentOnInit = null;
                    if (liveContent != null && liveContent.Get<int>("monitorIndex") == _currentMonitor)
                        _presentationLiveContentOnInit = liveContent;
                    else
                    {
                        // there is not live content: check if the live panel is open
                        if (panLiveContentContainer.Visible)
                        {
                            // close the live content panel
                            _presentationManager.UnloadLiveContent();
                        }
                    }

                    if (_isPresentationDataDownloadComplete && !needDownload)
                    {
                        _presentationManager.GotoScene(_sceneIndexToGo, _subSceneIndexToGo);
                    }
                    else
                    {
                        if (!_coverShown)
                        {
                            // waiting for the cover first...
                            await Task.Delay(300);
                            OnRealtimeCommunicationInitPresentation(result);
                            return;
                        }
                        _presentationId = presentationId;
                        _isPresentationDataDownloadComplete = false;
                        DownloadPresentationDataById(_presentationId);
                    }
                }
            }));
        }

        private void OnRealtimeCommunicationUnloadPresentation()
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                if (_forceUpdateVersion)
                    return;

                _presentationManager.Unload();
                PresentationManagerInitialize();

                _presentationLiveContentOnInit = null;
                _presentationId = -1;
                _isPresentationDataDownloadComplete = false;
                _sceneIndexToGo = -1;
                _subSceneIndexToGo = -1;
                _hasSceneContentError = false;

                await _infoMessage.HideMessage();

                //N.B.: the cover page is already visibile because it's not hidden by anyone
                imgPresentationBackground.BringToFront();
                imgPresentationBackground.Visible = true;

                lblMessage.BringToFront();
                imgPreload.BringToFront();
            }));
        }

        private void OnRealtimeCommunicationGotoScene(JObject result)
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                if (_forceUpdateVersion || _hasSceneContentError)
                    return;

                if (result == null)
                {
                    string errMsg = @"Goto scene data cannot be empty!";
                    LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                    await _infoMessage.ShowErrorMessage(errMsg);
                    return;
                }

                _sceneIndexToGo = result.Get<int>("sceneIndex");
                _subSceneIndexToGo = result.Get<int>("subSceneIndex", -1);

                if (_presentationLiveContentOnInit == null && panLiveContentContainer.Visible)
                    _presentationManager.UnloadLiveContent();

                if (_isPresentationDataDownloadComplete && _sceneIndexToGo >= 0)
                {
                    await _infoMessage.CleanErrorMessage();

                    _presentationManager.GotoScene(_sceneIndexToGo, _subSceneIndexToGo);
                }
            }));
        }

        private void OnRealtimeCommunicationDisconnected()
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                _needToProcessNextInit = true;
                await _infoMessage.ShowErrorMessage("Disconnected from server");
            }));
        }


        //###### live content

        private void OnRealtimeCommunicationInitLiveContent(JObject result)
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                if (_forceUpdateVersion)
                    return;

                await _infoMessage.CleanErrorMessage();

                LogTracer.Instance.Trace("Init the live content process...");

                if (result == null)
                {
                    string errMsg = @"Live presentation data cannot be empty!";
                    LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                    await _infoMessage.ShowErrorMessage(errMsg);
                    return;
                }

                // check if the live content is for the current monitor
                if (result.Get<int>("monitorIndex") != _currentMonitor)
                    return;

                _presentationManager.ShowLiveContent(result);
            }));
        }

        private void OnRealtimeCommunicationUnloadLiveContent()
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                if (_forceUpdateVersion)
                    return;

                await _infoMessage.HideMessage();

                LogTracer.Instance.Trace("Unload the live content");

                _presentationManager.UnloadLiveContent();
            }));
        }

        private void OnRealtimeCommunicationGotoSceneLiveContent(JObject result)
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                if (_forceUpdateVersion)
                    return;

                await _infoMessage.CleanErrorMessage();

                // check if there is a live content for this monitor
                if (panLiveContentContainer.Visible)
                {
                    var sceneIndex = result.Get<int>("sceneIndex", -1);
                    var subSceneIndex = result.Get<int>("subSceneIndex", -1);

                    LogTracer.Instance.Trace(string.Format("Live content goto scene: {0} {1}", sceneIndex, subSceneIndex));

                    _presentationManager.LiveContentGotoScene(sceneIndex, subSceneIndex);
                }
            }));
        }


        private async void OnRealtimeCommunicationClientDisplayModeStart(JObject result)
        {
            if (this.Disposing)
                return;

            try
            {
                // close the previous display mode process is it's still running
                await OnRealtimeCommunicationClientDisplayModeStop();

                string commandString = result.Get<string>("commandString", "");
                bool isWebSite = (result.Get<int>("isWebSite", 0) > 0);
                if (commandString.Trim() != "")
                {
                    if (isWebSite)
                    {
                        // open the browser with the specific url
                        ProcessStartInfo sInfo = new ProcessStartInfo(commandString.Trim());
                        sInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Maximized;
                        _displayModeAppProcessId = Process.Start(sInfo);
                        if (_displayModeAppProcessId == null)
                        {
                            // send the error to the director
                            await _rtc.PresentationErrorAsync(_presentationManager.GetCurrentSceneIndex(), _presentationManager.GetCurrentSubSceneIndex(), RealtimeCommunication.ERR_CODE_DISPLAY_MODE_ERROR, "Unable to open the browser!");
                            return;
                        }
                    }
                    else
                    {
                        if (!Enum.TryParse(result.Get<int>("mode").ToString(), out DisplayModeClientMode mode))
                        {
                            // send the error to the director
                            await _rtc.PresentationErrorAsync(_presentationManager.GetCurrentSceneIndex(), _presentationManager.GetCurrentSubSceneIndex(), RealtimeCommunication.ERR_CODE_DISPLAY_MODE_ERROR, string.Format("Display mode 'mode' not valid ({0})!", result.Get<int>("mode")));
                            return;
                        }
                        
                        if (mode == DisplayModeClientMode.FILE)
                        {
                            // download the specified resource from the server

                            // check the resource type (loaded from the media library or from the display mode library)
                            int resourceId = result.Get<int>("resourceId", 0);
                            
                            // send the info that the resource is downloading
                            await _rtc.PresentationDownloadStartAsync();

                            if (resourceId > 0)
                            {
                                // call the Laravel API to get the resource info
                                _ = APIService.CallGetAsync(string.Format("/resource/get?id={0}", resourceId), (APIResult) =>
                                {
                                    this.Invoke(new Action(() =>
                                    {
                                        DownloadFileFromUrl(commandString, APIResult);
                                    }));
                                },
                                (message) =>
                                {
                                    this.Invoke(new Action(async () =>
                                    {
                                        // send the info that the resource download ended
                                        await _rtc.PresentationDownloadEndedAsync();

                                        // send the error to the director
                                        await _rtc.PresentationErrorAsync(_presentationManager.GetCurrentSceneIndex(), _presentationManager.GetCurrentSubSceneIndex(), RealtimeCommunication.ERR_CODE_DISPLAY_MODE_ERROR, string.Format("Error getting the resource info from db: {0}", message));
                                    }));
                                });
                                return;
                            }
                            else
                            {
                                DownloadFileFromUrl(commandString);
                                return;
                            }
                        }
                        else if (mode == DisplayModeClientMode.PROGRAM)
                        {
                            // run a specific application if it's present on the machine
                            _displayModeAppProcessId = new System.Diagnostics.Process();
                            _displayModeAppProcessId.StartInfo.FileName = commandString;
                            _displayModeAppProcessId.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Maximized; // it Maximized application  
                            if (!_displayModeAppProcessId.Start())
                            {
                                // send the error to the director
                                await _rtc.PresentationErrorAsync(_presentationManager.GetCurrentSceneIndex(), _presentationManager.GetCurrentSubSceneIndex(), RealtimeCommunication.ERR_CODE_DISPLAY_MODE_ERROR, "Unable to start the process!");
                                return;
                            }
                        }
                        else if (mode == DisplayModeClientMode.SCREEN_SHARE)
                        {
                            // open the internal browser to show the cast webpage
                            // call the same method used by the Live Content when the user shows a website
                     
                            _ = this.Invoke(new Action(() =>
                            {
                                if (_forceUpdateVersion)
                                    return;

                                dynamic payload = new JObject();
                                payload.resourceType = DocumentsUtility.WEBSITE_RESOURCE_TYPE;
                                payload.resourceFile = commandString;
                                dynamic parameters = new JObject();
                                parameters.displayMode = true;
                                payload["params"] = parameters;

                                _presentationManager.ShowLiveContent(payload);
                            }));

                            return;
                        }
                    }
                }              
            }
            catch(Exception ex)
            {
                // send the info that the resource download ended
                await _rtc.PresentationDownloadEndedAsync();

                // send the error to the director
                await _rtc.PresentationErrorAsync(_presentationManager.GetCurrentSceneIndex(), _presentationManager.GetCurrentSubSceneIndex(), RealtimeCommunication.ERR_CODE_DISPLAY_MODE_ERROR, ex.Message);
                return;
            }

            SendPlayerToBackground();
        }

        private void DisplayModeOpenLocalResourceWithAssociatedProgram(string localResourcePath)
        {
            try
            {
                // check if the resource is a PawerPoint so we can start the presentation directly
                if (DocumentsUtility.IsPowerPoint(localResourcePath))
                {
                    var beforePowerPointProcesses = DocumentsUtility.GetPowerPointProcessSnapshot();
                    _displayModePowerPointApp = new Microsoft.Office.Interop.PowerPoint.Application();
                    _displayModePowerPointApp.Visible = MsoTriState.msoTrue;
                    DocumentsUtility.TrackNewPowerPointProcesses(beforePowerPointProcesses);
                    Presentations ppPresens = _displayModePowerPointApp.Presentations;

                    _displayModePowerPointPresentation = ppPresens.Open(localResourcePath, MsoTriState.msoFalse, MsoTriState.msoTrue, MsoTriState.msoTrue);
                    Slides objSlides = _displayModePowerPointPresentation.Slides;
                    SlideShowWindows objSSWs;
                    SlideShowSettings objSSS;

                    //Run the Slide show
                    objSSS = _displayModePowerPointPresentation.SlideShowSettings;
                    objSSS.Run();
                    objSSWs = _displayModePowerPointApp.SlideShowWindows;
                }
                else if (DocumentsUtility.IsImage(localResourcePath) || DocumentsUtility.IsPDF(localResourcePath))
                {
                    // TODO: at the moment we use the browser to run images and pdf files because the default app are not controllable. Verify it later with Accenture

                    string browserPath = WindowUtility.GetBrowserPath();
                    if (browserPath == string.Empty)
                        browserPath = "iexplore";
                    Process process = new Process();
                    process.StartInfo = new ProcessStartInfo(browserPath);
                    process.StartInfo.Arguments = "\"" + localResourcePath + "\"";
                    process.Start();
                    _displayModeAppProcessId = process;
                }
                else
                {
                    ProcessStartInfo sInfo = new ProcessStartInfo(localResourcePath);
                    sInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Maximized;
                    _displayModeAppProcessId = Process.Start(localResourcePath);
                    if (_displayModeAppProcessId == null)
                    {
                        // send the error to the director
                        _ = _rtc.PresentationErrorAsync(_presentationManager.GetCurrentSceneIndex(), _presentationManager.GetCurrentSubSceneIndex(), RealtimeCommunication.ERR_CODE_DISPLAY_MODE_ERROR, "Unable to open the browser!");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                // send the error to the director
                _ = _rtc.PresentationErrorAsync(_presentationManager.GetCurrentSceneIndex(), _presentationManager.GetCurrentSubSceneIndex(), RealtimeCommunication.ERR_CODE_DISPLAY_MODE_ERROR, ex.Message);
                return;
            }

            SendPlayerToBackground();
        }

        private void SendPlayerToBackground()
        {
            // put the player in background when the launched app is ready
            this.BeginInvoke(new MethodInvoker(async delegate
            {
                if (_displayModePowerPointApp == null)
                {
                    //if (_displayModeAppProcessId.MainWindowHandle != IntPtr.Zero)
                    //    WindowUtility.SetForegroundWindow(_displayModeAppProcessId.MainWindowHandle);
                    
                    bool isReady = false;
                    int paracadute = 60 * 1000 / 500;
                    while (!isReady && paracadute > 0)
                    {
                        paracadute--;
                        isReady = (_displayModeAppProcessId != null && _displayModeAppProcessId.MainWindowHandle != IntPtr.Zero);
                        await Task.Delay(500);
                    }
                }

                this.TopMost = false;
                this.SendToBack();
            }));
        }

        private async void DownloadFileFromUrl(string commandString, JObject resourceData = null)
        {
            StopCurrentDisplayModeClientDownload();
            _currentDisplayModeClientDownload = new CancellationTokenSource();

            string errorMsg = null;

            if (commandString.ToLower().IndexOf(@"http://") != -1 ||
                commandString.ToLower().IndexOf(@"https://") != -1 ||
                commandString.ToLower().IndexOf(@"file://") != -1)
            {
                LogTracer.Instance.Trace(string.Format("Found a document resource: '{0}'", commandString));

                // get only the file name from url
                var uriSegs = new Uri(commandString).Segments;
                string fileNameOnly = uriSegs.Last();
                int? presentationId = null;

                if (resourceData != null)
                {
                    var resourceDataResults = resourceData.Get<JObject>("results");
                    if (resourceDataResults != null)
                    {
                        int resourceId = resourceDataResults.Get<int>("id", 0);
                        presentationId = resourceDataResults.Get<int>("presentation_id");
                        int version = resourceDataResults.Get<int>("version", 0);

                        if (resourceId > 0)
                        {
                            // add the version code to the file name
                            string fileExt = Path.GetExtension(fileNameOnly);
                            int posExt = fileNameOnly.LastIndexOf(fileExt);
                            if (posExt != -1)
                            {
                                fileNameOnly = string.Format("{0}-{1}-{2}{3}", resourceId, fileNameOnly.Substring(0, posExt), version, fileExt);
                            }
                            else
                            {
                                fileNameOnly = string.Format("{0}-{1}-{2}", resourceId, fileNameOnly.Substring(0, posExt), version);
                            }
                        }
                    }
                }

                var displayModeTempPath = Path.Combine(_basePresentationsPath, (presentationId != null ? presentationId.ToString() : "display-mode"));
                if (!Directory.Exists(displayModeTempPath))
                {
                    // create the presentation directory
                    LogTracer.Instance.Trace(string.Format("Create the {0} directory: {1}", (presentationId != null ? "presentation" : "display-mode temp"), displayModeTempPath));

                    Directory.CreateDirectory(displayModeTempPath);
                }

                _currentDisplayModeResourceFileName = commandString;
                _currentDisplayModeResourceLocalFile = Path.Combine(displayModeTempPath, fileNameOnly);

                // if the local file already exists it will not download again (if the version number is the same)
                try
                {
                    await RemoteFileDownloader.DownloadAsync(commandString, _currentDisplayModeResourceLocalFile, false, _currentDisplayModeClientDownload.Token);
                    await _rtc.PresentationDownloadEndedAsync();
                    DisplayModeOpenLocalResourceWithAssociatedProgram(_currentDisplayModeResourceLocalFile);
                }
                catch (OperationCanceledException)
                {
                    await _rtc.PresentationDownloadEndedAsync();
                }
                catch (Exception ex)
                {
                    await _rtc.PresentationDownloadEndedAsync();
                    errorMsg = string.Format(@"Error downloading the remote file {0} - {1}", commandString, ex.Message);
                    LogTracer.Instance.Trace(errorMsg, TraceEventType.Error);
                    await _rtc.PresentationErrorAsync(_presentationManager.GetCurrentSceneIndex(), _presentationManager.GetCurrentSubSceneIndex(), RealtimeCommunication.ERR_CODE_DISPLAY_MODE_ERROR, errorMsg);
                }

                StopCurrentDisplayModeClientDownload();
            }
        }

        private void StopCurrentDisplayModeClientDownload()
        {
            if (_currentDisplayModeClientDownload != null)
            {
                _currentDisplayModeClientDownload.Cancel();
                _currentDisplayModeClientDownload.Dispose();
            }
            _currentDisplayModeClientDownload = null;
            _currentDisplayModeResourceLocalFile = null;
            _currentDisplayModeResourceFileName = null;
        }

        private async Task OnRealtimeCommunicationClientDisplayModeStop()
        {
            if (this.Disposing || this.IsDisposed)
                return;

            // put the player in foreground
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(new MethodInvoker(delegate
                {
                    ApplyTopMostState(_topMostEnabled);
                }));
            }

            try
            {
                // close the launched process 
                _displayModeAppProcessId?.Kill();

                // close the presentation without saving changes and quit PowerPoint
                _displayModePowerPointPresentation?.Close();
                _displayModePowerPointApp?.Quit();

                // kill all the office app process
                DocumentsUtility.KillAllOfficeProcesses();

                TaskCompletionSource<bool> closeShareScreenTask = new TaskCompletionSource<bool>();
                // close the sharescreen if it was opened before
                if (!this.IsHandleCreated)
                {
                    closeShareScreenTask.SetResult(true);
                }
                else
                {
                    _ = this.Invoke(new Action(async () =>
                    {
                        if (_forceUpdateVersion)
                        {
                            closeShareScreenTask.SetResult(true);
                            return;
                        }

                        await _infoMessage.HideMessage();

                        LogTracer.Instance.Trace("Unload the live content");

                        _presentationManager.UnloadLiveContent(true);
                        closeShareScreenTask.SetResult(true);
                    }));
                }

                await closeShareScreenTask.Task;
            }
            catch (Exception) { }

            _displayModeAppProcessId = null;
            _displayModePowerPointPresentation = null;
            _displayModePowerPointApp = null;
        }
        #endregion


        #region Presentation management

        private void PresentationManagerInitialize()
        {
            _presentationManager = new PresentationManager(this, panScenesContentsContainer, panLiveContentContainer, _basePresentationsPath, _purgePresentationData);
            _presentationManager.OnError += OnPresentationManagerError;
            _presentationManager.OnGotoSceneComplete += OnPresentationManagerGotoSceneComplete;
            _presentationManager.OnSceneContentError += OnPresentationManagerSceneContentError;
            _presentationManager.OnLoadScenesStart += OnPresentationManagerLoadScenesStart;
            _presentationManager.OnLoadScenesEnd += OnPresentationManagerLoadScenesEnd;
            _presentationManager.OnRealtimeCommunicationReconnect += OnPresentationManagerRealtimeCommunicationReconnect;
            _presentationManager.OnDownloadLiveContent += OnPresentationManagerDownloadLiveContent;
            _presentationManager.OnLoadContentLiveContent += OnPresentationManagerLoadContentLiveContent;
            _presentationManager.OnShowCover += OnPresentationManagerShowCover;
        }

        private void InstallStagedUpdate(string installScriptPath)
        {
            if (string.IsNullOrWhiteSpace(installScriptPath))
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => InstallStagedUpdate(installScriptPath)));
                return;
            }

            try
            {
                LogTracer.Instance.Trace("Auto-update install script launched: " + installScriptPath);
                Process.Start(new ProcessStartInfo(installScriptPath)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                QuitApp();
            }
            catch (Exception ex)
            {
                LogTracer.Instance.Trace("Unable to launch auto-update script: " + ex.Message, TraceEventType.Error);
                MessageBox.Show(
                    "Unable to launch the update installer: " + ex.Message,
                    "Update error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ShowCover(JObject result, Action completed)
        {
            LogTracer.Instance.Trace(string.Format(@"Show cover: {0}", result.ToString()));

            this.Invoke(new Action(async () =>
            {
                if (result == null)
                {
                    var errMsg = @"Main settings data cannot be empty!";
                    LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                    await _infoMessage.ShowErrorMessage(errMsg);
                    return;
                }

                _coverBackgroundColor = result.Get("background_color", DEFAULT_BACKGROUND_COLOR);
                _presentationManager.BackgroundColor = ColorTranslator.FromHtml(_coverBackgroundColor);
                
                // set the background color
                imgPresentationBackground.BackColor = _presentationManager.BackgroundColor;

                await Task.Delay(2000);  // if the download phase is too fast I wait a lot to read the previous message

                // hide the init UI elements
                await _infoMessage.HideMessage(new Action(() =>
                {
                    this.Invoke(new Action(() =>
                    {
                        // show the presentation cover image (if exists)
                        JObject coverJSON = result.Get<JObject>("cover");
                        if (coverJSON != null)
                        {
                            // get the image version
                            int ver = coverJSON.Get("version", 1);
                            string coverFile = coverJSON.Get<string>("file");
                            if (!string.IsNullOrEmpty(coverFile))
                            {
                                string coverPath = Path.Combine(_basePresentationsPath, string.Format(@"cover-{0}.png", ver));

                                FileUtility.PurgeAllUnterminatedDownloadFiles(_basePresentationsPath);

                                LogTracer.Instance.Trace(string.Format("Get the cover image: {0}", coverPath));

                                ImageUtility.GetImageFromURL(coverJSON["file"].Value<string>(), coverPath, true,
                                (coverImage) =>
                                {
                                    if (coverImage != null)
                                    {
                                        Transition tBackgroundLogoHide = new Transition(new TransitionType_Acceleration(200));
                                        tBackgroundLogoHide.TransitionCompletedEvent += (sender, e) =>
                                        {
                                            _ = this.Invoke(new Action(() =>
                                            {
                                                // dispose della cover precedente per evitare leak GDI ad ogni cambio versione
                                                var oldCover = imgPresentationBackground.Image;
                                                imgPresentationBackground.Image = coverImage;
                                                if (oldCover != null && !ReferenceEquals(oldCover, coverImage))
                                                    oldCover.Dispose();

                                                ContinueWithCoverDataSetted();

                                                completed.Invoke();
                                            }));
                                        };
                                        tBackgroundLogoHide.add(imgBackgroundLogo, "Opacity", 0f);
                                        tBackgroundLogoHide.run();
                                    }
                                    else
                                    {
                                        // cover not retrieved!
                                        LogTracer.Instance.Trace("There isn't error downloading the cover file", TraceEventType.Error);

                                        // continue anyway without the cover 
                                        ContinueWithCoverDataSetted();
                                        completed.Invoke();
                                    }
                                });
                            }
                            else
                            {
                                LogTracer.Instance.Trace("There isn't any cover configured in the CMS");

                                // continue anyway without the cover 
                                ContinueWithCoverDataSetted();

                                completed.Invoke();
                            }
                        }
                        else
                        {
                            completed.Invoke();
                        }
                    }));
                }));
            }));
        }

        private void ContinueWithCoverDataSetted()
        {
            imgBackgroundLogo.Visible = false;

            imgPresentationBackground.BringToFront();
            imgPresentationBackground.Visible = true;

            panScenesContentsContainer.Visible = false;

            // put the message label and preload on top of the cover image
            lblMessage.BringToFront();
            imgPreload.BringToFront();

            _infoMessage.MessageExitYOffset = _infoMessage.MessageEnterYOffset;

            // force the resize to adjust all the UI controls inside the form (in this case I need to move the lblMessage to the bottom of the form and the preload to the right bottom of the form)
            MainForm_SizeChanged(this, EventArgs.Empty);

            _coverShown = true;
        }

        private async void DownloadPresentationDataById(int id)
        {
            if (_forceUpdateVersion)
                return;

            // force the first resize to adjust all the UI controls inside the form
            MainForm_SizeChanged(this, EventArgs.Empty);

            await _infoMessage.ShowMessage("Download presentation data", false, false, new Action(async () =>
            {
                _infoMessage.ShowPreload();
                 
                // call the Laravel API to get the presentationData
                string serviceUrl = string.Format(API_PRESENTATION_DATA, id, _currentMonitor);
                _ = await APIService.CallGetAsync(serviceUrl, (result) =>
                {
                    DownloadPresentationData(result);
                },
                async (message) =>
                {
                    LogTracer.Instance.Trace(message, TraceEventType.Error);
                    await _infoMessage.ShowErrorMessage(message);
                });
            }));
        }
        
        private async void DownloadPresentationData(JObject presentationData)
        {
            if (_presentationManager == null)
                return;

            // now load the presentation documents for the current scene

            await _rtc.PresentationDownloadStartAsync();

            _presentationManager.Initialize(presentationData, async () =>
            {
                if (!_presentationManager.IsUnloaded)
                {
                    string customPresentationColor = _presentationManager.PresentationColor;
                    if (customPresentationColor != null)
                        _presentationManager.BackgroundColor = ColorTranslator.FromHtml(customPresentationColor);

                    _infoMessage.HidePreload();

                    // delay...
                    await Task.Delay(1000);

                    await _infoMessage.HideMessage(new Action(async () =>
                    {
                        LogTracer.Instance.Trace(string.Format("Local download completed... waiting for the 'enter' command from the controller"));
                        await _rtc.PresentationDownloadEndedAsync();

                        _isPresentationDataDownloadComplete = true;

                        if (_sceneIndexToGo >= 0)
                        {
                            _presentationManager.GotoScene(_sceneIndexToGo, _subSceneIndexToGo);
                        }
                    }));
                }
                else
                {
                    await _infoMessage.HideMessage();
                }
            },
            async (error) =>
            {
                await _infoMessage.ShowErrorMessage("Error during the presentation init!");          
            });               
        }
       
        private void OnPresentationManagerError(string message)
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                await _infoMessage.ShowErrorMessage(message);
            }));
        }

        private void OnPresentationManagerGotoSceneComplete(int sceneIndex, int subSceneIndex)
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(() =>
            {
                imgPresentationBackground.Focus();
                
                _presentationManager.ShowCurrentLoadedScene(() =>
                {
                    if (this.Disposing)
                        return;

                    _ = this.Invoke(new Action(async () =>
                    {

                        if (_hideMouseAndTopMostWin)
                        {
                            // hide the mouse cursor
                            Cursor.Hide();
                            WindowUtility.SetCursorPos(ClientRectangle.Width, ClientRectangle.Height);
                            SetFocusOnMainForm();
                        }

                        await _rtc.PresentationGotoSlideEndAsync(sceneIndex, subSceneIndex);

                        if (_presentationLiveContentOnInit != null)
                        {
                            // show the live content
                            _presentationManager.ShowLiveContent(_presentationLiveContentOnInit);
                            _presentationLiveContentOnInit = null;
                        }
                    }));
                },
                (error) =>
                {
                    this.Invoke(new Action(async () =>
                    {
                        await _infoMessage.ShowErrorMessage(error);
                    }));
                });
            }));
        }

        private async void OnPresentationManagerSceneContentError(int sceneIndex, int contentIndex, int errorCode)
        {
            //_hasSceneContentError = true;

            // I need to communicate this condition to the NodeJS server
            await _rtc.PresentationErrorAsync(sceneIndex, contentIndex, errorCode);
        }

        private void OnPresentationManagerLoadScenesStart()
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                await Task.Delay(300);
                await _infoMessage.ShowMessage("Loading scenes contents...", true);
            }));
        }

        private void OnPresentationManagerLoadScenesEnd()
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                await Task.Delay(500);
                await _infoMessage.HideMessage();
            }));
        }

        private void OnPresentationManagerDownloadLiveContent(bool start)
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                if (start)
                {
                    await Task.Delay(300);
                    await _infoMessage.ShowMessage("Download live contents...", true);
                }
                else
                {
                    await _infoMessage.HideMessage();
                }
            }));
        }

        private void OnPresentationManagerLoadContentLiveContent(bool start)
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(async () =>
            {
                if (start)
                {
                    await Task.Delay(300);
                    await _infoMessage.ShowMessage("Load live contents...", true);
                }
                else
                {
                    await _infoMessage.HideMessage();
                }
            }));
        }

        private void OnPresentationManagerRealtimeCommunicationReconnect()
        {
            _needToProcessNextInit = true;
            _rtc.Reconnect();
        }

        private void OnPresentationManagerShowCover(bool show)
        {
            if (this.Disposing)
                return;

            _ = this.Invoke(new Action(() =>
            {
                if (show)
                {
                    if (!imgPresentationBackground.Visible)
                        ContinueWithCoverDataSetted();       
                }
                else
                {
                    if (imgPresentationBackground.Visible)
                    {
                        imgPresentationBackground.Visible = false;

                        panScenesContentsContainer.BringToFront();
                        panScenesContentsContainer.Visible = true;

                        // put the message label and preload on top of the cover image
                        lblMessage.BringToFront();
                        imgPreload.BringToFront();
                    }
                }
            }));
        }
        #endregion

        #region Get the Windows scale factor
        private float GetWindowsScalingFactor()
        {
            // si rilasciano esplicitamente HDC e Graphics per non perdere handle GDI
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                IntPtr desktop = g.GetHdc();
                try
                {
                    int LogicalScreenHeight = GetDeviceCaps(desktop, (int)DeviceCap.VERTRES);
                    int PhysicalScreenHeight = GetDeviceCaps(desktop, (int)DeviceCap.DESKTOPVERTRES);

                    return (float)PhysicalScreenHeight / (float)LogicalScreenHeight; // 1.25 = 125%
                }
                finally
                {
                    g.ReleaseHdc(desktop);
                }
            }
        }
        #endregion

        private void ImgPresentationBackground_MouseClick(object sender, MouseEventArgs e)
        {
            /*
            #if DEBUG
                _ = this.Invoke(new Action(() =>
                {
                    imgPresentationBackground.Visible = false;
                    //@@@ _presentationManager.Restart();
                }));
            #endif
            */
        }

        private void MainForm_Activated(object sender, EventArgs e)
        {
            if (_topMostEnabled)
            {
                // show the app in foreground (above all)
                ApplyTopMostState(true);
            }
        }
    }
}
