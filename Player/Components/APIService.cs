using ContentDistributionPlayer.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ContentDistributionPlayer.Components
{
    public delegate void APIServ_EventError(string message);
    public delegate void APIServ_EventSucces(JObject result);

    class APIService
    {
        public static string API_URI;

        private static int NUM_RETRY = 3;

        // Un'unica istanza condivisa e riutilizzata: creare/disporre un HttpClient per
        // ogni chiamata esaurisce i socket (SocketException / TIME_WAIT). HttpClient è
        // thread-safe per le chiamate concorrenti.
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task<JObject> CallGetAsync(string serviceName, APIServ_EventSucces success = null, APIServ_EventError error = null, int retryCount = 0)
        {
            if (string.IsNullOrEmpty(API_URI))
            {
                var errorMsg = @"The API uri is not defined!";
                LogTracer.Instance.Trace(errorMsg, System.Diagnostics.TraceEventType.Error);
                error?.Invoke(errorMsg);
                return null;
            }

            try
            {
                var uri = APIService.API_URI + serviceName;

                HttpResponseMessage response = await _httpClient.GetAsync(uri);
                response.EnsureSuccessStatusCode();
                string result = await response.Content.ReadAsStringAsync();
                JObject jsonObject = JObject.Parse(result);
                success?.Invoke(jsonObject);
                return jsonObject;
            }
            catch (Exception e)
            {
                // check the retry number
                if (retryCount < NUM_RETRY)
                {
                    await Task.Delay(1000);
                    return await CallGetAsync(serviceName, success, error, retryCount + 1);
                }
                var errorMsg = string.Format("Error during the http calls: {0}", e.Message);
                LogTracer.Instance.Trace(errorMsg, System.Diagnostics.TraceEventType.Error);
                error?.Invoke(errorMsg);
                return null;
            }
        }
    }
}
