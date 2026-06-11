using ContentDistributionPlayer.Extensions;
using ContentDistributionPlayer.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Transitions;

namespace ContentDistributionPlayer.Components
{
    public delegate void PM_EventError(string message);
    public delegate void PM_EventGotoSceneComplete(int sceneIndex, int subSceneIndex);
    public delegate void PM_EventSucces();
    public delegate void PM_EventShowSceneContentError(int sceneIndex, int contentIndex, int errorCode);
    public delegate void PM_EventBoolParam(bool boolParam);
    
    class PresentationManager
    {
        public Form MainContainer { get; private set; }

        #region Presentation data
        private JObject _presentation;
        private string _presentationPath;
        private JArray _scenes;
        private int _previousSceneIndex = -1;
        private int _currentSceneIndex = -1;
        public int GetCurrentSceneIndex()
        {
            return _currentSceneIndex;
        } 
        private int _currentSubSceneIndex = -1;
        public int GetCurrentSubSceneIndex()
        {
            return _currentSubSceneIndex;
        }

        private Panel _panScenesContainer;
        private Panel _panLiveContentContainer;

        private SceneManager[] _sceneManager;
        private List<int> _sceneIndexPreloadSchemaList;
        private int _asyncPreloadSceneManagerIndex = 0;
        
        private bool _isGotoSceneInProgress = false;

        private WebClient _currentClientDownload = null;
        private int _currentFileDataIndex;
        private FileData[] _currentFileDatas;
        private bool _isCurrentDownloadFromLiveContent;
        private PM_EventSucces _fileDownloadSuccessCallback;
        private PM_EventError _fileDownloadErrorCallback;

        public bool IsUnloaded { get; private set; } = true;
        #endregion

        #region Events
        public PM_EventError OnError;
        public PM_EventGotoSceneComplete OnGotoSceneComplete;
        public PM_EventShowSceneContentError OnSceneContentError;
        public PM_EventSucces OnLoadScenesStart;
        public PM_EventSucces OnLoadScenesEnd;
        public PM_EventSucces OnRealtimeCommunicationReconnect;
        public PM_EventBoolParam OnDownloadLiveContent;
        public PM_EventBoolParam OnLoadContentLiveContent;
        public PM_EventBoolParam OnShowCover;
        #endregion

        #region Properties
        public Color BackgroundColor { get; set; } = Color.White;

        private readonly string _basePresentationsPath;
        private bool _isSceneShowed = false;
        private bool _purgePresentationData;
        #endregion

        #region Live content properties
        private ControlObjectElement _liveDocument;
        private JArray _livePages;
        private JObject _liveResult;
        private int _liveResourceId;
        private string _liveResourceType;
        private int _liveCurrentSceneIndex = -1;
        private int _liveCurrentSubSceneIndex = -1;
        private JArray _liveContentNumberOfSubScene;
        #endregion

        #region Command syncronization properties
        private class GotoSceneCommandVO
        {
            public int SceneIndex { get; set; }
            public int SubSceneIndex { get; set; }
        }

        private List<GotoSceneCommandVO> _gotoSceneCommandsQueue;
        #endregion

        #region Unique file management per scenes

        public class SceneContentPosition
        {
            public int SceneIndex { get; set; }
            public int ContentIndex { get; set; }
        }

        public class ResourceInScene
        {
            public int ResourceId { get; set; }
            public ControlObjectElement ControlObjectElement { get; set; }
            public List<SceneContentPosition> SceneIndexes { get; set; }
        }

        private List<ResourceInScene> _uniqueResourcesInScenes;

        public ResourceInScene GetUniqueResourceInScenes(int resourceId, int[] sceneIndexes)
        {
            // looking for the resource by id 
            var result = _uniqueResourcesInScenes?.Find(x => x.ResourceId == resourceId);
            if (result != null)
            {
                // same resource...

                // check if there already are the same resource in the scene...
                bool alreadyInScene = (result.SceneIndexes.Find(delegate (SceneContentPosition scp)
                {
                    foreach (var si in sceneIndexes)
                    {
                        if (si == scp.SceneIndex)
                            return true;
                    }
                    return false;
                }) != null);

                if (!alreadyInScene)
                    return result;

                // ... otherwise will return null so the caller can create a new instance
            }

            return null;
        }

        public void AddUniqueResourceInScenes(int resourceId, ControlObjectElement controlObjectElement, int[] sceneIndexes, int contentIndex)
        {
            if (_uniqueResourcesInScenes == null)
                _uniqueResourcesInScenes = new List<ResourceInScene>();

            var ris = new ResourceInScene
            {
                ResourceId = resourceId,
                ControlObjectElement = controlObjectElement,
                SceneIndexes = new List<SceneContentPosition>()
            };

            foreach (var si in sceneIndexes)
            {
                ris.SceneIndexes.Add(new SceneContentPosition
                {
                    SceneIndex = si,
                    ContentIndex = contentIndex
                });
            }
            _uniqueResourcesInScenes.Add(ris);
        }

        public ControlObjectElement FindControlObjectElementByControlContainer(Control container)
        {
            var resource = _uniqueResourcesInScenes?.Find(delegate (ResourceInScene ris)
            {
                if (ris.ControlObjectElement != null && ris.ControlObjectElement != null && ris.ControlObjectElement.Container != null && ris.ControlObjectElement.Container.Equals(container))
                    return true;
                return false;
            });

            if (resource != null)
                return resource.ControlObjectElement;
            return null;
        }

        #endregion

        public PresentationManager(Form mainContainer, Panel panScenesContentsContainer, Panel panLiveContentContainer, string basePresentationsPath, bool purgePresentationData)
        {
            MainContainer = mainContainer;
            _panScenesContainer = panScenesContentsContainer;
            _panLiveContentContainer = panLiveContentContainer;
            _basePresentationsPath = basePresentationsPath;
            _purgePresentationData = purgePresentationData;
        }

        private bool SetPresentationData(JObject data)
        {
            if (data == null)
            {
                LogTracer.Instance.Trace("The presentation data is empty!", TraceEventType.Error);
                return false;
            }

            LogTracer.Instance.Trace(string.Format("Set the presentation data: {0}", data.ToString()));

            if (string.IsNullOrEmpty(_basePresentationsPath))
            {
                LogTracer.Instance.Trace("The base presentation path is empty!", TraceEventType.Error);
                return false;
            }

            _presentation = data;

            // check or create the local presentation path
            _presentationPath = Path.Combine(_basePresentationsPath, _presentation.Get("id", 0).ToString());
            if (!Directory.Exists(_presentationPath))
            {
                // create the presentation directory
                LogTracer.Instance.Trace(string.Format("Create the presentation directory: {0}", _presentationPath));

                Directory.CreateDirectory(_presentationPath);
            }

            // set the presentation enter transition effect
            var enterTransition = _presentation.Get<JObject>("enter_transition");
            if (enterTransition != null)
            {
                var (_enterTransition, errorMsg) = SceneTransition.FromJObject(enterTransition);
                if (errorMsg != null)
                {
                    LogTracer.Instance.Trace(errorMsg, TraceEventType.Error);
                    return false;
                }
            }

            _isGotoSceneInProgress = false;
            
            // now get the scenes data            
            return SetScenesData(_presentation.Get<JArray>("scenes"));
        }

