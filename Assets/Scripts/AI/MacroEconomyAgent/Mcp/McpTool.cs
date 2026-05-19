using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ArcTrading.MacroAgent.Mcp
{
    public class McpTool
    {
        public string Name;
        public string Description;
        public JObject JsonSchema;
        public Func<JObject, Task<string>> Handler;
    }
}
