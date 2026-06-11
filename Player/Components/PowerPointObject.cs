using ContentDistributionPlayer.Utilities;
using Microsoft.Office.Interop.PowerPoint;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using PowerPointApplication = Microsoft.Office.Interop.PowerPoint.Application;


namespace ContentDistributionPlayer.Components
{
    class PowerPointObject
    {        
        private PowerPointApplication _powerApp;

        private Presentation _powerPointPresentation;

        private Action _openCompletedCallback;
        private Action _openErroCallback;
        
        private PowerPointCommand _lastCommand;

        public Control Container { get; set; }
        private Panel _sceneContainer;
        private Action _onSceneContentError;

        private int _currentSlideIndex;
        private int _currentPowerPointSubSceneIndex = 0;
        private bool _slideShowStarted = false;


        private int _sceneIndexToGo = -1;
        private int _subSceneIndexToGo = -1;
        private int _prevSceneIndexToGo = -1;
        private int _prevSubSceneIndexToGo = -1;

        private bool _slideChangeCompleted = true;

        class SubSlide
        {
            public int Slide { get; set; }
            public int SubSlides { get; set; }
        }

        private List<SubSlide> _subSlides;


        class RealSlideIndexesAssociation
        {
            public int RealIndex { get; set; }
            public int Index { get; set; }
        }

        private List<RealSlideIndexesAssociation> _realSlideIndexesAssociation;

       

        public enum PowerPointCommandType
        {
            GotoSlide,
            Pause,
            Resume
        }

        public static string GetCommandName(PowerPointCommandType type)
        {
            switch(type)
            {
                case PowerPointCommandType.GotoSlide:
                    return "Goto slide";
                case PowerPointCommandType.Pause:
                    return "Pause SlideShow";
                case PowerPointCommandType.Resume:
                    return "Resume SlideShow";
            }
            return string.Empty;
        }


        public class PowerPointCommand
        {
            public PowerPointCommandType Type { get; set; }
            public int Slide { get; set; }
            public int SubSlide { get; set; }
            public bool CameFromDisconnect { get; set; }
            public int Click { get; set; }

            public Action Completed { get; set; }
            public Action Error { get; set; }
        }            


        
        public void InitPowerPointApp(Action complete, Action error)
        {
            try
            {
                _powerApp = new PowerPointApplication
                {
                    DisplayAlerts = PpAlertLevel.ppAlertsNone,
                    Visible = Microsoft.Office.Core.MsoTriState.msoTrue
                };

                complete?.Invoke();
            }
            catch (Exception ex)
            {
                var errMsg = string.Format("Unable to start PowerPoint: {0}", ex.Message);
                LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                error?.Invoke();
            }
        }

        public PowerPointObject(Panel sceneContainer, Control container, Action onSceneContentError)
        {
            _sceneContainer = sceneContainer;
            Container = container;
            _onSceneContentError = onSceneContentError;
        }

        public void OpenDocument(string fileName, Action completed, Action error)
        {
            InitPowerPointApp(() =>
            {
                if (_powerApp == null)
                {
                    LogTracer.Instance.Trace("PowerPoint application is not started!", TraceEventType.Error);
                    error?.Invoke();
                    return;
                }

                try
                {
                    _slideChangeCompleted = true;
                   
                    _openCompletedCallback = completed;
                    _openErroCallback = error;
                    _powerApp.PresentationOpen += PowerPoint_PresentationOpen;
                    _powerApp.Presentations.Open(fileName, Microsoft.Office.Core.MsoTriState.msoTrue, Microsoft.Office.Core.MsoTriState.msoFalse, Microsoft.Office.Core.MsoTriState.msoTrue);
                }
                catch (Exception e)
                {
                    LogTracer.Instance.Trace(string.Format(@"Error opening the PowerPoint presentation: {0} - {1}", fileName, e.Message), TraceEventType.Error);
                    error?.Invoke();
                }
            },
            () =>
            {
                error?.Invoke();
            });
        }
       
