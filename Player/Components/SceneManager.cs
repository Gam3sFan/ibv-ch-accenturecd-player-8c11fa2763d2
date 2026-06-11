using ContentDistributionPlayer.Extensions;
using ContentDistributionPlayer.Utilities;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ContentDistributionPlayer.Components
{
    public delegate void SM_ErrorEvent(object sender, string error);
    public delegate void SM_LoadDocumentsCompleteEvent(object sender);
    public delegate void SM_ShowContentsCompleteEvent(object sender, bool success);

    class SceneManager
    {
        public Panel SceneContainer { get; }
        public int[] SceneIndexes { get; set; } = null;
        private PresentationManager _presentationManager = null;

        public ControlObjectElement[] ControlObjectElements { get; private set; }
        private SceneContentFile[] _sceneContentFiles = null;

       
        public SceneManager(int[] sceneIndexes, Panel sceneContianer, PresentationManager presentationManager)
        {
            SceneIndexes = sceneIndexes;
            SceneContainer = sceneContianer;
            _presentationManager = presentationManager;
        }

        public static (FileData[], string) GetDocumentFilesFromSceneData(JArray scenesData, int sceneId = 0)
        {
            LogTracer.Instance.Trace(string.Format(@"Get the document files from scenes data for scene id {0}", sceneId));

            // if sceneId == 0 returns all the file paths for the entire scenes
            if (scenesData == null || scenesData.Count == 0)
            {
                return (null, @"Unable to load the scene content because the scene data are empty!");
            }

            int sceneIdx = 0;
            List<FileData> fileDatas = new List<FileData>();

            LogTracer.Instance.Trace("Loops through scene data");

            foreach (JObject sceneData in scenesData)
            {
                LogTracer.Instance.Trace(string.Format("Scene data index {0}", sceneIdx));

                if (sceneData != null)
                {
                    if (sceneData["id"] == null)
                    {
                        return (null, string.Format(@"Scene id missing in scene index {0}!", sceneIdx));
                    }

                    var currentSceneId = sceneData.Get<int>("id");
                    if (sceneId == 0 || currentSceneId == sceneId)
                    {
                        LogTracer.Instance.Trace(string.Format("This scene id {0} will be used", sceneId));

                        var contents = sceneData.Get<JArray>("contents");
                        if (contents != null)
                        {
                            // loop for each files and search only the document file that should be downloaded locally
                            foreach (JObject sceneDoc in contents)
                            {
                                if (sceneDoc != null)
                                {
                                    if (!TransformResourceFileData(sceneDoc, fileDatas))
                                    {
                                        return (null, string.Format(@"Scene {0} content file missing the resource id", sceneId));
                                    }
                                }
                            }
                        }
                    }
                }

                sceneIdx++;
            }

            return (fileDatas.ToArray(), null);
        }

        public static bool TransformResourceFileData(JObject resourceContent, List<FileData> fileDatas, bool isLiveContent = false)
        {
            string fileName = resourceContent.Get<string>(isLiveContent ? "resourceFile" : "file");
            if (fileName != null)
            {
                int version = resourceContent.Get("version", 1);
                int resourceId = resourceContent.Get(isLiveContent ? "resourceId" : "resource_id", 0);
                string resourceType = resourceContent.Get<string>(isLiveContent ? "resourceType" : "resource_type");
                if (resourceId <= 0)
                {
                    return false;
                }

                if (isLiveContent && (resourceType == DocumentsUtility.EXCEL_RESOURCE_TYPE ||
                    resourceType == DocumentsUtility.PDF_RESOURCE_TYPE ||
                    resourceType == DocumentsUtility.WORD_RESOURCE_TYPE))
                {
                    LogTracer.Instance.Trace(string.Format("Processing each pages of the file '{0}' type {1}", fileName, resourceType));

                    // I must download each png file for each pages
                    var parameters = resourceContent.Get<JObject>("params");
                    var resourceBaseUrl = resourceContent.Get<string>("resourceBaseUrl");
                    if (string.IsNullOrEmpty(resourceBaseUrl))
                    {
                        LogTracer.Instance.Trace(string.Format("The resource base url is missing in the json data!"));
                        return false;
                    }

                    if (parameters != null)
                    {
                        var specificPagesSheetsAttribute = (resourceType == DocumentsUtility.EXCEL_RESOURCE_TYPE ? "sheets" : "pages");
                        var pages = parameters.Get<JArray>(specificPagesSheetsAttribute);
                        foreach (var p in pages)
                        {
                            var pageFileName = Path.Combine(Path.Combine(resourceBaseUrl, specificPagesSheetsAttribute), string.Format("{0}.png", p));

                            if (!fileDatas.Exists(x => x.FileName == pageFileName))
                            {
                                LogTracer.Instance.Trace(string.Format("Adding the file {0} for local download", pageFileName));

                                // it is a file that I need to copy locally
                                fileDatas.Add(new FileData(resourceId, pageFileName, 1, DocumentsUtility.IMAGE_RESOURCE_TYPE));
                            }
                        }
                    }
                    return true;
                }
                
                if (!fileDatas.Exists(x => x.FileName == fileName))
                {
                    LogTracer.Instance.Trace(string.Format("Processing the file '{0}' with version {1}", fileName, version));

                    if (DocumentsUtility.IsDocumentFile(fileName))
                    {
                        LogTracer.Instance.Trace("The file is a valid document file");

                        // it is a file that I need to copy locally
                        fileDatas.Add(new FileData(resourceId, fileName, version, resourceType));
                    }
                }
            }

            return true;
        }

        public JObject GetSceneData(int specificSceneIndex)
        {
            if (_presentationManager == null)
                return null;

            var sceneData = _presentationManager.GetSceneDataByIndex(specificSceneIndex);
            return sceneData;
        }

        public void LoadDocuments(int sceneIndex, Action complete, Action<string, int> error)
        {
            var sceneData = GetSceneData(sceneIndex);
            if (sceneData == null)
            {
                var errorMsg = @"Unable to load the scene content because the scene data are empty!";
                LogTracer.Instance.Trace(errorMsg, TraceEventType.Error);
                error?.Invoke(errorMsg, -1);
                return;
            }

            int jsonSceneIndex = sceneData.Get<int>("sceneIndex");

            var contents = sceneData.Get<JArray>("contents");
            if (contents == null || contents.Count == 0)
            {
                var errorMsg = string.Format(@"The scene {0} has the content data empty!", sceneData.Get<int>("id"));
                /*
                LogTracer.Instance.Trace(errorMsg, TraceEventType.Error);
                error?.Invoke(errorMsg, -1);
                */
                LogTracer.Instance.Trace(errorMsg);
                complete?.Invoke();
                return;
            }

            List<SceneContentFile> sceneContentFiles = new List<SceneContentFile>();
            for (int idxContent = 0; idxContent < contents.Count; idxContent++)
            {
                JObject sceneDoc = contents[idxContent] as JObject;            
                if (sceneDoc != null)
                {
                    string localFile = sceneDoc.Get<string>("localFile");
                    if (!string.IsNullOrEmpty(localFile))
                    {
                        sceneContentFiles.Add(new SceneContentFile
                        {
                            FileName = localFile,
                            ContentIndex = idxContent,
                            ContentData = sceneDoc,
                            SceneIndex = jsonSceneIndex
                        });
                    }
                    else
                    {
                        string file = sceneDoc.Get<string>("file");
                        if (!string.IsNullOrEmpty(file))
                        {
                            sceneContentFiles.Add(new SceneContentFile
                            {
                                FileName = file,
                                ContentIndex = idxContent,
                                ContentData = sceneDoc,
                                SceneIndex = jsonSceneIndex
                            });
                        }
                    }
                }
            }

            if (sceneContentFiles.Count == 0)
            {
                var errorMsg = string.Format(@"The scene {0} has no valid files in its content data!", sceneData.Get<int>("id"));
                LogTracer.Instance.Trace(errorMsg, TraceEventType.Error);
                error?.Invoke(errorMsg, -1);
                return;
            }

            // start loading all the scene documents
            _sceneContentFiles = sceneContentFiles.ToArray();
            LoadDocuments(complete, error);
        }

        private void LoadDocuments(Action complete, Action<string, int> error)
        {
            if (_sceneContentFiles == null)
                return;

            CloseAllDocuments();

            // check if the local file exists
            foreach (SceneContentFile sceneContentFile in _sceneContentFiles)
            {
                if (!string.IsNullOrEmpty(sceneContentFile.FileName) &&
                    sceneContentFile.FileName.ToLower().IndexOf(@"http://") == -1 &&
                    sceneContentFile.FileName.ToLower().IndexOf(@"https://") == -1 &&
                    sceneContentFile.FileName.ToLower().IndexOf(@"file://") == -1)
                {
                    if (!File.Exists(sceneContentFile.FileName))
                    {
                        var errorMsg = string.Format(@"File not found: {0}", sceneContentFile.FileName);
                        LogTracer.Instance.Trace(errorMsg, TraceEventType.Error);
                        error?.Invoke(errorMsg, -1);
                        return;
                    }
                }
            }

            // crete the specific controls to manage each files present in this scene content
            ControlObjectElements = new ControlObjectElement[_sceneContentFiles.Length];
            OpenNextDocument(0, complete, error);
        }

        private Panel CreateObjectContainer(DocumentsUtility.DocumentTypes type, JObject contentData)
        {
            Panel p = new Panel();

            p.BackColor = SceneContainer.BackColor;
           
            // and it will be disabled to avoid "traditional" user interaction (the content will be controlled only by specific commands) for all the content types but website
            p.Enabled = DocumentsUtility.IsWebsite(type);

            var bounds = GetSceneContentBounds(contentData);
            if (bounds != Rectangle.Empty)
            {
                p.Width = bounds.Width;
                p.Height = bounds.Height;
                p.Left = bounds.Left;
                p.Top = bounds.Top;
            }
           
            return p;
        }

        private void OpenNextDocument(int documentIndex, Action complete, Action<string, int> error)
        {
            if (ControlObjectElements == null || _sceneContentFiles == null || documentIndex < 0)
                return;

            // check if the file name list is finished to call a callback function
            if (documentIndex > ControlObjectElements.Length - 1 || documentIndex > _sceneContentFiles.Length - 1)
            {
                complete?.Invoke();
                _sceneContentFiles = null;
                return;
            }

            SceneContentFile sceneContentFile = _sceneContentFiles[documentIndex];
            if (string.IsNullOrEmpty(sceneContentFile.FileName))
            {
                OpenNextDocument(documentIndex + 1, complete, error);
                return;
            }

            // check if there is the same file already loaded in the resources list
            int resourceId = sceneContentFile.ContentData.Get<int>("resource_id");
            var resourceInScene = _presentationManager.GetUniqueResourceInScenes(resourceId, SceneIndexes);
            ControlObjectElement document;
            if (resourceInScene != null && resourceInScene.ControlObjectElement != null)
            {
                // reuse the same loaded document for more than one scene
                document = resourceInScene.ControlObjectElement;

                // update the bounding informations
                Rectangle bounds = GetSceneContentBounds(sceneContentFile.ContentData);
                document.SetBounds(bounds);

                foreach (var si in SceneIndexes)
                {
                    resourceInScene.SceneIndexes.Add(new PresentationManager.SceneContentPosition
                    {
                        SceneIndex = si,
                        ContentIndex = sceneContentFile.ContentIndex
                    });
                }

                ControlObjectElements[documentIndex] = document;               

                // open the next document
                OpenNextDocument(documentIndex + 1, complete, error);
            }
            else
            {
                document = new ControlObjectElement();

                document.OnSceneContentError += () =>
                {
                    OnSceneContentError(error, documentIndex);
                };

                // create the object container
                document.CreateDocumentObjectFromFile(sceneContentFile.FileName,
                                                      SceneContainer,
                                                      CreateObjectContainer(DocumentsUtility.GetDocumentTypeByFileName(sceneContentFile.FileName), sceneContentFile.ContentData),
                                                      () =>
                {
                    SceneContainer.BeginInvoke(new MethodInvoker(delegate
                    {
                        if (DocumentsUtility.IsOfficeDocument(document.Type) ||
                            DocumentsUtility.IsVideo(document.Type) ||
                            DocumentsUtility.IsWebsite(document.Type) ||
                            DocumentsUtility.IsImage(document.Type))
                        {
                            //SceneContainer.Controls.Add(document.Container);
                            //document.Container.Visible = false;
                        }
                        else
                        {
                            // remove the object container created before
                            SceneContainer.Controls.Remove(document.Container);
                        }

                        if (ControlObjectElements == null)
                            return;

                        ControlObjectElements[documentIndex] = document;

                        // add the resource in the unique list
                        _presentationManager.AddUniqueResourceInScenes(resourceId, document, SceneIndexes, sceneContentFile.ContentIndex);

                        // open the next document
                        OpenNextDocument(documentIndex + 1, complete, error);
                    }));
                },
                () =>
                {
                    OnSceneContentError(error, documentIndex);
                });
            }
        }

        private void OnSceneContentError(Action<string, int> error, int documentIndex)
        {
            SceneContainer.BeginInvoke(new MethodInvoker(delegate
            {
                _presentationManager.Restart();
            }));
        }

        public void PrepareAllContentsToShow(int sceneIndex, SM_ShowContentsCompleteEvent completed, Action<string, int> error)
        {
            var sceneData = GetSceneData(sceneIndex);
            if (sceneData == null)
            {
                completed.Invoke(this, false);
                return;
            }

            var contents = sceneData.Get<JArray>("contents");
            ShowNextContent(0, contents, completed, error);
        }

        private void ShowNextContent(int index, JArray contents, SM_ShowContentsCompleteEvent completed, Action<string, int> error)
        {
            if (contents == null || ControlObjectElements == null || index < 0)
            {
                // the scene is completly empty: the cover image will be shown
                completed.Invoke(this, true);
                return;
            }

            if (index >= contents.Count)
            {
                completed.Invoke(this, true);
                return;
            }

            if (index >= ControlObjectElements.Length)
            {
                LogTracer.Instance.Trace(string.Format("Control object element index {0} out of bound {1}", index, ControlObjectElements.Length));
                completed.Invoke(this, false);
                return;
            }

            if (ControlObjectElements[index] == null)
            {
                LogTracer.Instance.Trace(string.Format("Control object element in null on index {0}", index));
                OnSceneContentError(error, index);
                return;
            }

            JObject content = contents[index] as JObject;
            if (content == null)
            {
                completed.Invoke(this, false);
                return;
            }

            var bounds = GetSceneContentBounds(content);
            var parameters = content.Get<JObject>("params");
            var audio = content.Get<bool>("audio", true);
            var document = ControlObjectElements[index];
            
            if (document.Container.Parent == null)
                SceneContainer.Controls.Add(document.Container);
            
            document.ShowObject(bounds, audio, parameters, () =>
            {
                ShowNextContent(index + 1, contents, completed, error);
            });
        }

        public Rectangle GetSceneContentBounds(JObject content)
        {
            if (content == null)
                return Rectangle.Empty;

            var bounds = new Rectangle(-1, -1, SceneContainer.Width + 2, SceneContainer.Height + 2);
            JArray arrBounds = content.Get<JArray>("bounds");
            if (arrBounds != null)
            {
                var normalizedBounds = ObjectContentBoundsNormalize(arrBounds);
                bounds = new Rectangle(normalizedBounds[0], normalizedBounds[1], normalizedBounds[2], normalizedBounds[3]);
            }
            return bounds;
        }


        private int[] ObjectContentBoundsNormalize(JArray bounds)
        {
            // if one of the size measures (width or height) is -1 it means that fill the relative to the parent container dimension (ie: w = -1 means that the width is equals to the parent width)
            // if one of the position measures (x or y) is -1 it means that the position is right or bottom to the parent container (ie: x = -1 means that the content location is aligned to the right of the container)
            // if one measure is float it means a percentage value realtive to the parent container dimension (ie: w = 0.5 means that the width is equals to the half parent width)
            int[] retBounds = new int[4] { 0, 0, 0, 0 };

            if (bounds != null && bounds.Count == 4)
            {
                for (int i = 0; i < bounds.Count; i++)
                {
                    if (bounds[i] != null)
                    {
                        if (NumberUtility.IsFloat(bounds[i].ToString()))
                        {
                            // calculate the specific percentage
                            if (i == 0 || i == 2) // it is the width
                            {
                                retBounds[i] = (int)(SceneContainer.Width * bounds[i].Value<float>());
                            }
                            else if (i == 1 || i == 3) // it is the height
                            {
                                retBounds[i] = (int)(SceneContainer.Height * bounds[i].Value<float>());
                            }
                        }
                        else if (NumberUtility.IsInt(bounds[i].ToString()))
                        {
                            // check if its equal to -1
                            if (bounds[i].Value<int>() == -1)
                            {
                                if (i == 2) // it is the width
                                    retBounds[i] = SceneContainer.Width;
                                else if (i == 3) // it is the height
                                    retBounds[i] = SceneContainer.Height;
                                else
                                    retBounds[i] = bounds[i].Value<int>();
                            }
                            else
                            {
                                retBounds[i] = bounds[i].Value<int>();
                            }
                        }
                    }
                }

                // now check if there are the x or y equal to -1 so need to adjust the positione relative to the right or bottom of the container
                if (retBounds[0] == -1)
                    retBounds[0] = SceneContainer.Width - retBounds[2];
                if (retBounds[1] == -1)
                    retBounds[1] = SceneContainer.Height - retBounds[3];
            }
            else
            {
                LogTracer.Instance.Trace("Content bounds normalization failed: bounds array is empty or less than 4 items", TraceEventType.Error);
            }

            return retBounds;
        }

        #region Closing functions

        private void StopStartSingleDocument(int index, bool needToStop, Action completed)
        {
            if (ControlObjectElements == null || index < 0 || index >= ControlObjectElements.Length)
            {
                completed?.Invoke();
                return;
            }

            var coe = ControlObjectElements[index];
            coe.StopDocument(() =>
            {
                SceneContainer.BeginInvoke(new MethodInvoker(delegate
                {
                    StopStartSingleDocument(index + 1, needToStop, completed);
                }));
            });
        }

        private void StopSingleDocumentNotInScene(int index, SceneManager scene, Action completed)
        {
            if (ControlObjectElements == null || index < 0 || index >= ControlObjectElements.Length)
            {
                completed?.Invoke();
                return;
            }

            var coe = ControlObjectElements[index];
            bool found = false;
            foreach (var newSceneCoe in scene.ControlObjectElements)
            {
                if (newSceneCoe.Equals(coe))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                coe.StopDocument(() =>
                {
                    SceneContainer.BeginInvoke(new MethodInvoker(delegate
                    {
                        // remove the UI control
                        SceneContainer.Controls.Remove(coe.Container);

                        StopSingleDocumentNotInScene(index + 1, scene, completed);
                    }));
                });
            }
            else
            {
                StopSingleDocumentNotInScene(index + 1, scene, completed);
            }
        }

        public void StopsAllDocuments(Action completed = null)
        {
           StopStartSingleDocument(0, true, completed);            
        }

        public void StartsAllDocuments(Action completed = null)
        {
            StopStartSingleDocument(0, false, completed);
        }

        public void CloseAllDocuments(bool quitDocumentApp = false)
        {
            LogTracer.Instance.Trace("Close the previous opened documents");

            if (ControlObjectElements == null)
                return;

            foreach (ControlObjectElement document in ControlObjectElements)
            {
                try
                {
                    if (document != null)
                        document.RemoveDocumentControl(quitDocumentApp);
                }
                catch(Exception e)
                {
                    LogTracer.Instance.Trace(string.Format("Error removing document control {0}", e.Message), TraceEventType.Error);
                }
            }

            ControlObjectElements = null;
        }

        public void DestroyContentsNotInScene(Action completed = null)
        {
            List<ControlObjectElement> _coesToStop = null;

            for (int i = SceneContainer.Controls.Count - 1; i >= 0; i--)
            {
                var c = SceneContainer.Controls[i];

                // check if the control will be shown in the current scene
                bool found = false;
                if (ControlObjectElements != null && ControlObjectElements.Length > 0)
                {
                    for (var j = 0; j < ControlObjectElements.Length; j++)
                    {
                        var rightC = ControlObjectElements[j].Container;
                        if (c.Equals(rightC))
                        {
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    // remove the component from UI
                    SceneContainer.Controls.Remove(c);

                    // search the associated Control Object Element to stop or pause the control
                    var coeToStop = _presentationManager.FindControlObjectElementByControlContainer(c);
                    if (coeToStop == null)
                    {
                        LogTracer.Instance.Trace(string.Format("Unable to find a control object element to stop related to the UI container control {0}", c.Name), TraceEventType.Error);
                    }
                    else
                    {
                        if (_coesToStop == null)
                            _coesToStop = new List<ControlObjectElement>();
                        _coesToStop.Add(coeToStop);
                    }
                }
            }

            if (_coesToStop != null && _coesToStop.Count > 0)
            {
                StopControlObjectElement(_coesToStop, 0, () =>
                {
                    completed?.Invoke();
                });
            }
            else
            {
                completed?.Invoke();
            }
        }

        private void StopControlObjectElement(List<ControlObjectElement> _coesToStop, int index, Action completed)
        {
            if (_coesToStop == null || index < 0 || index >= _coesToStop.Count)
            {
                completed?.Invoke();
                return;
            }
            var coe = _coesToStop[index];

            coe.StopDocument(() =>
            {
                SceneContainer.BeginInvoke(new MethodInvoker(delegate
                {
                    StopControlObjectElement(_coesToStop, index + 1, completed);
                }));
            });
        }

        public void DestroyAllContents(Action completed = null)
        {
            StopsAllDocuments(() =>
            {
                // remove all the scene controls from the UI
                // (iterazione all'indietro per indice: rimuovere dentro un foreach
                //  modifica la collection durante l'enumerazione e salta dei controlli)
                if (SceneContainer != null)
                {
                    for (int i = SceneContainer.Controls.Count - 1; i >= 0; i--)
                    {
                        SceneContainer.Controls.RemoveAt(i);
                    }
                }

                completed?.Invoke();
            });
        }
        #endregion
    }
}
