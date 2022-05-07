using ComponentAce.Compression.Libs.zlib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PaulovLauncher
{
    public class TarkovRequesting
    {
        public string Session;
        public string RemoteEndPoint;
        public bool isUnity;
        public TarkovRequesting(string session, string remoteEndPoint, bool isUnity = true)
        {
            Session = session;
            RemoteEndPoint = remoteEndPoint;
        }
        /// <summary>
        /// Send request to the server and get Stream of data back
        /// </summary>
        /// <param name="url">String url endpoint example: /start</param>
        /// <param name="method">POST or GET</param>
        /// <param name="data">string json data</param>
        /// <param name="compress">Should use compression gzip?</param>
        /// <returns>Stream or null</returns>
        private Stream Send(string url, string method = "GET", string data = null, bool compress = true)
        {
            // disable SSL encryption
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            var fullUri = url;
            if (!Uri.IsWellFormedUriString(fullUri, UriKind.Absolute))
                fullUri = RemoteEndPoint + fullUri;
            WebRequest request = WebRequest.Create(new Uri(fullUri));

            if (!string.IsNullOrEmpty(Session))
            {
                request.Headers.Add("Cookie", $"PHPSESSID={Session}");
                request.Headers.Add("SessionId", Session);
            }

            request.Headers.Add("Accept-Encoding", "deflate");

            request.Method = method;

            if (method != "GET" && !string.IsNullOrEmpty(data))
            {
                // set request body
                byte[] bytes = (compress) ? SimpleZlib.CompressToBytes(data, zlibConst.Z_BEST_COMPRESSION) : Encoding.UTF8.GetBytes(data);

                request.ContentType = "application/json";
                request.ContentLength = bytes.Length;

                if (compress)
                {
                    request.Headers.Add("content-encoding", "deflate");
                }

                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }
            }

            // get response stream
            try
            {
                WebResponse response = request.GetResponse();
                return response.GetResponseStream();
            }
            catch (Exception e)
            {
                
            }

            return null;
        }

        public void PutJson(string url, string data, bool compress = true)
        {
            using (Stream stream = Send(url, "PUT", data, compress)) { }
        }

        public string GetJson(string url, bool compress = true)
        {
            using (Stream stream = Send(url, "GET", null, compress))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    if (stream == null)
                        return "";
                    stream.CopyTo(ms);
                    return SimpleZlib.Decompress(ms.ToArray(), null);
                }
            }
        }

        public string PostJson(string url, string data, bool compress = true)
        {
            using (Stream stream = Send(url, "POST", data, compress))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    if (stream == null)
                        return "";
                    stream.CopyTo(ms);
                    return SimpleZlib.Decompress(ms.ToArray(), null);
                }
            }
        }

        
    }
}
