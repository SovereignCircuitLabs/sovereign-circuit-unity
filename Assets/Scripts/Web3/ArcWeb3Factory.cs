using System;
using ArcTrading.Web3Net;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

// Single entry point for constructing Nethereum Web3 instances across all platforms.
//
// Editor / Standalone: keeps Nethereum's default network layer (System.Net.Http based RpcClient).
// WebGL player builds : swaps in WebGlUnityRpcClient so the transport runs through UnityWebRequest
//                       (browser fetch / XHR) — avoiding libc / native sockets / System.Net.Http,
//                       which are not supported under WebGL IL2CPP.
//
// Replace every `new Web3(...)` in the project with `ArcWeb3Factory.Create(...)`.
public static class ArcWeb3Factory
{
    public static Web3 Create(string rpcUrl)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return new Web3(new WebGlUnityRpcClient(new Uri(rpcUrl)));
#else
        return new Web3(rpcUrl);
#endif
    }

    public static Web3 Create(Account account, string rpcUrl)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return new Web3(account, new WebGlUnityRpcClient(new Uri(rpcUrl)));
#else
        return new Web3(account, rpcUrl);
#endif
    }
}
