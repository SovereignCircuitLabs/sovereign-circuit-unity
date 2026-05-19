using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ArcTrading.MacroAgent.Mcp
{
    /// <summary>
    /// Read-only tools that let the LLM drill into NPC / chain / market state.
    /// No tool here exposes private keys or initiates asset transfers.
    /// </summary>
    public class McpToolRegistry
    {
        private readonly List<McpTool> tools = new List<McpTool>();
        private readonly EconomySnapshotCollector collector;

        public McpToolRegistry(EconomySnapshotCollector collector)
        {
            this.collector = collector;
            RegisterDefaultTools();
        }

        public IReadOnlyList<McpTool> Tools
        {
            get { return tools; }
        }

        public McpTool Find(string name)
        {
            for (int i = 0; i < tools.Count; i++)
            {
                if (tools[i].Name == name)
                {
                    return tools[i];
                }
            }

            return null;
        }

        public async Task<string> InvokeAsync(string name, string argumentsJson)
        {
            McpTool tool = Find(name);
            if (tool == null)
            {
                return Err("Unknown tool: " + name);
            }

            JObject args;
            try
            {
                args = string.IsNullOrWhiteSpace(argumentsJson) ? new JObject() : JObject.Parse(argumentsJson);
            }
            catch (JsonException ex)
            {
                return Err("Malformed arguments JSON: " + ex.Message);
            }

            try
            {
                return await tool.Handler(args);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[MacroAgent.Mcp] Tool '" + name + "' threw: " + ex);
                return Err("Tool execution failed: " + ex.Message);
            }
        }

        private static string Err(string msg)
        {
            return JsonConvert.SerializeObject(new { error = msg });
        }

        private void RegisterDefaultTools()
        {
            tools.Add(new McpTool
            {
                Name = "list_npcs",
                Description = "List all trading NPCs with their archetype, current activity, wallet/vault totals. Read-only.",
                JsonSchema = JObject.Parse("{ \"type\": \"object\", \"properties\": {}, \"required\": [] }"),
                Handler = _ => Task.FromResult(JsonConvert.SerializeObject(BuildNpcList()))
            });

            tools.Add(new McpTool
            {
                Name = "get_npc_detail",
                Description = "Get full snapshot (config, state, recent activities) for a single NPC by npcId or display name. Read-only.",
                JsonSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""npcId"": { ""type"": ""string"", ""description"": ""Instance id of the NPC."" },
                        ""displayName"": { ""type"": ""string"", ""description"": ""Display name fallback."" }
                    },
                    ""required"": []
                }"),
                Handler = args => Task.FromResult(JsonConvert.SerializeObject(GetNpcDetail(args)))
            });

            tools.Add(new McpTool
            {
                Name = "get_active_world_events",
                Description = "Return currently active world events with elapsed time. Read-only.",
                JsonSchema = JObject.Parse("{ \"type\": \"object\", \"properties\": {}, \"required\": [] }"),
                Handler = _ => Task.FromResult(JsonConvert.SerializeObject(BuildActiveEvents()))
            });

            tools.Add(new McpTool
            {
                Name = "get_market_summary",
                Description = "Aggregated market metrics: totals, averages, std-dev, deposit/withdraw ratios, volatility index. Read-only.",
                JsonSchema = JObject.Parse("{ \"type\": \"object\", \"properties\": {}, \"required\": [] }"),
                Handler = _ => Task.FromResult(JsonConvert.SerializeObject(collector.Collect().market))
            });

            tools.Add(new McpTool
            {
                Name = "get_chain_status",
                Description = "Chain action status: running tx count, recent failures, cooldown saturation. Read-only.",
                JsonSchema = JObject.Parse("{ \"type\": \"object\", \"properties\": {}, \"required\": [] }"),
                Handler = _ => Task.FromResult(JsonConvert.SerializeObject(collector.Collect().chain))
            });
        }

        private object BuildNpcList()
        {
            EconomySnapshot snap = collector.Collect();
            List<object> list = new List<object>();
            for (int i = 0; i < snap.npcs.Count; i++)
            {
                NpcSnapshotEntry e = snap.npcs[i];
                list.Add(new
                {
                    npcId = e.npcId,
                    displayName = e.displayName,
                    archetype = e.archetype,
                    currentActivity = e.currentActivity,
                    walletUSDC = e.walletUSDC,
                    vaultUSDC = e.vaultUSDC,
                    totalUSDC = e.totalUSDC,
                    recentDeposits = e.recentDeposits,
                    recentWithdraws = e.recentWithdraws,
                    recentFailures = e.recentFailures
                });
            }

            return new { count = list.Count, npcs = list };
        }

        private object GetNpcDetail(JObject args)
        {
            string npcId = args.Value<string>("npcId");
            string displayName = args.Value<string>("displayName");
            EconomySnapshot snap = collector.Collect();

            for (int i = 0; i < snap.npcs.Count; i++)
            {
                NpcSnapshotEntry e = snap.npcs[i];
                if ((!string.IsNullOrEmpty(npcId) && e.npcId == npcId) ||
                    (!string.IsNullOrEmpty(displayName) && e.displayName == displayName))
                {
                    return e;
                }
            }

            return new { error = "NPC not found." };
        }

        private object BuildActiveEvents()
        {
            EconomySnapshot snap = collector.Collect();
            return new { count = snap.activeWorldEvents.Count, events = snap.activeWorldEvents };
        }
    }
}
