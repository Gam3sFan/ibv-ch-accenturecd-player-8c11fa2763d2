using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ContentDistributionPlayer.Utilities
{
    class WebUtility
    {
        public static HttpStatusCode GetHttpStatusCode(Exception err)
        {
            if (err is WebException)
            {
                WebException we = (WebException)err;
                if (we.Response is HttpWebResponse)
                {
                    HttpWebResponse response = (HttpWebResponse)we.Response;
                    return response.StatusCode;
                }
            }
            return 0;
        }
    }
}