        private bool SetWindowParent(SlideShowWindow ssw, Control container, Action error)
        {
            if (container == null || ssw == null)
                return true;

            try
            {
                //N.B.: used to allow the video playback on first start avoiding the freeze effect
                ssw.View.State = PpSlideShowState.ppSlideShowBlackScreen;

                IntPtr hwnd = new IntPtr(ssw.HWND);
                WindowUtility.SetParent(hwnd, container.Handle);
                int style = WindowUtility.GetWindowLong(hwnd, -16);
                WindowUtility.SetWindowLong(hwnd, -16, (style & ~(0x00800000 | 0x00400000 | 0x00040000)));
                WindowUtility.MoveWindow(hwnd, -1, -1, container.Width + 2, container.Height + 2, true);

                //N.B.: used to allow the video playback on first start avoiding the freeze effect
                ssw.View.State = PpSlideShowState.ppSlideShowRunning;

                _slideShowStarted = true;
            }
            catch (Exception e)
            {
                LogTracer.Instance.Trace(string.Format(@"Error on SetWindowParent: {0}", e.Message), TraceEventType.Error);
                error?.Invoke();
                return false;
            }

            return true;
        }

        public void Close(bool quitDocumentApp = true)
        {
            _slideShowStarted = false;
            _sceneIndexToGo = -1;
            _subSceneIndexToGo = -1;
            _prevSceneIndexToGo = -1;
            _prevSubSceneIndexToGo = -1;
            _slideChangeCompleted = true;


            if (_powerPointPresentation != null)
            {
                try
                {
                    _powerPointPresentation.Close();
                }
                catch (Exception) { }

                Marshal.ReleaseComObject(_powerPointPresentation);
                _powerPointPresentation = null;
            }

            if (_powerApp != null)
            {
                _powerApp.PresentationOpen -= PowerPoint_PresentationOpen;
                _powerApp.SlideShowBegin -= PowerPoint_SlideShowBegin;
                _powerApp.SlideShowNextSlide -= PowerPoint_SlideShowNextSlide;
                _powerApp.SlideShowEnd -= PowerPoint_SlideShowEnd;

                if (quitDocumentApp)
                {
                    try
                    {
                        _powerApp.Quit();
                    }
                    catch (Exception) { }
                }

                Marshal.ReleaseComObject(_powerApp);
                _powerApp = null;
            }

            _openErroCallback = null;
            _openCompletedCallback = null;
            _lastCommand = null;
        }

        public void SetPowerPointSubSlides(int slide, int subSlides)
        {
            if (subSlides > 0)
            {
                if (_subSlides == null)
                    _subSlides = new List<SubSlide>();

                var slideRec = _subSlides.Find(x => x.Slide == slide);
                if (slideRec == null)
                {
                    _subSlides.Add(new SubSlide
                    {
                        Slide = slide,
                        SubSlides = subSlides
                    });
                }
                else
                    slideRec.SubSlides = subSlides;
            }
        }

