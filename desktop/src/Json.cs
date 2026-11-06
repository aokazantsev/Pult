using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace Pult
{
    internal static class Json
    {
        public static Dictionary<string, object> ParseObject(string text)
        {
            var parsed = Serializer().DeserializeObject(text) as Dictionary<string, object>;
            if (parsed == null) throw new FormatException("json root is not an object");
            return parsed;
        }

        public static Dictionary<string, object> TryParseObject(string text)
        {
            try
            {
                return ParseObject(text);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public static string Write(object value)
        {
            return Serializer().Serialize(value);
        }

        public static string String(IDictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value)) return null;
            return value as string;
        }

        public static bool Bool(IDictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) && value is bool && (bool)value;
        }

        public static Dictionary<string, object> Object(IDictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value)) return null;
            return value as Dictionary<string, object>;
        }

        public static object Value(IDictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? value : null;
        }

        public static long Long(IDictionary<string, object> map, string key, long fallback)
        {
            object value = Value(map, key);
            if (value == null) return fallback;
            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return fallback;
            }
            catch (InvalidCastException)
            {
                return fallback;
            }
            catch (OverflowException)
            {
                return fallback;
            }
        }

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        }
    }
}
