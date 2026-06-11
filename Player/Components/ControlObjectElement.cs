using CefSharp;
using CefSharp.WinForms;
using ContentDistributionPlayer.Components;
using ContentDistributionPlayer.Extensions;
using ContentDistributionPlayer.Utilities;
using LibVLCSharp.Shared;
using Microsoft.Office.Interop.PowerPoint;
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ContentDistributionPlayer
{
    class ControlObjectElement
    {
        public DocumentsUtility.DocumentTypes Type { get; private set; } = DocumentsUtility.DocumentTypes.None;
        public Control Container { get; private set; }
        public Panel SceneContainer { get; private set; }

        

        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private const int DEFAULT_VIDEO_VOLUME_LEVEL = 100;
        private bool _hasVideoTheAudio = true;
        private bool _isVideoInLoop = false;
        private float _videoFromPerc = 0;
        private float _videoToPerc = 1;
        private int _videoFromSec = 0;
        private int _videoToSec = -1;
        private bool _isVideoPlaying = false;
        private bool _isVideoPaused = false;
        private LibVLCSharp.WinForms.VideoView _videoObject;

        private ChromiumWebBrowser _browserObject;
        private bool _hasWebsiteTheAudio = true;
        private static bool _isCefInitialized = false;
        private static bool _isCefInitializing = false;
        private bool _needToReloadWebsiteUrl = false;

        private PictureBox _imageObject;

        private PowerPointObject _powerPointObject;

        public string FileName { get; private set; }

        public Action OnSceneContentError;

        public void CreateDocumentObjectFromFile(string fileName, Panel sceneContainer, Control container, Action completed, Action error)
        {
            FileName = fileName;
            Type = DocumentsUtility.GetDocumentTypeByFileName(FileName);

            SceneContainer = sceneContainer;
            Container = container;
            Container.Name = FileName;
        
            if (Type != DocumentsUtility.DocumentTypes.None)
            {
                if (DocumentsUtility.IsOfficeDocument(Type))
                {
                    if (!string.IsNullOrEmpty(FileName))
                    {
                        if (DocumentsUtility.IsPowerPoint(Type))
                        {
                            _powerPointObject = new PowerPointObject(SceneContainer, Container, OnSceneContentError);
                            _powerPointObject.OpenDocument(FileName, completed, error);

                            // waiting the load complete event before invoke the completed event
                            return;
                        }
                    }
                }
                else if (Type == DocumentsUtility.DocumentTypes.Video)
                {
                    if (_libVLC == null)
                        _libVLC = new LibVLC();

                    if (_mediaPlayer == null)
                        _mediaPlayer = new MediaPlayer(_libVLC);
    
                    _videoObject = new LibVLCSharp.WinForms.VideoView
                    {
                        MediaPlayer = _mediaPlayer
                    };
                }
                else if (Type == DocumentsUtility.DocumentTypes.Website)
                {
                    if (!_isCefInitialized && !_isCefInitializing)
                    {
                        _isCefInitializing = true;
                        CefSettings settings = new CefSettings();
                        //settings.CefCommandLineArgs["enable-media-stream"] = "1";

                        /*
                        settings.LogSeverity = LogSeverity.Verbose;
                        string _logPath = Path.Combine((string.IsNullOrEmpty(Properties.Settings.Default.ContentsFolder) ? AppDomain.CurrentDomain.BaseDirectory : Properties.Settings.Default.ContentsFolder), @"cef");
                        if (!Directory.Exists(_logPath))
                        {
                            // create the log directory
                            Directory.CreateDirectory(_logPath);
                        }
                        settings.LogFile = Path.Combine(_logPath, "cef_log.txt");
                        */

                        // disable user gesture required to autoplay video with audio
                        settings.CefCommandLineArgs["autoplay-policy"] = "no-user-gesture-required";

                        // Initialize cef with the provided settings
                        Cef.Initialize(settings);
                    }                    

                    _browserObject = new ChromiumWebBrowser("about:blank")
                    {
                        Dock = DockStyle.None,
                        Margin = new Padding(0)
                    };

                    void handler(object sender, EventArgs e)
                    {
                        SceneContainer.BeginInvoke(new MethodInvoker(delegate
                        {
                            _browserObject.IsBrowserInitializedChanged -= handler;

                            if (_browserObject.IsBrowserInitialized)
                            {
                                _isCefInitialized = true;
                                _isCefInitializing = false;

                                if (_needToReloadWebsiteUrl)
                                {
                                    NavigateWeb();
                                    _needToReloadWebsiteUrl = false;
                                }
                            }
                        }));
                    }

                    _browserObject.IsBrowserInitializedChanged += handler;
                }
                else if (Type == DocumentsUtility.DocumentTypes.Image)
                {
                    _imageObject = new PictureBox
                    {
                        BackColor = Color.Transparent,
                        BorderStyle = BorderStyle.None,
                        Dock = DockStyle.None,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Image = ImageUtility.LoadBitmapUnlocked(FileName)
                    };
                }
            }
            else
            {
                Close(false);
            }

            AddControlToContainer();

            completed.Invoke();
        }

        public void SendPowerPointCommand(PowerPointObject.PowerPointCommand command)
        {
            if (_powerPointObject != null)
                _powerPointObject.SendPowerPointCommand(command);
            else if (command != null)
                command.Completed?.Invoke();
        }

        public void SetPowerPointSubSlides(int slide, int subSlides)
        {
            _powerPointObject?.SetPowerPointSubSlides(slide, subSlides);
        }

        public void PlayVideo()
        {
            if (_mediaPlayer == null || _libVLC == null || string.IsNullOrEmpty(FileName) || (_isVideoPlaying && !_isVideoPaused))
                return;

        
            _mediaPlayer.PositionChanged -= OnVideo_PositionChanged;
            _mediaPlayer.EndReached -= OnVideo_EndReached;
            _mediaPlayer.Playing -= OnVideo_Playing;
            _mediaPlayer.Playing += OnVideo_Playing;

            var media = new Media(_libVLC, FileName, FromType.FromPath);
           /* if (_isVideoInLoop)
                media.AddOption("input-repeat=-1");*/
            _mediaPlayer.Play(media);
        }

        private void OnVideo_Playing(object sender, EventArgs e)
        {
            _mediaPlayer.Playing -= OnVideo_Playing;

            _isVideoPlaying = true;
            _isVideoPaused = false;

            if (_videoFromSec > 0)
            {
                _videoFromPerc = ((float)(_videoFromSec * 1000) / (float)_mediaPlayer.Length);
                if (_videoFromPerc > 1)
                    _videoFromPerc = 1;
            }
            else
                _videoFromPerc = 0;

            if (_videoToSec > 0)
            {
                _videoToPerc = ((float)(_videoToSec * 1000) / (float)_mediaPlayer.Length);
                if (_videoToPerc > 1)
                    _videoToPerc = 1;
            }
            else
                _videoToPerc = 1;

            _mediaPlayer.Position = _videoFromPerc;
            if (_videoToSec > 0 /*|| _isVideoInLoop*/)
                _mediaPlayer.PositionChanged += OnVideo_PositionChanged;
            if (_isVideoInLoop)
                _mediaPlayer.EndReached += OnVideo_EndReached;

            _mediaPlayer.Volume = (_hasVideoTheAudio ? DEFAULT_VIDEO_VOLUME_LEVEL : 0);
        }

        private void OnVideo_PositionChanged(object sender, MediaPlayerPositionChangedEventArgs e)
        {
            // Fix for some video that not give Position between 0 and 1, skip first 10 seconds
            if (_mediaPlayer.Position > 1f)
                _mediaPlayer.Position = Math.Min(_videoFromPerc + (10000f / _mediaPlayer.Length), 0.5f);

            if (_videoToPerc < 1 && _mediaPlayer.Position >= _videoToPerc)
            {
                if (_isVideoInLoop)
                    _mediaPlayer.Position = _videoFromPerc;
                else
                {
                    // stop the video
                    _mediaPlayer.Pause();
                }
            }
        }
        
        private void OnVideo_EndReached(object sender, EventArgs e)
        {
            // IMPORTANTE: Questo evento viene chiamato da un thread diverso
            // Usa Invoke sul Form per tornare al thread UI
            SceneContainer.BeginInvoke(new MethodInvoker(delegate
            {
                if (_isVideoInLoop)
                {
                    _isVideoPaused = true;
                    _mediaPlayer.Pause();
                    // _mediaPlayer.Stop();
                    PlayVideo();
                }
            }));

            /*
            if (_isVideoInLoop)
            {

                _mediaPlayer.Stop();
                _mediaPlayer.Dispose();
                PlayVideo();

                // Riavvia il video e posizionati a una percentuale specifica
                //_mediaPlayer.Pause();
                //_mediaPlayer.Play();

                //_mediaPlayer.Position = _videoFromPerc;
                //_mediaPlayer.Pause();
                //_mediaPlayer.Pause();
            }*/
        }


        public void NavigateWeb()
        {
            if (_browserObject == null || string.IsNullOrEmpty(FileName))
                return;

            if (!_isCefInitialized || !_browserObject.IsBrowserInitialized)
            {
                _needToReloadWebsiteUrl = true;
                return;
            }

            if (!_hasWebsiteTheAudio)
            {
                //TODO: controllare
                /*
                _browserObject.Dispose();
                Cef.Shutdown();
                var settings = new CefSettings();
                settings.CefCommandLineArgs.Add("mute-audio", "true");
                Cef.Initialize(settings);
                _browserObject = new ChromiumWebBrowser(FileName)
                {
                    Dock = DockStyle.None,
                    Margin = new Padding(0)
                };
                AddControlToContainer();*/
            }
            //else
            {
                _browserObject.GetBrowser().MainFrame.LoadUrl(FileName);
            }
        }

        public void ChangeImage(string fileName)
        {
            if (_imageObject != null)
            {
                // si dispone la vecchia immagine prima di sostituirla: altrimenti si accumula
                // memoria GDI ad ogni cambio pagina/slide (PDF/Word/Excel renderizzati come PNG)
                var old = _imageObject.Image;
                _imageObject.Image = ImageUtility.LoadBitmapUnlocked(fileName);
                old?.Dispose();
            }
        }

        private void Close(bool quitDocumentApp = true)
        {
            if (DocumentsUtility.IsOfficeDocument(Type))
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();

                if (_powerPointObject != null)
                {
                    _powerPointObject.Close(quitDocumentApp);
                    _powerPointObject = null;
                }
            }

            if (_videoObject != null)
            {
                _videoObject.Dispose();
                _mediaPlayer.Playing -= OnVideo_Playing;
                _mediaPlayer.PositionChanged -= OnVideo_PositionChanged;
                _mediaPlayer.EndReached -= OnVideo_EndReached;
                _mediaPlayer.Stop();
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
                _isVideoPlaying = false;
                _libVLC.Dispose();
                _libVLC = null;
                _videoObject = null;
            }

            if (_browserObject != null)
            {
                _browserObject.Dispose();
                _browserObject = null;
                _needToReloadWebsiteUrl = false;
            }

            if (_imageObject != null)
            {
                _imageObject.Image.Dispose();
                _imageObject.Dispose();
                _imageObject = null;
            }
        }

        public void RemoveDocumentControl(bool quitDocumentApp = false)
        {
            if (_videoObject != null && _videoObject.Parent != null)
            {
                _videoObject.Parent.Controls.Remove(_videoObject);   
            }

            if (_browserObject != null && _browserObject.Parent != null)
            {
                _browserObject.Parent.Controls.Remove(_browserObject);
            }

            if (_imageObject != null && _imageObject.Parent != null)
            {
                _imageObject.Parent.Controls.Remove(_imageObject);
            }

            Close(quitDocumentApp);

            FileName = null;
            Type = DocumentsUtility.DocumentTypes.None;
        }

        private void AddControlToContainer()
        {
            if (Container == null)
                return;

            if (DocumentsUtility.IsVideo(Type) && _videoObject != null)
                Container.Controls.Add(_videoObject);
            else if (DocumentsUtility.IsWebsite(Type) && _browserObject != null)
                Container.Controls.Add(_browserObject);
            else if (DocumentsUtility.IsImage(Type) && _imageObject != null)
                Container.Controls.Add(_imageObject);
            else if (DocumentsUtility.IsPowerPoint(Type) && _powerPointObject != null)
                _powerPointObject.Container = Container;
        }

        public void ShowObject(Rectangle docWindowSize, bool audio, JObject specificParams, Action completed)
        {
            if (Container == null || docWindowSize == null)
            {
                completed.Invoke();
                return;
            }

            SetContainerSize(docWindowSize);
            
            if (DocumentsUtility.IsPowerPoint(Type) && _powerPointObject != null)
            {
                // N.B.: the audio is not managed for the powerpoint files! To mute some audio inside a ppt slide the user need to do it inside PowerPoint itself before saving and publish the file on the CMS
            }
            else if (DocumentsUtility.IsVideo(Type) && _videoObject != null)
            {
                SetObjectSize(_videoObject, docWindowSize);

                _hasVideoTheAudio = audio;
                _videoFromPerc = 0;
                _videoToPerc = 1;
                _videoFromSec = 0;
                _videoToSec = -1;
                if (specificParams != null)
                {
                    _isVideoInLoop = specificParams.Get<bool>("loop", false);
                    _videoFromSec = specificParams.Get<int>("from", 0);
                    _videoToSec = specificParams.Get<int>("to", -1);
                }
            }
            else if (DocumentsUtility.IsWebsite(Type) && _browserObject != null)
            {
                SetObjectSize(_browserObject, docWindowSize);

                //TODO: capire come gestire l'audio delle webview               
                _hasWebsiteTheAudio = audio;                
            }
            else if (DocumentsUtility.IsImage(Type) && _imageObject != null)
            {
                SetObjectSize(_imageObject, docWindowSize);
            }

            completed.Invoke();
        }

        public void StopDocument(Action completed = null)
        {
            if (_videoObject != null)
            {
                if (_isVideoPlaying)
                {
                    _isVideoPaused = true;
                    _mediaPlayer.Pause();
                }
            }

            if (_powerPointObject != null)
            {
                SendPowerPointCommand(new PowerPointObject.PowerPointCommand()
                {
                    Type = PowerPointObject.PowerPointCommandType.Pause,
                    Completed = () =>
                    {
                        completed?.Invoke();
                    },
                    Error = () =>
                    {
                        OnSceneContentError?.Invoke();
                    }
                });
                return;
            }

            completed?.Invoke();
        }

        public void StartDocument(Action completed = null)
        {
            if (_videoObject != null)
            {
                if (_isVideoPaused)
                {
                    _isVideoPaused = false;
                    _mediaPlayer.Pause();
                }
                else
                {
                    PlayVideo();
                }
            }

            if (_powerPointObject != null)
            {
                SendPowerPointCommand(new PowerPointObject.PowerPointCommand()
                {
                    Type = PowerPointObject.PowerPointCommandType.Resume,
                    Completed = () =>
                    {
                        completed?.Invoke();
                    },
                    Error = () =>
                    {
                        OnSceneContentError?.Invoke();
                    }
                });
                return;
            }

            completed?.Invoke();
        }

        private void SetContainerSize(Rectangle docWindowSize)
        {
            if (Container == null)
                return;

            Container.Width = docWindowSize.Width;
            Container.Height = docWindowSize.Height;
            Container.Left = docWindowSize.Left;
            Container.Top = docWindowSize.Top;
        }

        private void SetObjectSize(Control control, Rectangle docWindowSize)
        {
            if (control == null)
                return;

            control.Width = docWindowSize.Width;
            control.Height = docWindowSize.Height;
            control.Left = 0;
            control.Top = 0;
        }

        public void SetBounds(Rectangle bounds)
        {
            SetContainerSize(bounds);

            if (DocumentsUtility.IsVideo(Type) && _videoObject != null)
                SetObjectSize(_videoObject, bounds);
            else if (DocumentsUtility.IsWebsite(Type) && _browserObject != null)
                SetObjectSize(_browserObject, bounds);
            else if (DocumentsUtility.IsImage(Type) && _imageObject != null)
                SetObjectSize(_imageObject, bounds);
            else if (DocumentsUtility.IsPowerPoint(Type) && _powerPointObject != null)
                _powerPointObject.SetBounds(bounds);
        }
    }
}