        public string PresentationColor
        {
            get
            {
                if (_presentation != null)
                {
                    return _presentation.Get<string>("background_color");
                }
                return null;
            }
        }

        private bool SetScenesData(JArray scenes)
        {
            // get the scenes information
            if (scenes == null || scenes.Count == 0)
            {
                var errorMsg = @"Unable to find the scenes information";
                LogTracer.Instance.Trace(string.Format("{0} - presentation {1}", errorMsg, _presentation.Get<int>("id")), TraceEventType.Error);
                OnError?.Invoke(errorMsg);
                OnSceneContentError?.Invoke(-1, -1, RealtimeCommunication.ERR_CODE_PRESENTATION_CONTENT_ERROR);
                return false;
            }
            
            _scenes = scenes;
            
            return true;
        }

        private bool AddLocalFileNamePathToScenesData(string fileName, string localFile)
        {
            // search all the same file name in the scenes data list
            bool almostOneFound = false;
            if (_scenes != null)
            {
                foreach (JObject scene in _scenes)
                {
                    if (scene != null)
                    {
                        var contents = scene.Get<JArray>("contents");
                        if (contents != null)
                        {
                            foreach (JObject content in contents)
                            {
                                if (content != null)
                                {
                                    string file = content.Get<string>("file");
                                    if (file == fileName)
                                    {
                                        content["localFile"] = localFile;
                                        almostOneFound = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return almostOneFound;
        }

        public void Initialize(JObject presentationData, PM_EventSucces successCallback, PM_EventError errorCallback)
        {
            LogTracer.Instance.Trace("Presentation initialization");

            _isSceneShowed = false;

            PurgeAllUnterminatedDownloadPresentationDocuments();

            if (!SetPresentationData(presentationData))
            {
                errorCallback?.Invoke(@"SetPresentationData error");
                return;
            }

            // start to download locally all the presentation documents
            var (fileDatas, errorMsg) = SceneManager.GetDocumentFilesFromSceneData(_scenes);
            if (!string.IsNullOrEmpty(errorMsg))
            {
                LogTracer.Instance.Trace(string.Format("{0} - presentation {1}", errorMsg, _presentation.Get<int>("id")), TraceEventType.Error);
                OnError?.Invoke(errorMsg);
                errorCallback?.Invoke(errorMsg);
                return;
            }

            // if fileNames are null or empty the presentation contents could consist of online elements only 
            if (fileDatas != null)
            {
                LogTracer.Instance.Trace(string.Format("Starting the download process to copy all the presentation files in the local folder '{0}'", _presentationPath));
                DownloadRemoteFiles(fileDatas, false, successCallback, errorCallback);
                return;
            }
            
            LogTracer.Instance.Trace("All the presentation files are online resources (no locally download needed).");
            
            IsUnloaded = false;
            successCallback?.Invoke();
        }

        public void DownloadRemoteFiles(FileData[] fileDatas, bool isLiveContent, PM_EventSucces successCallback, PM_EventError errorCallback)
        {
            StopCurrentClientDownload();
            _currentClientDownload = new WebClient();
            _currentClientDownload.DownloadFileCompleted += CurrentClientDownload_DownloadFileCompleted;
            _currentFileDataIndex = 0;
            _currentFileDatas = fileDatas;
            _isCurrentDownloadFromLiveContent = isLiveContent;
            _fileDownloadSuccessCallback = successCallback;
            _fileDownloadErrorCallback = errorCallback;

            DownloadNextFile();
        }

        private void ResetDownloadFileVariables()
        {
            StopCurrentClientDownload();
            _currentFileDataIndex = 0;
            _currentFileDatas = null;
            _isCurrentDownloadFromLiveContent = false;
            _fileDownloadSuccessCallback = null;
            _fileDownloadErrorCallback = null;
        }

        private void CurrentClientDownload_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            MainContainer.Invoke(new Action(() =>
            {
                FileData fileData = _currentFileDatas[_currentFileDataIndex];

                if (e.Error != null)
                {
                    HttpStatusCode httpStatusCode = Utilities.WebUtility.GetHttpStatusCode(e.Error);
                    string errorMsg = @"Error downloading the the remote file";
                    LogTracer.Instance.Trace(string.Format("{0} '{1}' to the scene JSON data - presentation {2}: {3}", errorMsg, fileData.FileName, _presentation.Get<int>("id"), e.Error.Message), System.Diagnostics.TraceEventType.Error);
                    OnError?.Invoke(errorMsg);

                    _fileDownloadErrorCallback?.Invoke(errorMsg);
                    ResetDownloadFileVariables();
                    return;
                }
                else
                {
                    if (!e.Cancelled)
                    {
                        // Downloaded OK
                        string errorMsg;

                        if (!File.Exists(fileData.LocalFile + FileUtility.DOWNLOADING_FILE_POSTFIX))
                        {
                            errorMsg = @"Unable to find the downloaded file";
                            LogTracer.Instance.Trace(string.Format("{0} {1} - presentation {2}", errorMsg, fileData.LocalFile + FileUtility.DOWNLOADING_FILE_POSTFIX, _presentation.Get<int>("id")), TraceEventType.Error);
                            OnError?.Invoke(errorMsg);
                            _fileDownloadErrorCallback?.Invoke(errorMsg);
                            ResetDownloadFileVariables();
                            return;
                        }

                        // rename the file downloaded
                        File.Move(fileData.LocalFile + FileUtility.DOWNLOADING_FILE_POSTFIX, fileData.LocalFile);


                        // check if the download worked fine
                        if (!File.Exists(fileData.LocalFile))
                        {
                            errorMsg = @"Unable to download the file locally";
                            LogTracer.Instance.Trace(string.Format("{0} {1} - presentation {2}", errorMsg, fileData.FileName, _presentation.Get<int>("id")), System.Diagnostics.TraceEventType.Error);
                            OnError?.Invoke(errorMsg);
                            _fileDownloadErrorCallback?.Invoke(errorMsg);
                            ResetDownloadFileVariables();
                            return;
                        }

                        if (!_isCurrentDownloadFromLiveContent)
                        {
                            // add the local file path to the scenes data JSON list to avoid new path generation during the scene play
                            if (!AddLocalFileNamePathToScenesData(fileData.FileName, fileData.LocalFile))
                            {
                                errorMsg = @"Unable to add the local file path";
                                LogTracer.Instance.Trace(string.Format("{0} '{1}' to the scene JSON data - presentation {2}", errorMsg, fileData.LocalFile, _presentation.Get<int>("id")), System.Diagnostics.TraceEventType.Error);
                                OnError?.Invoke(errorMsg);
                                _fileDownloadErrorCallback?.Invoke(errorMsg);
                                ResetDownloadFileVariables();
                                return;
                            }
                        }

                        _currentFileDataIndex++;
                        DownloadNextFile();
                    }
                    else
                    {
                        // this situation is true when the room controller unload the room while the client is downloading the presentation data
                        _fileDownloadSuccessCallback?.Invoke();
                        ResetDownloadFileVariables();
                    }
                }
            }));
        }

        private void DownloadNextFile()
        {
            if (_currentClientDownload == null)
            {
                // the donwload process is stopped by a new NodeJS derective
                ResetDownloadFileVariables();
                return;
            }

            if (_currentFileDatas == null || _currentFileDataIndex < 0 || _currentFileDataIndex >= _currentFileDatas.Length)
            {
                // end of the file data lists       
                IsUnloaded = false;
                _fileDownloadSuccessCallback?.Invoke();
                ResetDownloadFileVariables();
                return;
            }

            FileData fileData = _currentFileDatas[_currentFileDataIndex];
            if (fileData == null)
            {
                // pass to the next file data
                _currentFileDataIndex++;
                DownloadNextFile();
                return;
            }

            string errorMsg = null;

            if (fileData.FileName.ToLower().IndexOf(@"http://") != -1 ||
                fileData.FileName.ToLower().IndexOf(@"https://") != -1 ||
                fileData.FileName.ToLower().IndexOf(@"file://") != -1)
            {
                LogTracer.Instance.Trace(string.Format("Found a document resource: '{0}'", fileData.FileName));

                // get only the file name from url
                var uriSegs = new Uri(fileData.FileName).Segments;
                string fileNameOnly = uriSegs.Last();

                // add the version code to the file name
                string fileExt = Path.GetExtension(fileNameOnly);
                int posExt = fileNameOnly.LastIndexOf(fileExt);
                if (posExt != -1)
                {
                    fileNameOnly = string.Format("{0}-{1}-{2}{3}", fileData.ResourceId, fileNameOnly.Substring(0, posExt), fileData.Version, fileExt);
                }
                else
                {
                    fileNameOnly = string.Format("{0}-{1}-{2}", fileData.ResourceId, fileNameOnly.Substring(0, posExt), fileData.Version);
                }

                fileData.LocalFile = Path.Combine(_presentationPath, fileNameOnly);

                // if the local file already exists it will not download again (if the version number is the same)
                if (!File.Exists(fileData.LocalFile))
                {
                    LogTracer.Instance.Trace(string.Format("Download process started for {0}", fileData.FileName));

                    try
                    {
                        // trying to download locally 
                        _currentClientDownload.DownloadFileAsync(new Uri(fileData.FileName), fileData.LocalFile + FileUtility.DOWNLOADING_FILE_POSTFIX);
                    }
                    catch (Exception ex)
                    {
                        if (_currentClientDownload == null)
                        {
                            // this situation is true when the room controller unload the room while the client is downloading the presentation data
                            _fileDownloadSuccessCallback?.Invoke();
                            ResetDownloadFileVariables();
                        }
                        else
                        {
                            // error downloading remote file
                            errorMsg = string.Format(@"Error downloading the remote file {0} - {1}", fileData.FileName, ex.Message);
                            LogTracer.Instance.Trace(errorMsg, System.Diagnostics.TraceEventType.Error);
                            OnError?.Invoke(errorMsg);
                            _fileDownloadErrorCallback?.Invoke(errorMsg);
                            ResetDownloadFileVariables();
                        }
                    }
                    return;
                }
                else
                {
                    LogTracer.Instance.Trace(string.Format("A local file version of the document already exists ({0}) and it will not download again", fileData.LocalFile));
                }

                if (!_isCurrentDownloadFromLiveContent)
                {
                    // add the local file path to the scenes data JSON list to avoid new path generation during the scene play
                    if (!AddLocalFileNamePathToScenesData(fileData.FileName, fileData.LocalFile))
                    {
                        errorMsg = @"Unable to add the local file path";
                        LogTracer.Instance.Trace(string.Format("{0} '{1}' to the scene JSON data - presentation {2}", errorMsg, fileData.LocalFile, _presentation.Get<int>("id")), System.Diagnostics.TraceEventType.Error);
                        OnError?.Invoke(errorMsg);
                        _fileDownloadErrorCallback?.Invoke(errorMsg);
                        ResetDownloadFileVariables();
                        return;
                    }
                }
            }

            _currentFileDataIndex++;
            DownloadNextFile();
        }

        private void StopCurrentClientDownload()
        {
            if (_currentClientDownload != null)
            {
                if (_currentClientDownload.IsBusy)
                    _currentClientDownload.CancelAsync();
                _currentClientDownload.DownloadFileCompleted -= CurrentClientDownload_DownloadFileCompleted;
                _currentClientDownload.Dispose();
            }
            _currentClientDownload = null;
        }

        public void Unload(bool purgeDocument = true, bool quitDocumentApp = false)
        {
            if (_presentation == null)
                return;

            IsUnloaded = true;

            if (_sceneManager != null)
            {
                foreach (SceneManager sm in _sceneManager)
                {
                    if (sm != null)
                    {
                        sm.CloseAllDocuments(quitDocumentApp);
                    }
                }
            }
            _sceneManager = null;

            if (_panScenesContainer != null)
                _panScenesContainer.Visible = false;

            _uniqueResourcesInScenes?.Clear();
            _uniqueResourcesInScenes = null;

            _isSceneShowed = false;
            _isGotoSceneInProgress = false;

            if (purgeDocument && _purgePresentationData)
                PurgeAllPresentationsDocuments();
        }

        public void Restart()
        {
            // kill all the office app process (to ensure that PowerPoint is not freezed)
            DocumentsUtility.KillAllOfficeProcesses();

            Unload(false, true);


            // reload all presentation data
            _previousSceneIndex = -1;
            _currentSceneIndex = -1;
            _currentSubSceneIndex = -1;
       
            OnRealtimeCommunicationReconnect?.Invoke();
        }

        private void PurgeAllUnterminatedDownloadPresentationDocuments()
        {
            // now purge all local file having the postfix FileUtility.DOWNLOADING_FILE_POSTFIX
            StopCurrentClientDownload();

            FileUtility.PurgeAllUnterminatedDownloadFiles(_presentationPath);
        }

        private void PurgeAllPresentationsDocuments()
        {
            // now purge all local presentations documents
            if (Directory.Exists(_basePresentationsPath))
            {
                LogTracer.Instance.Trace(string.Format("Delete all the local presentations documents in folder: {0}", _basePresentationsPath));

                StopCurrentClientDownload();

                string[] subDirectories = Directory.GetDirectories(_basePresentationsPath);
                foreach (string subDir in subDirectories)
                {
                    // int retry = 0;
                    // bool exit = false;
                    string dirToDelete = Path.Combine(_basePresentationsPath, subDir);

                    /* removed to avoid file lock and delete process continue in background removing the new downloaded file of other presentation!!!
                    do
                    {
                    */
                        try
                        {
                            LogTracer.Instance.Trace(string.Format("Deleting directory {0}...", dirToDelete));

                            Directory.Delete(dirToDelete, true);

                            LogTracer.Instance.Trace(string.Format("Directory {0} deleted!", dirToDelete));

                            // exit = true;
                        }
                        catch (Exception)
                        {
                            // trying to close Office Program
                            DocumentsUtility.KillAllOfficeProcesses();
                            /* removed to avoid file lock and delete process continue in background removing the new downloaded file of other presentation!!!
                            retry++;
                            await Task.Delay(1000);*/
                        }
                    /* removed to avoid file lock and delete process continue in background removing the new downloaded file of other presentation!!!    
                    } while (!exit && retry < 20);
                    */
                }
            }
        }

        public async void GotoScene(int sceneIndex = -1, int subSceneIndex = -1)
        {
            LogTracer.Instance.Trace(string.Format("Goto presentation scene index {0} (subscene index {1})", sceneIndex, subSceneIndex));

            if (_presentation == null)
            {
                LogTracer.Instance.Trace("Presenntation is null!Ggo to scene command cannot continue", TraceEventType.Error);
                return;
            }

            // manage a gotoScene commands queue
            if (_gotoSceneCommandsQueue != null && _gotoSceneCommandsQueue.Count > 0)
            {
                // add the command to the queue
                AddGotoSceneCommandToQueue(sceneIndex, subSceneIndex);
                return;
            }

            if (_isGotoSceneInProgress)
                return;
            _isGotoSceneInProgress = true;

            if (_scenes == null || _scenes.Count == 0)
            {
                var errorMsg = @"Unable to find the scenes information";
                LogTracer.Instance.Trace(string.Format("{0} - presentation {1}", errorMsg, _presentation.Get<int>("id")), System.Diagnostics.TraceEventType.Error);
                OnError?.Invoke(errorMsg);
                OnSceneContentError?.Invoke(-1, -1, RealtimeCommunication.ERR_CODE_PRESENTATION_CONTENT_ERROR);
                ClearGotoSceneCommandQueue();
                _isGotoSceneInProgress = false;
                return;
            }

            if (sceneIndex == -1)
                sceneIndex = _currentSceneIndex;

            if (subSceneIndex == -1)
                subSceneIndex = _currentSubSceneIndex;

            if (sceneIndex < 0)
            {
                var errorMsg = string.Format(@"Unable to find the scene number {0}", sceneIndex + 1);
                LogTracer.Instance.Trace(string.Format("{0} - presentation {1}", errorMsg, _presentation.Get<int>("id")), System.Diagnostics.TraceEventType.Error);
                //OnError?.Invoke(errorMsg);
                ClearGotoSceneCommandQueue();
                _isGotoSceneInProgress = false;
                return;
            }

            if (sceneIndex >= _scenes.Count)
            {
                var errorMsg = string.Format(@"Unable to find the scene number {0} of {1} scenes", sceneIndex + 1, _scenes.Count);
                LogTracer.Instance.Trace(string.Format("{0} - presentation {1}", errorMsg, _presentation.Get<int>("id")), System.Diagnostics.TraceEventType.Error);
                //OnError?.Invoke(errorMsg);
                ClearGotoSceneCommandQueue();
                _isGotoSceneInProgress = false;
                return;
            }

            _previousSceneIndex = _currentSceneIndex;
            _currentSceneIndex = sceneIndex;
            _currentSubSceneIndex = (subSceneIndex < 0 ? 0 : subSceneIndex);

            if (_sceneManager == null)
            {
                OnLoadScenesStart?.Invoke();
                
                // I need this delay to avoid the flickering during loading message shows
                await Task.Delay(500);

                _sceneIndexPreloadSchemaList = new List<int>();
                for (int j = 0; j < _scenes.Count; j++)
                {
                    var idx = GetValidPreloadSceneIndex(j);
                    if (idx >= 0)
                        _sceneIndexPreloadSchemaList.Add(idx);
                }

                _sceneManager = new SceneManager[_sceneIndexPreloadSchemaList.Count];
    
                // now start loading all the scene in the scene manager in memory array
                LoadNextScene(0);
            }
            else
            {
                _isGotoSceneInProgress = false;

                // show the scene contents
                ShowTheSceneContents();
            }
        }
        
        public void ShowCurrentLoadedScene(PM_EventSucces success = null, PM_EventError error = null)
        {
            if (_isGotoSceneInProgress || _sceneManager == null || _scenes == null || _currentSceneIndex < 0 || _currentSceneIndex >= _scenes.Count)
                return;
            
            Debug.WriteLine("**** GOTO SCENE: " + _currentSceneIndex + "  SUB: " + _currentSubSceneIndex);

            SceneManager currentScene = GetSceneManagerByIndex(_currentSceneIndex);
            if (currentScene != null)
            {
                //N.B.: if the previousScene == null means that the client is entered in the room when the presentation is already started (due to a disconnection/re-connection) so the client may not has a previousScene status.
                //      Also in this situation I need to check if there are some powerpoint in the scene and set their slides number to the values presente in each params data
                SceneManager previousScene = null;
                List<ResourceInScene> resourcesInPreviousScene = null;
                if (_previousSceneIndex >= 0)
                {
                    previousScene = GetSceneManagerByIndex(_previousSceneIndex);

                    // get the previous scene resource ids
                    resourcesInPreviousScene = _uniqueResourcesInScenes?.FindAll(x => x.SceneIndexes.Find(y => y.SceneIndex == _previousSceneIndex) != null);
                }

                bool isTheSameSceneStructure = IsTheSameScene(_currentSceneIndex, _previousSceneIndex);

                if (_previousSceneIndex == -1 || (_previousSceneIndex >= 0 && _previousSceneIndex < _scenes.Count))
                {
                    var currentSceneData = currentScene.GetSceneData(_currentSceneIndex);
                    if (currentSceneData != null && currentScene.ControlObjectElements != null)
                    {
                        // hide the cover image (if it is visible)
                        OnShowCover?.Invoke(false);

                        List<ZIndexSceneElement> zIndexList = new List<ZIndexSceneElement>();

                        // for each PowerPoint documents move theme inside slide by slide
                        for (int idx = 0; idx < currentScene.ControlObjectElements.Length; idx++)
                        {
                            ControlObjectElement coe = currentScene.ControlObjectElements[idx];

                            // get the coe content index in the json data structure
                            var uniqueResource = _uniqueResourcesInScenes?.Find(x => x.ControlObjectElement.Equals(coe));
                            if (uniqueResource == null)
                            {
                                string errorMsg = string.Format("Unable to find the loaded control object element {0} in the unique resource list!", coe.FileName);
                                LogTracer.Instance.Trace(errorMsg, TraceEventType.Error);
                                error?.Invoke(errorMsg);
                                return;
                            }
                            var uniqueSceneContentPosition = uniqueResource.SceneIndexes.Find(x => x.SceneIndex == _currentSceneIndex);
                            if (uniqueSceneContentPosition == null)
                            {
                                string errorMsg = string.Format("Unable to find the loaded control object element {0} content index in the unique resource list!", coe.FileName);
                                LogTracer.Instance.Trace(errorMsg, TraceEventType.Error);
                                error?.Invoke(errorMsg);
                                return;
                            }
                            var contentIndex = uniqueSceneContentPosition.ContentIndex;

                            JArray contents = currentSceneData.Get<JArray>("contents");
                            if (contents != null && contentIndex >= 0 && contentIndex < contents.Count)
                            {
                                JObject content = contents[contentIndex] as JObject;
                                if (content != null)
                                {
                                    var resourceType = content.Get<string>("resource_type");

                                    var zIndex = content.Get<int>("z_index");

                                    zIndexList.Add(new ZIndexSceneElement()
                                    {
                                        ControlObjectElement = coe,
                                        ZIndex = zIndex
                                    });

                                    if (DocumentsUtility.IsPowerPoint(coe.FileName))
                                    {
                                        // now read the slide to params attribute

                                        JObject parameters = content.Get<JObject>("params");
                                        if (parameters != null)
                                        {
                                            int slide = parameters.Get<int>("slide", 0);
                                            int subSlides = parameters.Get<int>("sub_slides", 0);

                                            // update the bounding informations
                                            Rectangle bounds = currentScene.GetSceneContentBounds(content);
                                            coe.SetBounds(bounds);

                                            coe.SetPowerPointSubSlides(slide, subSlides);
                                            coe.SendPowerPointCommand(new PowerPointObject.PowerPointCommand
                                            {
                                                Type = PowerPointObject.PowerPointCommandType.GotoSlide,
                                                Slide = slide - 1,
                                                SubSlide = _currentSubSceneIndex,
                                                CameFromDisconnect = !_isSceneShowed,
                                                Completed = () =>
                                                {
                                                    Debug.WriteLine("**** COMMAND SENDED ||| GOTO SCENE: " + _currentSceneIndex + "  SUB: " + _currentSubSceneIndex);
                                                },
                                                Error = () =>
                                                {
                                                    OnSceneContentError?.Invoke(_currentSceneIndex, idx, RealtimeCommunication.ERR_CODE_PPT_ERROR);
                                                }
                                            });
                                        }
                                    }
                                    else if (DocumentsUtility.IsVideo(coe.FileName))
                                    {
                                        // now I always send the play video command and the control object element knows if the video is already in play (avoiding to start it again) or not (starts the video play)
                                        coe.PlayVideo();
                                    }
                                    else if (DocumentsUtility.IsWebsite(coe.FileName))
                                    {
                                        var prevCoeFound = resourcesInPreviousScene?.Find(x => x.ControlObjectElement.Equals(coe));

                                        if (prevCoeFound == null && _currentSceneIndex != _previousSceneIndex)
                                            coe.NavigateWeb();
                                    }
                                    else if (resourceType == DocumentsUtility.PDF_RESOURCE_TYPE || 
                                             resourceType == DocumentsUtility.WORD_RESOURCE_TYPE ||
                                             resourceType == DocumentsUtility.EXCEL_RESOURCE_TYPE)
                                    {
                                        // change the png showed in the PictureBox
                                        coe.ChangeImage(content.Get<string>("localFile"));
                                    }
                                }
                            }
                        }

                        // adjust the contents z-index
                        zIndexList.Sort(delegate (ZIndexSceneElement a, ZIndexSceneElement b)
                        {
                            if (a == null || b == null) return 0;
                            if (a.ZIndex < b.ZIndex) return -1;
                            if (a.ZIndex > b.ZIndex) return 1;
                            return 0;
                        });

                        for (int i = 0; i < zIndexList.Count; i++)
                        {
                            if (zIndexList[i] != null)
                            {
                                zIndexList[i].ControlObjectElement.Container.BringToFront();
                            }
                        }

                        // no transition
                        if (!_panScenesContainer.Visible)
                        {
                            _panScenesContainer.Visible = true;
                            _panScenesContainer.BringToFront();
                        }
                    }
                    else
                    {
                        // show the cover image (if it exists)
                        OnShowCover?.Invoke(true);
                    }
                }

                _isSceneShowed = true;

                currentScene.DestroyContentsNotInScene(() =>
                {
                    success?.Invoke();
                });
            }
        }   

        private int GetValidPreloadSceneIndex(int index)
        {
            // check if the index is already in the list or if the scene data is equals to one already present
            bool forceExit = false;
            do
            {
                bool found = false;
                // search the same preload index
                foreach (int idx in _sceneIndexPreloadSchemaList)
                {
                    if (idx == index)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // search the same scene data
                    var sceneData = GetSceneDataByIndex(index);
                    if (sceneData != null)
                    {
                        foreach (int idx in _sceneIndexPreloadSchemaList)
                        {
                            var sd = GetSceneDataByIndex(idx);
                            if (IsTheSameScene(sceneData, sd))
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        index = -1;
                        forceExit = true;
                    }
                }

                if (!forceExit)
                {
                    if (!found)
                    {
                        forceExit = true;
                    }
                    else
                    {
                        index++;
                        if (index < 0 || index >= _scenes.Count)
                        {
                            index = -1;
                            forceExit = true;
                        }
                    }
                }
            }
            while (!forceExit);

            return index;
        }

        public JObject GetSceneDataByIndex(int index)
        {
            if (_scenes == null || index < 0 || index >= _scenes.Count)
                return null;

            return _scenes[index] as JObject;
        }

        private SceneManager GetSceneManagerByIndex(int index)
        {
            if (_sceneManager == null || _scenes == null || index < 0 || index >= _scenes.Count)
                return null;

            SceneManager sm = Array.Find(_sceneManager, (x) => {
                return x != null && Array.Exists(x.SceneIndexes, y => y == index);
            });
            return sm;
        }
        
        private bool IsTheSameScene(int sceneIndex1, int sceneIndex2)
        {
            var sceneData1 = GetSceneDataByIndex(sceneIndex1);
            var sceneData2 = GetSceneDataByIndex(sceneIndex2);
            return IsTheSameScene(sceneData1, sceneData2);
        }

        private bool IsTheSameScene(JObject sceneData1, JObject sceneData2)
        {
            if (sceneData1 == null || sceneData2 == null)
                return false;

            // check the following properties:
            //
            // 1) contents items are the same
            //
            //      1.a) resourceId is the same
            //
            //      1.b) bounds are the same
            //
            //      1.c) zIndex is the same

            JArray contents1 = sceneData1.Get<JArray>("contents");
            JArray contents2 = sceneData2.Get<JArray>("contents");
            if (contents1 == null || contents2 == null || contents1.Count != contents2.Count)
                return false;

            for (int contentIdx = 0; contentIdx < contents1.Count; contentIdx++)
            {
                if (!(contents1[contentIdx] is JObject content1) || !(contents2[contentIdx] is JObject content2))
                    return false;

                // 1.a)
                int resId1 = content1.Get<int>("resource_id");
                int resId2 = content2.Get<int>("resource_id");
                if (resId1 != resId2)
                    return false;

                // 1.b)
                JArray bounds1 = content1.Get<JArray>("bounds");
                JArray bounds2 = content2.Get<JArray>("bounds");
                if (bounds1 == null || bounds2 == null || bounds1.Count != bounds2.Count)
                    return false;
                for (int boundIdx = 0; boundIdx < bounds1.Count; boundIdx++)
                {
                    if (NumberUtility.IsFloat(bounds1[boundIdx].ToString()) && NumberUtility.IsFloat(bounds2[boundIdx].ToString()))
                    {
                        if (bounds1[boundIdx].Value<float>() != bounds2[boundIdx].Value<float>())
                        {
                            return false;
                        }
                    }
                    else if (NumberUtility.IsInt(bounds1[boundIdx].ToString()) && NumberUtility.IsInt(bounds2[boundIdx].ToString()))
                    {
                        if (bounds1[boundIdx].Value<int>() != bounds2[boundIdx].Value<int>())
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }

                // 1.c)
                int zIndex1 = content1.Get<int>("z_index");
                int zIndex2 = content2.Get<int>("z_index");
                if (zIndex1 != zIndex2)
                    return false;
            }

            return true;
        }

        public void ClosePresentation()
        {
            if (_sceneManager == null)
                return;

            foreach (SceneManager scene in _sceneManager)
            {
                if (scene != null)
                {
                    scene.CloseAllDocuments();
                }
            }

            _sceneManager = null;
        }

        public void ShowTheSceneContents(bool forceReload = false)
        {
            // before continue I need to check if there are other "GotoScene" commands in the queue. If so I need to execute them before continue
            var cmd = RemoveNextGotoSceneCommandFromQueue();
            if (cmd != null)
            {
                LogTracer.Instance.Trace(string.Format(@"Execute next GotoScene command from the commands queue. SceneIndex: {0}  SubSceneIndex: {1}", cmd.SceneIndex, cmd.SubSceneIndex));
                
                // new command
                GotoScene(cmd.SceneIndex, cmd.SubSceneIndex);
                return;
            }

            SceneManager currentScene = GetSceneManagerByIndex(_currentSceneIndex);

            if (currentScene != null)
            {
                bool isSceneDifferent = true;
                if (_previousSceneIndex >= 0)
                {
                    var previousScene = GetSceneManagerByIndex(_previousSceneIndex);
                    if (IsTheSameScene(currentScene.GetSceneData(_currentSceneIndex), previousScene?.GetSceneData(_previousSceneIndex)))
                    {
                        isSceneDifferent = false;
                    }
                }

                if (isSceneDifferent || forceReload)
                {
                    currentScene.PrepareAllContentsToShow(_currentSceneIndex, (sender, success) =>
                    {
                        if (success)
                        {
                            OnGotoSceneComplete?.Invoke(_currentSceneIndex, _currentSubSceneIndex);
                        }
                    },
                    (errorMessage, contentIndex) =>
                    {
                        LogTracer.Instance.Trace(string.Format("Error during PrepareAllContentsToShow: sceneIndex = {0} - contentIndex = {1} - {2}", _currentSceneIndex, contentIndex, errorMessage), TraceEventType.Error);
                        OnSceneContentError?.Invoke(_currentSceneIndex, contentIndex, RealtimeCommunication.ERR_CODE_GOTO_SCENE_ERROR);
                    });
                    return;
                }
            }

            OnGotoSceneComplete?.Invoke(_currentSceneIndex, _currentSubSceneIndex);
        }

        private void LoadNextScene(int preloadSceneManagerIndex)
        {
            if (_sceneManager == null || MainContainer == null || preloadSceneManagerIndex < 0)
            {
                _isGotoSceneInProgress = false;
                return;
            }

            // check if the scene manager preload list is finished to call a callback function
            if (preloadSceneManagerIndex > _sceneManager.Length - 1 || preloadSceneManagerIndex > _sceneIndexPreloadSchemaList.Count - 1)
            {
                OnLoadScenesEnd?.Invoke();
                _isGotoSceneInProgress = false;

                // show the scene contents
                ShowTheSceneContents();

                _asyncPreloadSceneManagerIndex = 0; 
                _sceneIndexPreloadSchemaList.Clear();
                _sceneIndexPreloadSchemaList = null;
                return;
            }

            _asyncPreloadSceneManagerIndex = preloadSceneManagerIndex;

            // check if the scene is already loaded in memory
            if (_sceneManager[preloadSceneManagerIndex] != null)
            {
                // go to preload the next scene
                LoadNextScene(_asyncPreloadSceneManagerIndex + 1);
                return;
            }

            // it will have the scene color background
            _panScenesContainer.BackColor = BackgroundColor;
            


            int sceneIndex = _sceneIndexPreloadSchemaList[preloadSceneManagerIndex];
            List<int> sceneIndexes = new List<int>();
            // find all the scene indexes related to the same scene data 
            for (int sIdx = 0; sIdx < _scenes.Count; sIdx++)
            {
                if (sceneIndex == sIdx || IsTheSameScene(sceneIndex, sIdx))
                {
                    sceneIndexes.Add(sIdx);
                }
            }
            var sm = new SceneManager(sceneIndexes.ToArray(), _panScenesContainer, this);
            _sceneManager[preloadSceneManagerIndex] = sm;
            sm.LoadDocuments(sceneIndex, 
                            () =>
                            {
                                MainContainer.Invoke(new Action(() =>
                                {
                                    LoadNextScene(_asyncPreloadSceneManagerIndex + 1);
                                })); 
                            },
                            (message, contentIndex) =>
                            {
                                OnSceneContentError?.Invoke(sceneIndex, contentIndex, RealtimeCommunication.ERR_CODE_GOTO_SCENE_ERROR);
                            });
        }

        #region Scene event handlers
        private void OnSceneManagerError(object sender, string message)
        {
            SceneManager sm = (SceneManager)sender;
           
            OnError?.Invoke(message);
            ClearGotoSceneCommandQueue();
            _isGotoSceneInProgress = false;
        }

        #endregion


        #region Commands queue functions
        private void AddGotoSceneCommandToQueue(int sceneIndex, int subSceneIndex)
        {
            if (_gotoSceneCommandsQueue == null)
                _gotoSceneCommandsQueue = new List<GotoSceneCommandVO>();
            _gotoSceneCommandsQueue.Add(new GotoSceneCommandVO()
            {
                SceneIndex = sceneIndex,
                SubSceneIndex = subSceneIndex
            });
        }

        private GotoSceneCommandVO GetNextGotoSceneCommandFromQueue()
        {
            if (_gotoSceneCommandsQueue != null && _gotoSceneCommandsQueue.Count > 0)
                return _gotoSceneCommandsQueue[0];
            return null;
        }

        private GotoSceneCommandVO RemoveNextGotoSceneCommandFromQueue()
        {
            var cmd = GetNextGotoSceneCommandFromQueue();
            if (cmd != null)
                _gotoSceneCommandsQueue.RemoveAt(0);
            return cmd;
        }

        private void ClearGotoSceneCommandQueue()
        {
            _gotoSceneCommandsQueue?.Clear();
            _gotoSceneCommandsQueue = null;
        }
        #endregion

        #region Live content
        public async void ShowLiveContent(JObject result)
        {
            if (result == null)
                return;

            _liveResult = result;
            var parameters = result.Get<JObject>("params");
            var audio = result.Get<bool>("audio", true);
            _liveResourceId = result.Get<int>("resourceId");
            _liveResourceType = result.Get<string>("resourceType", string.Empty);
            _liveCurrentSceneIndex = result.Get<int>("sceneIndex", -1);
            if (_liveCurrentSceneIndex < 0)
                _liveCurrentSceneIndex = 0;
            _liveCurrentSubSceneIndex = result.Get<int>("subSceneIndex", -1);
            _liveContentNumberOfSubScene = result.Get<JArray>("numberOfSubScene");
            _livePages = null;

            if (_liveResourceType != DocumentsUtility.WEBSITE_RESOURCE_TYPE)
            {
                if (parameters != null)
                {
                    var specificPagesSheetsAttribute = (_liveResourceType == DocumentsUtility.EXCEL_RESOURCE_TYPE ? "sheets" : (_liveResourceType == DocumentsUtility.POWERPOINT_RESOURCE_TYPE ? "slides" : "pages"));
                    _livePages = parameters.Get<JArray>(specificPagesSheetsAttribute);
                }

                // get the file data strucure for the live resource to load
                List<FileData> fileDatas = new List<FileData>();
                if (!SceneManager.TransformResourceFileData(result, fileDatas, true))
                {
                    string errMsg = @"Live content data missing the resource id";
                    LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                    OnError?.Invoke(errMsg);
                    return;
                }

                if (fileDatas.Count == 0)
                {
                    string errMsg = @"Live content data is empty!";
                    LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                    OnError?.Invoke(errMsg);
                    return;
                }

                // start the remote file download...
                OnDownloadLiveContent?.Invoke(true);
                // I need this delay to avoid the flickering during loading message shows
                await Task.Delay(500);

                if (_liveResult == null)
                {
                    // the live content show was interrupted by the user
                    OnDownloadLiveContent?.Invoke(false);
                    return;
                }

                var fileDatasArr = fileDatas.ToArray();
                DownloadRemoteFiles(fileDatasArr, true, () =>
                {
                    if (_liveResult == null)
                    {
                        // the live content show was interrupted by the user
                        OnDownloadLiveContent?.Invoke(false);
                        return;
                    }

                    foreach (var fileData in fileDatasArr)
                    {
                        // check if the local file exists
                        if (!File.Exists(fileData.LocalFile))
                        {
                            var errMsg = string.Format(@"Live content local file not found: {0}", fileData.LocalFile);
                            LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                            OnError?.Invoke(errMsg);
                            return;
                        }
                    }

                    OnDownloadLiveContent?.Invoke(false);

                    ContinueShowLiveContent(fileDatas.First().LocalFile, parameters, audio); 
                },
                (error) =>
                {
                    var errMsg = string.Format("Error during the live content file download process: {0}!", error);
                    LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                    OnError?.Invoke(errMsg);

                    RemoveLiveContentControls();
                });
            }
            else
            {
                ContinueShowLiveContent(result.Get<string>("resourceFile"), parameters, audio);
            }
        }

        private async void ContinueShowLiveContent(string fileName, JObject contentParameters, bool audio)
        {
            if (_liveResult == null)
            {
                // the live content show was interrupted by the user
                return;
            }

            OnLoadContentLiveContent?.Invoke(true);
            // I need this delay to avoid the flickering during loading message shows
            await Task.Delay(500);

            if (_liveResult == null)
            {
                // the live content show was interrupted by the user
                OnLoadContentLiveContent?.Invoke(false);
                return;
            }

            // create the control object element
            RemoveLiveContentControls();

            _liveDocument = new ControlObjectElement();

            _liveDocument.OnSceneContentError += async () =>
            {
                if (_liveResult == null)
                {
                    // the live content show was interrupted by the user
                    OnLoadContentLiveContent?.Invoke(false);
                    return;
                }

                var errMsg = string.Format(@"Live content error during scene management");
                LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                OnError?.Invoke(errMsg);

                // reload the live content...
                await Task.Delay(2000);
                ContinueShowLiveContent(fileName, contentParameters, audio);
            };

            // create the object container
            _panLiveContentContainer.BackColor = BackgroundColor;

            var liveContainer = new Panel();
            liveContainer.BackColor = _panLiveContentContainer.BackColor;
            liveContainer.Left = 0;
            liveContainer.Top = 0;
            liveContainer.Width = _panLiveContentContainer.Width;
            liveContainer.Height = _panLiveContentContainer.Height;

            _liveDocument.CreateDocumentObjectFromFile(fileName,
                                                       _panLiveContentContainer,
                                                       liveContainer,
                                                       () =>
                                                       {
                                                           MainContainer.Invoke(new Action(() =>
                                                           {
                                                               if (_liveResult == null)
                                                               {
                                                                   // the live content show was interrupted by the user
                                                                   OnLoadContentLiveContent?.Invoke(false);
                                                                   return;
                                                               }

                                                               // and it will be disabled to avoid "traditional" user interaction (the content will be controlled only by specific commands) for all the content types but website
                                                               liveContainer.Enabled = DocumentsUtility.IsWebsite(_liveDocument.Type) && (contentParameters == null || (contentParameters != null && !contentParameters.Get<bool>("displayMode", false)));

                                                                // now show the loaded content
                                                                if (_liveDocument.Container.Parent == null)
                                                                    _panLiveContentContainer.Controls.Add(_liveDocument.Container);

                                                                _liveDocument.ShowObject(_panLiveContentContainer.Bounds, audio, contentParameters, async   () =>
                                                                {
                                                                    if (_liveResult == null)
                                                                    {
                                                                        // the live content show was interrupted by the user
                                                                        OnLoadContentLiveContent?.Invoke(false);
                                                                        return;
                                                                    }

                                                                    OnLoadContentLiveContent?.Invoke(false);
                                                                    await Task.Delay(600);

                                                                    if (_liveResult == null)
                                                                    {
                                                                        // the live content show was interrupted by the user
                                                                        return;
                                                                    }

                                                                    // stop all the presentation contents
                                                                    SceneManager currentScene = GetSceneManagerByIndex(_currentSceneIndex);
                                                                    currentScene?.StopsAllDocuments();

                                                                    _liveDocument.Container.Visible = true;
                                                                    _panLiveContentContainer.Visible = true;
                                                                    _panLiveContentContainer.BringToFront();

                                                                    RefreshLiveContentSceneIndex();
                                                                });
                                                           }));
                                                       },
                                                       async () =>
                                                       {
                                                           if (_liveResult == null)
                                                           {
                                                               // the live content show was interrupted by the user
                                                               OnLoadContentLiveContent?.Invoke(false);
                                                               return;
                                                           }

                                                           var errMsg = string.Format(@"Live content error during scene management");
                                                            LogTracer.Instance.Trace(errMsg, TraceEventType.Error);
                                                            OnError?.Invoke(errMsg);

                                                            // reload the live content...
                                                            await Task.Delay(2000);
                                                            ContinueShowLiveContent(fileName, contentParameters, audio);
                                                       });
        }

        private void RemoveLiveContentControls()
        {
            for (int i = _panLiveContentContainer.Controls.Count - 1; i >= 0; i--)
                _panLiveContentContainer.Controls.RemoveAt(i);
            _panLiveContentContainer.Visible = false;

            _liveDocument?.RemoveDocumentControl();
            
            _liveDocument = null;
        }

        public void UnloadLiveContent(bool displayMode = false)
        {
            OnDownloadLiveContent?.Invoke(false);
            OnLoadContentLiveContent?.Invoke(false);

            if (_liveDocument == null)
                return;

            _panLiveContentContainer.Visible = false;

            _liveDocument.RemoveDocumentControl();
            _liveDocument = null;

            RemoveLiveContentControls();

            _livePages = null;
            _liveResult = null;
            _liveCurrentSceneIndex = -1;
            _liveCurrentSubSceneIndex = -1;
            _liveContentNumberOfSubScene = null;
            _liveResourceId = 0;
            _liveResourceType = null;

            if (!displayMode)
            {
                // restart the scene objects...
                SceneManager currentScene = GetSceneManagerByIndex(_currentSceneIndex);
                currentScene?.StartsAllDocuments();
            }
        }

        private void RefreshLiveContentSceneIndex()
        {
            if (_liveDocument == null)
                return;

            if (DocumentsUtility.IsVideo(_liveDocument.Type))
                _liveDocument.PlayVideo();
            else if (DocumentsUtility.IsWebsite(_liveDocument.Type))
                _liveDocument.NavigateWeb();
            else if (_liveResourceType == DocumentsUtility.PDF_RESOURCE_TYPE ||
                     _liveResourceType == DocumentsUtility.WORD_RESOURCE_TYPE ||
                     _liveResourceType == DocumentsUtility.EXCEL_RESOURCE_TYPE)
            {
                if (_livePages != null && _liveCurrentSceneIndex >= 0 && _liveCurrentSceneIndex < _livePages.Count)
                {
                    var pageFileName = Path.Combine(_presentationPath, string.Format("{0}-{1}-{2}.png", _liveResourceId, _livePages[_liveCurrentSceneIndex], 1));

                    // change the png showed in the PictureBox
                    _liveDocument.ChangeImage(pageFileName);
                }
            }
            else if (DocumentsUtility.IsPowerPoint(_liveDocument.Type))
            {
                if (_liveCurrentSceneIndex < 0)
                    _liveCurrentSceneIndex = 0;

                if (_livePages != null && _liveCurrentSceneIndex >= 0 && _liveCurrentSceneIndex < _livePages.Count)
                {
                    var slide = _livePages[_liveCurrentSceneIndex].Value<int>();
                    _liveDocument.SetPowerPointSubSlides(slide, _liveContentNumberOfSubScene[_liveCurrentSceneIndex].Value<int>());
                    _liveDocument.SendPowerPointCommand(new PowerPointObject.PowerPointCommand
                    {
                        Type = PowerPointObject.PowerPointCommandType.GotoSlide,
                        Slide = slide - 1,
                        SubSlide = _liveCurrentSubSceneIndex,
                        CameFromDisconnect = false,
                        Completed = () =>
                        {
                            Debug.WriteLine("**** COMMAND SENDED ||| LIVE CONTENT GOTO SCENE: " + _liveCurrentSceneIndex + "  SUB: " + _liveCurrentSubSceneIndex);
                        },
                        Error = () =>
                        {
                            // reload the live content...
                            ShowLiveContent(_liveResult);
                        }
                    });
                }
            }
        }

        public void LiveContentGotoScene(int sceneIndex, int subSceneIndex)
        {
            if (_liveDocument == null || sceneIndex < 0)
                return;

            _liveCurrentSceneIndex = sceneIndex;
            _liveCurrentSubSceneIndex = subSceneIndex;
            RefreshLiveContentSceneIndex();
        }
        #endregion        
    }
}
