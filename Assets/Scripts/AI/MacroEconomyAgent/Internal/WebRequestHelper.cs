using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace ArcTrading.MacroAgent.Internal
{
    public static class WebRequestHelper
    {
        public class HttpResult
        {
            public bool success;
            public long statusCode;
            public string body;
            public string error;
        }

        public static Task<HttpResult> PostJsonAsync(string url, string json, int timeoutSeconds,
            (string key, string value)[] headers)
        {
            TaskCompletionSource<HttpResult> tcs = new TaskCompletionSource<HttpResult>();

            try
            {
                UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
                byte[] bodyBytes = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");
                request.timeout = Math.Max(1, timeoutSeconds);

                if (headers != null)
                {
                    for (int i = 0; i < headers.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(headers[i].key))
                        {
                            request.SetRequestHeader(headers[i].key, headers[i].value ?? string.Empty);
                        }
                    }
                }

                UnityWebRequestAsyncOperation op = request.SendWebRequest();
                op.completed += _ =>
                {
                    HttpResult result = new HttpResult
                    {
                        statusCode = request.responseCode,
                        body = request.downloadHandler != null ? request.downloadHandler.text : null
                    };

#if UNITY_2020_2_OR_NEWER
                    result.success = request.result == UnityWebRequest.Result.Success;
#else
                    result.success = !request.isNetworkError && !request.isHttpError;
#endif
                    result.error = request.error;
                    request.Dispose();
                    tcs.TrySetResult(result);
                };
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(new HttpResult
                {
                    success = false,
                    statusCode = 0,
                    error = ex.Message
                });
            }

            return tcs.Task;
        }
    }
}
