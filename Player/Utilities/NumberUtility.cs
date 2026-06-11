using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ContentDistributionPlayer.Utilities
{
    class NumberUtility
    {
        // NOTA: si usa SEMPRE InvariantCulture perché questi valori provengono dal JSON
        // (JValue.ToString() formatta in invariante, con '.' come separatore decimale).
        // Parsare con la cultura corrente romperebbe numeri come "0.5" sui sistemi che
        // usano ',' come separatore decimale (es. locale CH/IT/DE) -> bounds delle scene errati.
        public static bool IsFloat(string value)
        {
            return !IsInt(value) &&
                   float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }
        public static bool IsInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }
    }
}
