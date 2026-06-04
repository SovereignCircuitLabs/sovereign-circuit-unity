using UnityEngine;
using System.Collections;
using System.Threading.Tasks;

public class WebGlRpcSmokeTest : MonoBehaviour
{
    public string rpcUrl = "https://rpc.testnet.arc.network";
    
    private void Start()
    {
        Debug.Log("[Smoke] Start");
        StartCoroutine(RunSmokeTestCoroutine());
    }

    IEnumerator RunSmokeTestCoroutine()
    {
        var web3 = ArcWeb3Factory.Create(rpcUrl);

        Debug.Log("[Smoke] Before chainId");
        
        var chainIdTask = web3.Eth.ChainId.SendRequestAsync();
        
        // 用最传统的 C# 协程死等 Task 完成
        // 因为没有 await 的 Context 切换
        // 纯靠 Unity 主线程每帧轮询，Wasm 绝对没有任何机会能吃掉你的执行上下文
        while (!chainIdTask.IsCompleted)
        {
            yield return null; // 每帧交出控制权，浏览器绝不卡死
        }
        
        if (chainIdTask.IsFaulted || chainIdTask.IsCanceled)
        {
            Debug.LogError("[Smoke] Task 失败或被取消! 异常: " + chainIdTask.Exception?.InnerException);
            yield break;
        }

        var chainId = chainIdTask.Result;

        Debug.Log("[Smoke] ！！！业务层成功苏醒！！！");
        Debug.Log("[Smoke] After chainId");
        Debug.Log("[Smoke] ChainId = " + chainId.Value);
    }
}