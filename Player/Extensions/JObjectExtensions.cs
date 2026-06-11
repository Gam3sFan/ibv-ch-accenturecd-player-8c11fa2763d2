using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ContentDistributionPlayer.Extensions
{
    static class JObjectExtensions
    {
        public static T Get<T>(this JObject self, string property, T ifNullValue = default)
        {
            T retValue = ifNullValue;
            if (self[property] != null)
            {
                try
                {
                    retValue = self[property].Value<T>();
                }
                catch //(Exception ex)
                {
                    //Debug.WriteLine("Error: " + ex.Message);
                }
            }

            return retValue;
        }
    }
}