        public void SendPowerPointCommand(PowerPointCommand command)
        {
            if (_powerPointPresentation == null)
            {
                LogTracer.Instance.Trace(string.Format(@"Error calling SendPowerPointCommand on null PPT presentation!"), TraceEventType.Error);
                if (command != null)
                    command.Error?.Invoke();
                _onSceneContentError?.Invoke();
                return;
            }
            try
            {
                var task = Task.Run(() =>
                {
                    try
                    {
                        if (_powerPointPresentation.SlideShowWindow == null || _powerPointPresentation.SlideShowWindow.View == null)
                        {
                            LogTracer.Instance.Trace(string.Format(@"Error calling SendPowerPointCommand on not started PPT presentation {0}!", _powerPointPresentation.Path), TraceEventType.Error);
                            if (command != null)
                                command.Error?.Invoke();
                            _onSceneContentError?.Invoke();
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        if (command != null && command.Type != PowerPointCommandType.Pause && command.Type != PowerPointCommandType.Resume)
                        {
                            try
                            {
                                LogTracer.Instance.Trace(string.Format(@"Error calling SendPowerPointCommand on not started PPT presentation {0}!", _powerPointPresentation.Path), TraceEventType.Error);
                            }
                            catch (Exception)
                            {
                                LogTracer.Instance.Trace(string.Format(@"Error calling SendPowerPointCommand on not started PPT presentation!"), TraceEventType.Error);
                            }

                            command.Error?.Invoke();
                            _onSceneContentError?.Invoke();
                            return;
                        }
                    }
                });
                if (!task.Wait(TimeSpan.FromSeconds(5)))
                { 
                    LogTracer.Instance.Trace(string.Format(@"Error calling SendPowerPointCommand: PPT locked!"), TraceEventType.Error);
                    if (command != null)
                        command.Error?.Invoke();
                    _onSceneContentError?.Invoke();
                    return;
                }
            }
            catch(Exception ex)
            {
                LogTracer.Instance.Trace(string.Format(@"Error calling SendPowerPointCommand on not started PPT presentation {0}: {1}!", _powerPointPresentation.Path, ex.Message), TraceEventType.Error);
                if (command != null)
                    command.Error?.Invoke();
                _onSceneContentError?.Invoke();
                return;
            }
            if (command == null)
            {
                LogTracer.Instance.Trace(string.Format(@"Error calling SendPowerPointCommand with empty command on PPT: {0}", _powerPointPresentation.Path), TraceEventType.Error);
                _onSceneContentError?.Invoke();
                return;
            }

            _lastCommand = command;

            if (command.Type == PowerPointCommandType.GotoSlide)
            {
                if (command.Slide < 0 || command.Slide >= _realSlideIndexesAssociation.Count)
                {
                    LogTracer.Instance.Trace(string.Format("SendPowerPointCommand sceneIndex out of bound {0} -> [0-{1}]", command.Slide, _realSlideIndexesAssociation.Count - 1));
                    command.Error?.Invoke();
                    _onSceneContentError?.Invoke();
                    return;
                }

                // goto forward... check if the scene index to go is near to the current index
                _prevSceneIndexToGo = _sceneIndexToGo;
                _prevSubSceneIndexToGo = _subSceneIndexToGo;

                _sceneIndexToGo = command.Slide;
                _subSceneIndexToGo = command.SubSlide;

                if (_prevSceneIndexToGo == _sceneIndexToGo && _prevSubSceneIndexToGo == _subSceneIndexToGo)
                {
                    // nothing to do!
                    return;
                }

                // check if the current slide has sub scenes or not to allow sub scene "movement"
                int subSlides = 0;
                if (_subSlides != null)
                {
                    var sceneRec = _subSlides.Find(x => x.Slide == _sceneIndexToGo + 1);
                    if (sceneRec != null)
                        subSlides = sceneRec.SubSlides;
                }

                if ((_slideChangeCompleted && _sceneIndexToGo == _currentSlideIndex + 1) ||
                    (_sceneIndexToGo == _prevSceneIndexToGo && _subSceneIndexToGo == _prevSubSceneIndexToGo + 1))
                {
                    // check if the current ppt slide has subclicks to move on otherwise it will stop to the current slide
                    if ((_slideChangeCompleted && _sceneIndexToGo == _currentSlideIndex + 1) ||
                        (_sceneIndexToGo == _prevSceneIndexToGo && _subSceneIndexToGo == _prevSubSceneIndexToGo + 1 && _subSceneIndexToGo <= subSlides))
                    {
                        if (_slideChangeCompleted && _sceneIndexToGo == _currentSlideIndex + 1)
                        {
                            // check if the next slide index is out of bound
                            if (_sceneIndexToGo >= _powerPointPresentation.Slides.Count)
                            {
                                return;
                            }
                        }

                        // call the next method to show enter/exit slides animations
                        Debug.WriteLine(string.Format("NEXT SLIDE {0}", _sceneIndexToGo));

                        if (_slideChangeCompleted)
                            _slideChangeCompleted = false;

                        var task = Task.Run(() =>
                        {
                            try
                            {
                                if (_powerPointPresentation != null)
                                {
                                    _powerPointPresentation.SlideShowWindow.View.Next();
                                }
                            }
                            catch (Exception ex)
                            {
                                LogTracer.Instance.Trace(string.Format("Error during PPT Next call: {0}", ex.Message), TraceEventType.Error);
                            }
                        });

                        if (!task.Wait(TimeSpan.FromSeconds(5)))
                        {
                            LogTracer.Instance.Trace(string.Format("Timeout executing PPT command {0}", GetCommandName(command.Type)), TraceEventType.Error);
                            command.Error?.Invoke();
                            _onSceneContentError?.Invoke();
                        }
                    }
                    else
                    {
                        Debug.WriteLine("STOP to the current scene!");
                    }
                }
                else
                {
                    // get the real scene index
                    var indexesAssociation = _realSlideIndexesAssociation.Find(x => x.Index == _sceneIndexToGo + 1);
                    if (indexesAssociation == null)
                    {
                        LogTracer.Instance.Trace(string.Format("Real index can't be found for index {0} on PPT {1}!", _sceneIndexToGo, _powerPointPresentation.Path), TraceEventType.Error);
                        command.Error?.Invoke();
                        _onSceneContentError?.Invoke();
                        return;
                    }

                    Debug.WriteLine(string.Format("2) GOTO SLIDE {0} [orig idx: {1}]", indexesAssociation.RealIndex, _sceneIndexToGo));

                    if (_slideChangeCompleted)
                        _slideChangeCompleted = false;

                    var task = Task.Run(() =>
                    {
                        try
                        {
                            if (_powerPointPresentation != null)
                            {
                                _powerPointPresentation.SlideShowWindow.View.GotoSlide(indexesAssociation.RealIndex);
                            }
                        }
                        catch(Exception ex)
                        {
                            LogTracer.Instance.Trace(string.Format("Error during PPT GotoSlide call: {0}", ex.Message), TraceEventType.Error);
                        }                      
                    });

                    if (!task.Wait(TimeSpan.FromSeconds(5)))
                    {
                        LogTracer.Instance.Trace(string.Format("Timeout executing PPT command {0}", GetCommandName(command.Type)), TraceEventType.Error);
                        command.Error?.Invoke();
                        _onSceneContentError?.Invoke();
                    }
                }
            }
            else if (command.Type == PowerPointCommandType.Pause)
            {
                var task = Task.Run(() =>
                {
                    if (_powerPointPresentation != null)
                    {
                        try
                        {
                            if (_powerPointPresentation.SlideShowWindow.View.State == PpSlideShowState.ppSlideShowRunning)
                                _powerPointPresentation.SlideShowWindow.View.State = PpSlideShowState.ppSlideShowPaused;
                            command.Completed?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            LogTracer.Instance.Trace(string.Format("Error accessing PPT OLE - command: {0} - {1}", GetCommandName(command.Type), ex.Message), TraceEventType.Error);
                        }
                    }
                });

                if (!task.Wait(TimeSpan.FromSeconds(5)))
                {
                    LogTracer.Instance.Trace(string.Format("Timeout executing PPT command {0}", GetCommandName(command.Type)), TraceEventType.Error);
                    command.Error?.Invoke();
                    _onSceneContentError?.Invoke();
                }
            }
            else if (command.Type == PowerPointCommandType.Resume)
            {
                var task = Task.Run(() =>
                {
                    if (_powerPointPresentation != null)
                    {
                        try
                        {
                            if (_powerPointPresentation.SlideShowWindow.View.State == PpSlideShowState.ppSlideShowPaused)
                                _powerPointPresentation.SlideShowWindow.View.State = PpSlideShowState.ppSlideShowRunning;
                            command.Completed?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            LogTracer.Instance.Trace(string.Format("Error accessing PPT OLE - command: {0} - {1}", GetCommandName(command.Type), ex.Message), TraceEventType.Error);
                        }
                    }
                });

                if (!task.Wait(TimeSpan.FromSeconds(5)))
                {
                    LogTracer.Instance.Trace(string.Format("Timeout executing PPT command {0}", GetCommandName(command.Type)), TraceEventType.Error);
                    command.Error?.Invoke();
                    _onSceneContentError?.Invoke();
                }
            }
        }
        
        private void OnAdjustSubSlideAfterGotoSlide(int subSlide)
        {
            if (_powerPointPresentation.SlideShowWindow != null)
            {
                for (int click = _currentPowerPointSubSceneIndex; click < subSlide; click++)
                {
                    _currentPowerPointSubSceneIndex++;
                    _powerPointPresentation.SlideShowWindow.View.Next();
                }
            }
        }
       

        #region Event handlers
        private void PowerPoint_PresentationOpen(Presentation pres)
        {
            _powerApp.PresentationOpen -= PowerPoint_PresentationOpen;
            _powerPointPresentation = pres;

            try
            {   
                // get real slide indexes association (discard the hidden slides)
                _realSlideIndexesAssociation = new List<RealSlideIndexesAssociation>();
                int index = 0;
                for (int i = 0; i < pres.Slides.Count; i++)
                {
                    Slide currentSlide = pres.Slides[i + 1];
                    if (currentSlide.SlideShowTransition.Hidden == Microsoft.Office.Core.MsoTriState.msoFalse)
                    {
                        // the slide is not hidden!
                        index++;
                        _realSlideIndexesAssociation.Add(new RealSlideIndexesAssociation { Index = index, RealIndex = i + 1 });
                    }

                    Marshal.ReleaseComObject(currentSlide);
                    currentSlide = null;
                }


                // now start the presentation
                _powerPointPresentation.SlideShowSettings.ShowType = PpSlideShowType.ppShowTypeWindow;
                _powerPointPresentation.SlideShowSettings.ShowScrollbar = Microsoft.Office.Core.MsoTriState.msoFalse;
                _powerPointPresentation.SlideShowSettings.ShowMediaControls = Microsoft.Office.Core.MsoTriState.msoFalse;
                _powerPointPresentation.SlideShowSettings.ShowWithAnimation = Microsoft.Office.Core.MsoTriState.msoTrue;

                _powerApp.SlideShowBegin += PowerPoint_SlideShowBegin;
                _powerApp.SlideShowNextSlide += PowerPoint_SlideShowNextSlide;
                _powerApp.SlideShowEnd += PowerPoint_SlideShowEnd;

                _powerPointPresentation.SlideShowSettings.Run();
            }
            catch (Exception)
            {
                _openErroCallback?.Invoke();
            }
        }

        private void PowerPoint_SlideShowBegin(SlideShowWindow ssw)
        {
            _powerApp.SlideShowBegin -= PowerPoint_SlideShowBegin;

            try
            {
                if (_powerPointPresentation != null && ssw.HWND != _powerPointPresentation.SlideShowWindow.HWND)
                    return;

                _sceneContainer.BeginInvoke(new MethodInvoker(delegate
                {
                    try
                    {
                        _currentPowerPointSubSceneIndex = 0;
                        ssw.View.State = PpSlideShowState.ppSlideShowPaused;
                        if (SetWindowParent(ssw, Container, _openErroCallback))
                        {
                            _openCompletedCallback?.Invoke();
                        }

                        _openCompletedCallback = null;
                        _openErroCallback = null;
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            LogTracer.Instance.Trace(string.Format(@"Error during the PowerPoint SlideShowBegin event: {0} - PPT: {1} [PowerPoint_SlideShowBegin]", ex.Message, ssw.Presentation.Path), TraceEventType.Error);
                        }
                        catch (Exception)
                        {
                            LogTracer.Instance.Trace(string.Format(@"Error during the PowerPoint SlideShowBegin event: {0} - [PowerPoint_SlideShowBegin]", ex.Message), TraceEventType.Error);
                        }
                    }
                }));
            }
            catch (Exception ex)
            {
                LogTracer.Instance.Trace(string.Format(@"Error during the PowerPoint SlideShowBegin event: {0} - PPT: {1} [PowerPoint_SlideShowBegin]", ex.Message, ssw.Presentation.Path), TraceEventType.Error);
            }
        }

