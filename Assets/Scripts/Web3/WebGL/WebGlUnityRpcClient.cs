using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Nethereum.JsonRpc.Client;
using Nethereum.JsonRpc.Client.RpcMessages;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace ArcTrading.Web3Net
{
    // WebGL-safe JSON-RPC transport for Nethereum.
    //
    // WebGL specifics intentionally encoded in this file:
    //   1. `SendAsync` is NOT `async` and does NOT use `RunContinuationsAsynchronously`.
    //      Nethereum's ClientBase.SendInnerRequestAsync awaits us with `.ConfigureAwait(false)`,
    //      which on WebGL routes the continuation through TaskScheduler.Default. Wasm has no
    //      real ThreadPool, so that continuation never fires and the entire await chain stalls
    //      (symptom: response is deserialized but caller's `await` never resumes).
    //      By returning a plain TaskCompletionSource.Task and calling TrySetResult from
    //      op.completed (Unity main thread), every awaiter continuation in the chain runs
    //      synchronously inline on the main thread — no SyncContext / ThreadPool hop.
    //
    //   2. Transport uses UnityEngine.Networking.UnityWebRequest, which in WebGL maps to the
    //      browser's fetch/XHR — no System.Net.Http, no native sockets, no libc.
    public class WebGlUnityRpcClient : ClientBase
    {
        private readonly Uri _baseUrl;
        private readonly JsonSerializerSettings _jsonSerializerSettings;
        private readonly Dictionary<string, string> _headers;
        private readonly int _timeoutSeconds;

        public WebGlUnityRpcClient(
            Uri baseUrl,
            JsonSerializerSettings jsonSerializerSettings = null,
            Dictionary<string, string> headers = null,
            int timeoutSeconds = 30)
        {
            if (baseUrl == null) throw new ArgumentNullException(nameof(baseUrl));
            _baseUrl = baseUrl;
            _jsonSerializerSettings = jsonSerializerSettings
                                      ?? DefaultJsonSerializerSettingsFactory.BuildDefaultJsonSerializerSettings();
            _headers = headers;
            _timeoutSeconds = timeoutSeconds <= 0 ? 30 : timeoutSeconds;
        }

        protected override Task<RpcResponseMessage> SendAsync(
            RpcRequestMessage request, string route = null)
        {
            var url = ResolveUrl(route);
            var requestJson = JsonConvert.SerializeObject(request, _jsonSerializerSettings);
            Debug.Log("[RPC][Request] " + requestJson);

            var tcs = new TaskCompletionSource<RpcResponseMessage>();

            StartWebRequest(url, requestJson,
                onSuccess: text =>
                {
                    try
                    {
                        Debug.Log("[RPC][ResponseRaw] " + text);
                        var response = JsonConvert.DeserializeObject<RpcResponseMessage>(
                            text, _jsonSerializerSettings);
                        Debug.Log("[RPC][Deserialized] id=" + response?.Id
                                  + ", hasError=" + (response?.Error != null)
                                  + ", result=" + response?.Result);
                        tcs.TrySetResult(response);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[RPC][DeserializeFailed] " + ex);
                        tcs.TrySetException(new RpcClientUnknownException(
                            "Invalid RPC response payload: " + Truncate(text), ex));
                    }
                },
                onError: ex =>
                {
                    tcs.TrySetException(new RpcClientUnknownException(
                        "Error occurred when trying to send rpc request via UnityWebRequest", ex));
                });

            return tcs.Task;
        }

        protected override Task<RpcResponseMessage[]> SendAsync(RpcRequestMessage[] requests)
        {
            var url = ResolveUrl(null);
            var requestJson = JsonConvert.SerializeObject(requests, _jsonSerializerSettings);

            var tcs = new TaskCompletionSource<RpcResponseMessage[]>();

            StartWebRequest(url, requestJson,
                onSuccess: text =>
                {
                    try
                    {
                        var response = JsonConvert.DeserializeObject<RpcResponseMessage[]>(
                            text, _jsonSerializerSettings);
                        tcs.TrySetResult(response);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(new RpcClientUnknownException(
                            "Invalid RPC batch response payload: " + Truncate(text), ex));
                    }
                },
                onError: ex =>
                {
                    tcs.TrySetException(new RpcClientUnknownException(
                        "Error occurred when trying to send rpc batch request via UnityWebRequest", ex));
                });

            return tcs.Task;
        }

        private string ResolveUrl(string route)
        {
            if (string.IsNullOrEmpty(route)) return _baseUrl.AbsoluteUri;
            return new Uri(_baseUrl, route).AbsoluteUri;
        }

        private void StartWebRequest(string url, string json,
            Action<string> onSuccess, Action<Exception> onError)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw) { contentType = "application/json" };
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.timeout = _timeoutSeconds;

            if (_headers != null)
            {
                foreach (var kv in _headers)
                {
                    request.SetRequestHeader(kv.Key, kv.Value);
                }
            }

            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
#if UNITY_2020_2_OR_NEWER
                    var failed = request.result != UnityWebRequest.Result.Success;
#else
                    var failed = request.isNetworkError || request.isHttpError;
#endif
                    if (failed)
                    {
                        var body = request.downloadHandler != null ? request.downloadHandler.text : null;
                        onError(new Exception(
                            $"UnityWebRequest failed: {request.error} (HTTP {request.responseCode}) Body: {Truncate(body)}"));
                    }
                    else
                    {
                        onSuccess(request.downloadHandler.text);
                    }
                }
                catch (Exception ex)
                {
                    onError(ex);
                }
                finally
                {
                    request.Dispose();
                }
            };
        }

        private static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= 512 ? s : s.Substring(0, 512) + "...";
        }
    }
}
