using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArcTrading.MacroAgent
{
    public class EconomySnapshotCollector
    {
        private const float RecentActivityWindowSeconds = 120f;

        public EconomySnapshot Collect()
        {
            EconomySnapshot snapshot = new EconomySnapshot
            {
                capturedAtUtc = DateTime.UtcNow.ToString("o"),
                gameTimeSeconds = Time.time
            };

            List<TradingNpcActor> npcs = GetAllNpcs();
            float now = Time.time;

            int totalDeposit = 0;
            int totalWithdraw = 0;
            int totalHold = 0;
            int totalFailure = 0;
            int runningChain = 0;
            int blockedByCooldown = 0;
            float cooldownSum = 0f;

            float walletSum = 0f;
            float vaultSum = 0f;
            List<float> walletValues = new List<float>();
            List<float> vaultValues = new List<float>();

            for (int i = 0; i < npcs.Count; i++)
            {
                TradingNpcActor npc = npcs[i];
                if (npc == null)
                {
                    continue;
                }

                NpcSnapshotEntry entry = BuildEntry(npc, now,
                    out int deposits, out int withdraws, out int holds, out int failures);

                snapshot.npcs.Add(entry);

                totalDeposit += deposits;
                totalWithdraw += withdraws;
                totalHold += holds;
                totalFailure += failures;
                if (entry.isRunningChainAction)
                {
                    runningChain++;
                }

                float remaining = Mathf.Max(0f, entry.chainActionCooldown - 0f);
                cooldownSum += remaining;
                if (remaining > 0f && !entry.isRunningChainAction)
                {
                    blockedByCooldown++;
                }

                walletSum += entry.walletUSDC;
                vaultSum += entry.vaultUSDC;
                walletValues.Add(entry.walletUSDC);
                vaultValues.Add(entry.vaultUSDC);
            }

            int n = Mathf.Max(1, snapshot.npcs.Count);
            float totalAll = walletSum + vaultSum;
            snapshot.market.npcCount = snapshot.npcs.Count;
            snapshot.market.totalWalletUSDC = walletSum;
            snapshot.market.totalVaultUSDC = vaultSum;
            snapshot.market.totalUSDC = totalAll;
            snapshot.market.averageWalletUSDC = walletSum / n;
            snapshot.market.averageVaultUSDC = vaultSum / n;
            snapshot.market.walletStdDev = StdDev(walletValues, snapshot.market.averageWalletUSDC);
            snapshot.market.vaultStdDev = StdDev(vaultValues, snapshot.market.averageVaultUSDC);
            snapshot.market.recentDepositCount = totalDeposit;
            snapshot.market.recentWithdrawCount = totalWithdraw;
            snapshot.market.recentHoldCount = totalHold;
            snapshot.market.depositToWithdrawRatio = totalWithdraw > 0
                ? (float)totalDeposit / totalWithdraw
                : totalDeposit;
            float concentration = totalAll > 0f ? (snapshot.market.vaultStdDev / Mathf.Max(0.0001f, totalAll / n)) : 0f;
            snapshot.market.volatilityIndex = Mathf.Clamp01(concentration);

            snapshot.chain.runningChainActions = runningChain;
            snapshot.chain.recentChainFailures = totalFailure;
            snapshot.chain.averageCooldownRemaining = cooldownSum / n;
            snapshot.chain.npcsBlockedByCooldown = blockedByCooldown;

            WorldEventManager manager = WorldEventManager.Instance;
            if (manager != null)
            {
                List<ActiveWorldEvent> active = manager.GetActiveEvents();
                for (int i = 0; i < active.Count; i++)
                {
                    snapshot.activeWorldEvents.Add(new WorldEventSnapshot
                    {
                        type = active[i].type.ToString(),
                        displayName = active[i].displayName,
                        elapsedSeconds = Time.time - active[i].startedGameTime
                    });
                }
            }

            return snapshot;
        }

        private NpcSnapshotEntry BuildEntry(TradingNpcActor npc, float now,
            out int deposits, out int withdraws, out int holds, out int failures)
        {
            deposits = 0;
            withdraws = 0;
            holds = 0;
            failures = 0;

            NpcSnapshotEntry entry = new NpcSnapshotEntry
            {
                npcId = npc.GetInstanceID().ToString(),
                displayName = npc.name,
                archetype = npc.archetype.ToString(),
                currentActivity = npc.CurrentActivity,
                isRunningChainAction = npc.IsRunningChainAction,
                walletUSDC = npc.portfolioState.walletUSDC,
                vaultUSDC = npc.portfolioState.vaultUSDC,
                totalUSDC = npc.portfolioState.TotalUSDC,
                livingBudgetUSDC = npc.portfolioState.livingBudgetUSDC,
                reserveBudgetUSDC = npc.portfolioState.reserveBudgetUSDC,
                tradingBudgetUSDC = npc.portfolioState.tradingBudgetUSDC,
                livingNeedsWeight = npc.portfolioConfig.livingNeedsWeight,
                reserveWeight = npc.portfolioConfig.reserveWeight,
                tradingWeight = npc.portfolioConfig.tradingWeight,
                minTradeUSDC = npc.portfolioConfig.minTradeUSDC,
                maxTradeUSDC = npc.portfolioConfig.maxTradeUSDC,
                rebalanceInterval = npc.portfolioConfig.rebalanceInterval,
                chainActionCooldown = npc.portfolioConfig.chainActionCooldown
            };

            List<TradingNpcActivityRecord> recent = npc.GetRecentActivities(40);
            for (int i = 0; i < recent.Count; i++)
            {
                TradingNpcActivityRecord rec = recent[i];
                if (now - rec.gameTime > RecentActivityWindowSeconds)
                {
                    continue;
                }

                switch (rec.type)
                {
                    case TradingNpcActivityType.TradeDeposit: deposits++; break;
                    case TradingNpcActivityType.TradeWithdraw: withdraws++; break;
                    case TradingNpcActivityType.TradeHold: holds++; break;
                    case TradingNpcActivityType.ChainActionFailed: failures++; break;
                }
            }

            entry.recentDeposits = deposits;
            entry.recentWithdraws = withdraws;
            entry.recentHolds = holds;
            entry.recentFailures = failures;
            return entry;
        }

        private static float StdDev(List<float> values, float mean)
        {
            if (values.Count <= 1)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < values.Count; i++)
            {
                float d = values[i] - mean;
                sum += d * d;
            }

            return Mathf.Sqrt(sum / values.Count);
        }

        public static List<TradingNpcActor> GetAllNpcs()
        {
            if (ActorManager.Instance != null)
            {
                return ActorManager.Instance.GetAllActorsByType<TradingNpcActor>();
            }

            return new List<TradingNpcActor>(UnityEngine.Object.FindObjectsOfType<TradingNpcActor>());
        }
    }
}