        private async void PowerPoint_SlideShowNextSlide(SlideShowWindow ssw)
        {
            try
            { 
                if (_powerPointPresentation != null && ssw.HWND != _powerPointPresentation.SlideShowWindow.HWND)
                    return;

                if (!_slideShowStarted || _lastCommand == null)
                    return;

            
                //N.B.: used to allow the video playback on first start avoiding the freeze effect
                ssw.View.State = PpSlideShowState.ppSlideShowPaused;
                await Task.Delay(100);
                ssw.View.State = PpSlideShowState.ppSlideShowRunning;
                    
                var currentRealSlideIndex = ssw.View.Slide.SlideIndex;
                var indexesAssociation = _realSlideIndexesAssociation.Find(x => x.RealIndex == currentRealSlideIndex);
                if (indexesAssociation == null)
                {
                    LogTracer.Instance.Trace(string.Format("Index can't be found for real index {0} on PPT: {1} [PowerPoint_SlideShowNextSlide]!", currentRealSlideIndex, ssw.Presentation.Path), TraceEventType.Error);
                   _lastCommand.Error?.Invoke();
                    return;
                }
                _currentSlideIndex = indexesAssociation.Index - 1;
                _slideChangeCompleted = true;

                int subSlides = 0;
                if (_subSlides != null)
                {
                    var sceneRec = _subSlides.Find(x => x.Slide == _currentSlideIndex + 1);
                    if (sceneRec != null)
                        subSlides = sceneRec.SubSlides;
                }

                if (_lastCommand.SubSlide > 0 && subSlides > 0)
                {
                    OnAdjustSubSlideAfterGotoSlide(_lastCommand.SubSlide);
                }

                Debug.WriteLine(string.Format("PowerPoint on slide {0}", _currentSlideIndex));

                _lastCommand.Completed?.Invoke();
            }
            catch (Exception ex)
            {
                try
                {
                    LogTracer.Instance.Trace(string.Format(@"Error during the PowerPoint SlideShowNextSlide event: {0} - PPT: {1} [PowerPoint_SlideShowNextSlide]", ex.Message, ssw.Presentation.Path), TraceEventType.Error);
                }
                catch (Exception)
                {
                    LogTracer.Instance.Trace(string.Format(@"Error during the PowerPoint SlideShowNextSlide event: {0} [PowerPoint_SlideShowNextSlide]", ex.Message), TraceEventType.Error);
                }
                if (_lastCommand != null)
                {
                    _lastCommand.Error?.Invoke();
                }
            }
        }

        private void PowerPoint_SlideShowEnd(Presentation pres)
        {
            _slideShowStarted = false;
            _slideChangeCompleted = true;
        }
        #endregion

        public void SetBounds(Rectangle bounds)
        {
            if (_powerPointPresentation != null && _powerPointPresentation.SlideShowWindow != null)
            {
                IntPtr hwnd = new IntPtr(_powerPointPresentation.SlideShowWindow.HWND);

                WindowUtility.MoveWindow(hwnd, -1, -1, bounds.Width + 2, bounds.Height + 2, true);
            }
        }
    }
}
