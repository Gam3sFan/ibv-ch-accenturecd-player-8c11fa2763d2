using ContentDistributionPlayer.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Transitions;

namespace ContentDistributionPlayer.Components
{
    class InfoMessage
    {
        private Control _ctrlParent;
        private Label _lblMessage;
        private PictureBoxWithOpacity _imgPreload;
        private bool _isMessageTransitionInProgress = false;
        private bool _isPreloadTransitionInProgress = false;
        public int MessageYPosition { get; set; }
        public int MessageExitYOffset { get; set; } = -15;
        public int MessageEnterYOffset { get; set; } = 15;

        private bool _hasErrorMessage = false;

        public InfoMessage(Control parentControl, Label message, PictureBoxWithOpacity preload)
        {
            _ctrlParent = parentControl;
            _lblMessage = message;
            _imgPreload = preload;

            _lblMessage.ForeColor = Color.White;
            _lblMessage.Visible = false;
            _imgPreload.Visible = false;
        }

        public async Task ShowErrorMessage(string message)
        {
            await ShowMessage(message, false, true);
        }

        public async Task ShowMessage(string message, bool showPreload = false, bool isError = false, Action showComplete = null)
        {
           _hasErrorMessage = isError;

            if (_isMessageTransitionInProgress)
            {
                await Task.Delay(10);
                // si propagano TUTTI i parametri: in precedenza isError e showComplete venivano persi
                await ShowMessage(message, showPreload, isError, showComplete);
                return;
            }

            _lblMessage.BringToFront();

            if (!_lblMessage.Visible)
            {
                _lblMessage.Text = message;
                _lblMessage.Visible = true;
            }
            else
            {
                // before I need to hide the current message...
                await HideMessage(new Action(() =>
                {
                    _ctrlParent.Invoke(new Action(async () =>
                    {
                        await Task.Delay(200);
                        
                        await ShowMessage(message, showPreload, isError, showComplete);
                    }));
                }));
                return;
            }

            _isMessageTransitionInProgress = true;
            _lblMessage.Top = MessageYPosition + MessageEnterYOffset;
            var tShow = new Transition(new TransitionType_Deceleration(500));
            tShow.TransitionCompletedEvent += (sender, e) =>
            {
                _ = _ctrlParent.Invoke(new Action(() =>
                  {
                      _isMessageTransitionInProgress = false;

                      showComplete?.Invoke();
                  }));
            };
            tShow.add(_lblMessage, "Top", MessageYPosition);
            tShow.add(_lblMessage, "ForeColor", Color.Black);
            tShow.run();

            if (showPreload)
            {
                ShowPreload();
            }
            else
            {
                HidePreload();
            }
        }

        public async Task CleanErrorMessage(Action hideComplete = null)
        {
            if (_hasErrorMessage)
                await HideMessage(hideComplete);
        }

        public async Task HideMessage(Action hideComplete = null)
        {
            if (_isMessageTransitionInProgress)
            {
                await Task.Delay(10);
                await HideMessage(hideComplete);
                return;
            }

            if (_lblMessage.Visible)
            {
                _isMessageTransitionInProgress = true;
                var tHide = new Transition(new TransitionType_Acceleration(200));
                tHide.TransitionCompletedEvent += (sender, e) =>
                {
                    _ = _ctrlParent.Invoke(new Action(() =>
                      {
                          _isMessageTransitionInProgress = false;

                          _lblMessage.Visible = false;
                          _hasErrorMessage = false;

                          hideComplete?.Invoke();
                      }));
                    
                };
                tHide.add(_lblMessage, "ForeColor", Color.White);
                tHide.add(_lblMessage, "Top", MessageYPosition + MessageExitYOffset);
                tHide.run();
            }
            else
            {
                _hasErrorMessage = false;
                hideComplete?.Invoke();
            }

            HidePreload();
        }

        public async void HidePreload()
        {
            if (_isPreloadTransitionInProgress)
            {
                await Task.Delay(10);
                HidePreload();
                return;
            }

            if (!_imgPreload.Visible)
                return;

            _isPreloadTransitionInProgress = true;
            Transition tPreload = new Transition(new TransitionType_Acceleration(200));
            tPreload.TransitionCompletedEvent += (sender, e) =>
            {
                _ = _ctrlParent.Invoke(new Action(() =>
                {
                    _imgPreload.Visible = false;

                    _isPreloadTransitionInProgress = false;
                }));
            };
            tPreload.add(_imgPreload, "Opacity", 1f);
            tPreload.run();
        }

        public async void ShowPreload()
        {
            if (_isPreloadTransitionInProgress)
            {
                await Task.Delay(10);
                ShowPreload();
                return;
            }

            if (_imgPreload.Visible)
                return;


            _imgPreload.BringToFront();
            _isPreloadTransitionInProgress = true;
            _imgPreload.Opacity = 0;
            _imgPreload.Visible = true;
            Transition tPreloader = new Transition(new TransitionType_Deceleration(500));
            tPreloader.TransitionCompletedEvent += (sender, e) =>
            {
                _ = _ctrlParent.Invoke(new Action(() =>
                {
                    _isPreloadTransitionInProgress = false;
                }));
            };
            tPreloader.add(_imgPreload, "Opacity", 1f);
            tPreloader.run();
        }
    }
}
