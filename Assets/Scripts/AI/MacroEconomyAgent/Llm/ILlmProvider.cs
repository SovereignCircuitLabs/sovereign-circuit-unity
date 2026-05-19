using System.Threading.Tasks;

namespace ArcTrading.MacroAgent.Llm
{
    public interface ILlmProvider
    {
        string Name { get; }
        Task<LlmResponse> RequestAsync(LlmRequest request);
    }
}
