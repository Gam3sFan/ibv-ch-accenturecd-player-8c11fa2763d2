using ContentDistributionPlayer.Extensions;
using ContentDistributionPlayer.Utilities;
using MQTTnet;
using MQTTnet.Client;

using Newtonsoft.Json.Linq;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ContentDistributionPlayer.Components
{
    public delegate void RTC_EventError(string message);
    public delegate void RTC_EventGenericNoParams();
    public delegate Task RTC_EventGenericTaskNoParams();
    public delegate void RTC_EventGenericResult(JObject result);

    class RealtimeCommunication
    {
        // Error code const
        public const int ERR_CODE_GOTO_SCENE_ERROR = 1000;
        public const int ERR_CODE_PRESENTATION_CONTENT_ERROR = 1001;
        public const int ERR_CODE_PPT_ERROR = 1100;
        public const int ERR_CODE_DISPLAY_MODE_ERROR = 2000;


        // topics
        private readonly string INIT_TOPIC;
        private readonly string UNLOAD_TOPIC;
        private readonly string GOTO_TOPIC;
        private readonly string CLIENT_TOPIC;
        private readonly string CLIENTUID_TOPIC;

        // live content topics
        private readonly string LIVEINIT_TOPIC;
        private readonly string LIVEUNLOAD_TOPIC;
        private readonly string LIVEGOTO_TOPIC;

        // Display mode start/stop (run external applications)
        private readonly string CLIENT_DISPLAYMODE_START_TOPIC;
        private readonly string CLIENT_DISPLAYMODE_STOP_TOPIC;
        private readonly string DISPLAYMODE_STOP_TOPIC;



        private IMqttClient _client;
        private MqttClientOptions _connectionOptions;

        private string _host;
        private int _port;
        private string _protocol;
        private int _room;
        private int _monitor;
        private string _clientId;
        private string _clientUid;
        private string _topicPrefix;
        private int _retryReconnectionInSeconds;
        private int _screenWidth;
        private int _screenHeight;
        private bool _forceClose = false;
        public bool IsConnected
        {
            get { return _client != null && _client.IsConnected; }
        }
        
        public RTC_EventError OnConnectionError;
        public RTC_EventGenericNoParams OnClientNotUpdatedError;
        public RTC_EventGenericResult OnConnectionSuccess;
        public RTC_EventError OnError;
        public RTC_EventGenericResult OnInitPresentation;
        public RTC_EventGenericNoParams OnUnloadPresentation;
        public RTC_EventGenericResult OnGotoScene;
        public RTC_EventGenericNoParams OnDisconnected;

        public RTC_EventGenericResult OnInitLiveContent;
        public RTC_EventGenericNoParams OnUnloadLiveContent;
        public RTC_EventGenericResult OnGotoSceneLiveContent;

        public RTC_EventGenericResult OnClientDisplayModeStart;
        public RTC_EventGenericTaskNoParams OnClientDisplayModeStop;

        public RealtimeCommunication(string host, int port, string protocol, int room, int monitor)
        {
            _host = host;
            _port = port;
            _protocol = protocol;
            _room = room;
            _monitor = monitor;

            _clientId = string.Format("R{0}_M{1}", _room, _monitor);
            _clientUid = string.Format("{0}_{1}|{2}", _clientId, DateTime.Now.ToString("yyyyMMddHHmmss"), RandomString());
            _topicPrefix = string.Format("rooms/{0}", _room);

            INIT_TOPIC = string.Format("{0}/init", _topicPrefix);
            UNLOAD_TOPIC = string.Format("{0}/unload", _topicPrefix);
            GOTO_TOPIC = string.Format("{0}/goto", _topicPrefix);
            CLIENT_TOPIC = string.Format("{0}/client/{1}", _topicPrefix, _clientId);
            CLIENTUID_TOPIC = string.Format("{0}/client/{1}", _topicPrefix, _clientUid);

            LIVEINIT_TOPIC = string.Format("{0}/live-init", _topicPrefix);
            LIVEUNLOAD_TOPIC = string.Format("{0}/live-unload", _topicPrefix);
            LIVEGOTO_TOPIC = string.Format("{0}/live-goto", _topicPrefix);

            CLIENT_DISPLAYMODE_START_TOPIC = string.Format("{0}/display-mode-start/{1}", _topicPrefix, _monitor);
            CLIENT_DISPLAYMODE_STOP_TOPIC = string.Format("{0}/display-mode-stop/{1}", _topicPrefix, _monitor);
            DISPLAYMODE_STOP_TOPIC = string.Format("{0}/display-mode-stop", _topicPrefix);
        }

        private static readonly Random _rnd = new Random();
        private static string RandomString(int length = 45, string chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            var sb = new StringBuilder(length);
            lock (_rnd) // Random non è thread-safe
            {
                for (int i = 0; i < length; i++)
                    sb.Append(chars[_rnd.Next(0, chars.Length)]); // max è esclusivo: ora include anche l'ultimo carattere
            }
            return sb.ToString();
        }

        public async void Connect(int retryReconnectionInSecods, int screenWidth, int screenHeight)
        {
            _retryReconnectionInSeconds = retryReconnectionInSecods;
            _screenWidth = screenWidth;
            _screenHeight = screenHeight;

            _forceClose = false;

            LogTracer.Instance.Trace("Connecting to the NodeJS server (" + _protocol + "://" + _host + ":" + _port + ") - room: " + _room + "  monitor: " + _monitor);

            try
            {
                string brokerURL = string.Format("{2}://{0}:{1}", _host, _port, _protocol);

                var factory = new MqttFactory();
                if (_client == null)
                {
                    _client = factory.CreateMqttClient();

                    _client.DisconnectedAsync += async e =>
                    {
                        if (_forceClose)
                            return;

                        LogTracer.Instance.Trace("Disconnected from NodeJS server");

                        // now communicate the state to the main form
                        OnDisconnected?.Invoke();

                        if (_retryReconnectionInSeconds > 0)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(_retryReconnectionInSeconds));

                            LogTracer.Instance.Trace("Retry to connect to the NodeJS server");
                            Connect(retryReconnectionInSecods, screenWidth, screenHeight);
                        }
                    };

                    _client.ConnectedAsync += async e =>
                    {
                        LogTracer.Instance.Trace("Connected to NodeJS server");

                        // Subscribe to a topic
                        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(INIT_TOPIC).Build());
                        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(UNLOAD_TOPIC).Build());
                        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(GOTO_TOPIC).Build());
                        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(CLIENT_TOPIC).Build());

                        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(LIVEINIT_TOPIC).Build());
                        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(LIVEUNLOAD_TOPIC).Build());
                        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(LIVEGOTO_TOPIC).Build());

                        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(CLIENT_DISPLAYMODE_START_TOPIC).Build());
                        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(CLIENT_DISPLAYMODE_STOP_TOPIC).Build());
                        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(DISPLAYMODE_STOP_TOPIC).Build());

                        // the client communicate to the server its app version
                        await PublishMessage(CLIENTUID_TOPIC, @"
                                        {
                                            ""action"": ""client-info"",
                                            ""data"": 
                                            {
                                                ""appVersion"": """ + MainForm.APP_VERSION + @""",
			                                    ""resolution"":
			                                    {
                                                    ""width"": " + screenWidth + @",
				                                    ""height"": " + screenHeight + @"
                                                }
                                            }
                                        }");


                        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(CLIENTUID_TOPIC).Build());

                        LogTracer.Instance.Trace("Subscribed to MQTT topics");
                    };

                    _client.ApplicationMessageReceivedAsync += e => 
                    {
                        var payload = e.ApplicationMessage.PayloadSegment;
                        string strPayload = payload.Array == null
                            ? string.Empty
                            : Encoding.UTF8.GetString(payload.Array, payload.Offset, payload.Count);

                        LogTracer.Instance.Trace(string.Format(@"Message received from NodeJS server: topic ({0}) - payload ({1})", e.ApplicationMessage.Topic, strPayload));

                        /*
                        Console.WriteLine("### RECEIVED APPLICATION MESSAGE ###");
                        Console.WriteLine($"+ Topic = {e.ApplicationMessage.Topic}");
                        Console.WriteLine($"+ Payload = {Encoding.UTF8.GetString(e.ApplicationMessage.Payload)}");
                        Console.WriteLine($"+ QoS = {e.ApplicationMessage.QualityOfServiceLevel}");
                        Console.WriteLine($"+ Retain = {e.ApplicationMessage.Retain}");
                        Console.WriteLine();
                        */

                        Console.WriteLine("REALTIME--> " + e.ApplicationMessage.Topic);

                        try
                        {
                            if (e.ApplicationMessage.Topic == INIT_TOPIC)
                            {
                                JObject jsonResult = JObject.Parse(strPayload);
                                OnInitPresentation?.Invoke(jsonResult);
                            }
                            else if (e.ApplicationMessage.Topic == UNLOAD_TOPIC)
                            {
                                OnUnloadPresentation?.Invoke();
                            }
                            else if (e.ApplicationMessage.Topic == GOTO_TOPIC)
                            {
                                JObject jsonResult = JObject.Parse(strPayload);
                                OnGotoScene?.Invoke(jsonResult);
                            }
                            else if (e.ApplicationMessage.Topic == CLIENT_TOPIC)
                            {
                                // use the action attribute to determine the message data
                                JObject message = JObject.Parse(strPayload);
                                if (message != null)
                                {
                                    string action = message.Get<string>("action");
                                    if (action == "room-init")
                                    {
                                        // now it communicates the room PIN code
                                        var data = message.Get<JObject>("data");
                                        OnConnectionSuccess?.Invoke(data);
                                    }
                                }
                            }
                            else if (e.ApplicationMessage.Topic == CLIENTUID_TOPIC)
                            {
                                JObject message = JObject.Parse(strPayload);
                                if (message != null)
                                {
                                    string action = message.Get<string>("action");
                                    if (action == "app-need-update")
                                    {
                                        // used to force the client app to be updated
                                        OnClientNotUpdatedError?.Invoke();
                                    }
                                }
                            }
                            // live contents
                            else if (e.ApplicationMessage.Topic == LIVEINIT_TOPIC)
                            {
                                JObject jsonResult = JObject.Parse(strPayload);
                                OnInitLiveContent?.Invoke(jsonResult);
                            }
                            else if (e.ApplicationMessage.Topic == LIVEUNLOAD_TOPIC)
                            {
                                OnUnloadLiveContent?.Invoke();
                            }
                            else if (e.ApplicationMessage.Topic == LIVEGOTO_TOPIC)
                            {
                                JObject jsonResult = JObject.Parse(strPayload);
                                OnGotoSceneLiveContent?.Invoke(jsonResult);
                            }
                            // display mode start/stop
                            else if (e.ApplicationMessage.Topic == CLIENT_DISPLAYMODE_START_TOPIC)
                            {
                                JObject jsonResult = JObject.Parse(strPayload);
                                OnClientDisplayModeStart?.Invoke(jsonResult);
                            }
                            else if (e.ApplicationMessage.Topic == CLIENT_DISPLAYMODE_STOP_TOPIC ||
                                     e.ApplicationMessage.Topic == DISPLAYMODE_STOP_TOPIC)
                            {
                                OnClientDisplayModeStop?.Invoke();
                            }
                        }
                        catch (Exception ex)
                        {
                            var message = string.Format("Error handling MQTT message on topic {0}: {1}", e.ApplicationMessage.Topic, ex.Message);
                            LogTracer.Instance.Trace(message, System.Diagnostics.TraceEventType.Error);
                            OnError?.Invoke(message);
                        }

                        return Task.CompletedTask;
                    };


                    _connectionOptions = new MqttClientOptionsBuilder()
                                    .WithClientId(_clientUid)
                                    .WithWebSocketServer(options => options.WithUri(brokerURL))
                                    .WithCleanSession()
                                    .Build();
                }

                if (!_client.IsConnected)
                    await _client.ConnectAsync(_connectionOptions);
            }
            catch (Exception e)
            {
                OnConnectionError?.Invoke(e.Message);
            }
        }

        public async Task Close()
        {
            if (_client == null)
                return;

            try
            {
                _forceClose = true;
                await _client.DisconnectAsync();
                _client.Dispose();
            }
            catch(Exception e)
            {
                LogTracer.Instance.Trace(string.Format("Error on close {0}", e.Message), System.Diagnostics.TraceEventType.Error);
            }
            _client = null;
        }

        public async void Reconnect()
        {
            await Close();

            Connect(_retryReconnectionInSeconds, _screenWidth, _screenHeight);
        }

        private const int PUBLISH_MAX_RETRY = 20; // ~10s con attese da 500ms

        private async Task PublishMessage(string topic, string message, int attempt = 0)
        {
            if (_client == null || !_client.IsConnected)
            {
                if (attempt >= PUBLISH_MAX_RETRY)
                {
                    LogTracer.Instance.Trace(
                        string.Format("Impossibile pubblicare sul topic {0}: client non connesso dopo {1} tentativi", topic, attempt),
                        System.Diagnostics.TraceEventType.Warning);
                    return;
                }
                await Task.Delay(500);
                await PublishMessage(topic, message, attempt + 1);
                return;
            }

            var appMessage = new MqttApplicationMessageBuilder()
                                    .WithTopic(topic)
                                    .WithPayload(message)
                                    .WithRetainFlag()
                                    .Build();
            await _client.PublishAsync(appMessage);
        }

        public async Task PresentationDownloadStartAsync()
        {
            // the client communicate to the server that it is starting the download of all the presentation files
            await PublishMessage(CLIENT_TOPIC, @"{
                                                    ""action"": ""download-start""
                                                 }");
        }

        public async Task PresentationDownloadEndedAsync()
        {
            // the client communicate to the server that it has downloaded all the presentation files
            await PublishMessage(CLIENT_TOPIC, @"{
                                                    ""action"": ""download-ended""
                                                 }");
        }

        public async Task PresentationGotoSlideEndAsync(int sceneIndex, int subSceneIndex)
        {
            // the client communicate to the server that it has changed the slide
            await PublishMessage(CLIENT_TOPIC, @"{
                                                    ""action"": ""scene-changed"",
                                                    ""data"": 
                                                    {
                                                        ""sceneIndex"": " + sceneIndex + @", 
                                                        ""subSceneIndex"": " + subSceneIndex + @"
                                                    }
                                                 }");
        }

        public async Task PresentationErrorAsync(int sceneIndex, int contentIndex, int errorCode, string message = "")
        {
            // the client communicate to the server that it has error
            await PublishMessage(CLIENT_TOPIC, @"{
                                                    ""action"": ""scene-content-error"",
                                                    ""data"": 
                                                    {
                                                        ""sceneIndex"": " + sceneIndex + @", 
                                                        ""contentIndex"": " + contentIndex + @",
                                                        ""errorCode"": " + errorCode + @",
                                                        ""message"": """ + message + @"""
                                                    }
                                                 }");
        }
        
    }
}
