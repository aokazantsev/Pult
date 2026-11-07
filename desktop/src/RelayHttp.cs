using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Pult
{
    internal static class RelayHttp
    {
        public const int MaxResponseBytes = 65536;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static HttpWebRequest Create(string url, string method, IDictionary<string, string> headers, int timeoutMs)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.KeepAlive = true;
            request.AllowAutoRedirect = false;
            if (request.RequestUri.IsLoopback) request.Proxy = null;
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers) request.Headers[header.Key] = header.Value;
            }
            return request;
        }

        public static RelayResponse Send(string url, string method, IDictionary<string, string> headers, byte[] body, int timeoutMs)
        {
            try
            {
                HttpWebRequest request = Create(url, method, headers, timeoutMs);
                if (body != null)
                {
                    request.ContentType = "application/json; charset=utf-8";
                    request.ContentLength = body.Length;
                    using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
                }
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    return new RelayResponse((int)response.StatusCode, ReadBody(response), null);
                }
            }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                if (response != null)
                {
                    using (response) return new RelayResponse((int)response.StatusCode, ReadBody(response), null);
                }
                return new RelayResponse(-1, null, error.Status.ToString());
            }
            catch (IOException error)
            {
                return new RelayResponse(-1, null, error.Message);
            }
        }

        public static RelayResponse PostJson(string url, string json, int timeoutMs)
        {
            return Send(url, "POST", null, Utf8.GetBytes(json), timeoutMs);
        }

        public static string ReadBody(HttpWebResponse response)
        {
            try
            {
                using (Stream stream = response.GetResponseStream())
                using (var collected = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        collected.Write(buffer, 0, read);
                        if (collected.Length > MaxResponseBytes) return null;
                    }
                    return Utf8.GetString(collected.ToArray());
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (WebException)
            {
                return null;
            }
        }
    }
}
