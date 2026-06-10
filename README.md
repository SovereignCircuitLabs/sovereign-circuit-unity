# Sovereign Circuit — Unity Client

**Language / 语言 / 語言**:
[English](#english) ｜ [简体中文](#简体中文) ｜ [繁體中文](#繁體中文)

- Unity Client (this repo): https://github.com/SovereignCircuitLabs/sovereign-circuit-unity
- Smart Contracts: https://github.com/SovereignCircuitLabs/sovereign-circuit-contracts
- x402 Seller Server: https://github.com/SovereignCircuitLabs/sovereign-circuit-server

---

## English

### 1. Project Overview

**Sovereign Circuit** (a.k.a. *Arc-Chain-Economy-System*) is an experimental **Autonomous Agentic Economy Infrastructure** built on top of [Arc Network](https://arc.network/). It validates whether AI NPCs can become first-class economic participants — owning wallets, holding assets, paying for services and trading with each other on-chain.

Concretely, the demo combines:

- A Unity game world where every NPC is an **Autonomous Economic Agent** with its own portfolio, risk profile and behavior tree.
- An **on-chain identity layer** based on **ERC-6551 Token Bound Accounts (TBA)** — every NPC NFT owns its own smart-contract wallet that holds USDC and ERC-1155 game items.
- A **machine-to-machine micropayment layer** based on **Circle x402 + Gateway Nanopayments**, so NPCs can pay each other (or paywalled APIs) in fractions of a USDC with zero per-call gas.
- An **LLM-driven macro central-bank agent** that observes the world via MCP-style read-only tools and tunes global economic modifiers (inflation, liquidity crunch, market boom…) every few seconds.
- A **MetaMask SIWE login + transaction-bridge** so the desktop client can use a real wallet without ever holding the player's private key.

The system models a closed economic loop:

> Player logs in wallet → NPC NFTs are enumerated → NPCs spawn → NPCs trade autonomously → x402 micropayments settle via Circle Gateway → ERC-1155 items mint into NPC TBAs → NPC NFT value grows → players buy/sell NPCs on the marketplace.

**TBA vs Payment Wallet.** Each NPC is backed by two distinct on-chain artifacts. The **TBA (ERC-6551 Token Bound Account)** is the NPC's asset-custody smart-contract wallet — deterministically derived from the NFT, it holds that NPC's USDC and ERC-1155 inventory, and ownership transfers automatically whenever the NFT is sold. The **Payment Wallet** is a separate off-chain EOA, generated locally and registered on-chain via `bindPaymentWallet(tokenId, addr)`; it is used solely to sign EIP-3009 `transferWithAuthorization` messages so the NPC can spend from the shared Circle Gateway Wallet for x402 nanopayments. It carries no assets, can be rotated/revoked by the NFT owner, and is automatically cleared on any NFT transfer.

### 2. Tech Stack

| Layer | Technology |
| --- | --- |
| Client / Game | Unity **2022.3.21f1**, URP, Fluid Behavior Tree, NavMesh, Steering Behaviors |
| Web3 client | **Nethereum 5.0** |
| Blockchain | **Arc Testnet** (EVM compatible, chainId `5042002`), native USDC `0x3600…0000` |
| Smart contracts | **Solidity** + **Foundry** (`forge` / `cast` / `anvil`) + OpenZeppelin |
| NPC identity | **ERC-721** (`NpcCharacter`) + **ERC-6551** (`Registry` + `Account` impl, ERC-1167 minimal proxy via `CREATE2`) |
| Game assets | **ERC-1155** (`GameItems`: MarketIntel / EnergyPack / AccessPass / RiskReport / ServiceVoucher) |
| Marketplace | `NpcNFTPricing` + `NpcMarketplace` (sudoswap-inspired AMM curve) |
| Micropayments | **Circle x402** + **Gateway Nanopayments** (EIP-3009 `TransferWithAuthorization`, EIP-712 typed signatures, batched settlement) |
| Seller backend | **Node.js + TypeScript + Express + viem + `@circle-fin/x402-batching`** |
| AI Macro Agent | LLM tool-loop (**OpenAI / Anthropic Claude / Google Gemini / DeepSeek**) + in-process **MCP** read-only tools |
| Wallet login | **EIP-1193** (MetaMask/Rabby/OKX) + **EIP-4361 SIWE** + local `HttpListener` Tx-bridge |
| Storage | `PlayerPrefs` for sessions, `Application.persistentDataPath` for NPC payment-wallet vault |

### 3. Startup Flow

> The Unity client can **only** complete an end-to-end x402 purchase if the local x402 Seller server is reachable at `http://localhost:4021/item/`. **Always start the seller first.**

#### 3.1 Prerequisites

- Node.js ≥ 18
- Unity **2022.3.21f1**
- A funded Arc Testnet wallet (USDC from [Circle Faucet](https://faucet.circle.com))
- MetaMask (desktop) with Arc Testnet RPC `https://rpc.testnet.arc.network`, chainId `5042002`

#### 3.2 Step 1 — Start the x402 Seller server

```bash
# Repo: https://github.com/SovereignCircuitLabs/sovereign-circuit-server
cd <path-to>/unity_nanopayments_server

# Install deps
npm install

# One-time: generate CLIENT_PRIVATE_KEY / SERVER_PRIVATE_KEY / SERVER_ADDRESS
npm run setup
# → paste the printed values into .env

# Fund CLIENT address with Arc Testnet USDC via https://faucet.circle.com
# Then deposit USDC into the Circle Gateway Wallet (only needed once)
npm run deposit -- 1     # deposits 1 USDC into Gateway

# Start the paywalled API on :4021
npm run server
```

Expected log:
```
[x402-seller] listening on http://localhost:4021
```

Sanity check (in a second terminal):
```bash
curl -i http://localhost:4021/item/1     # should return HTTP/1.1 402 Payment Required
```

#### 3.3 Step 2 — Open the Unity project

1. Open this folder in **Unity Hub** → Unity **2022.3.21f1**.
2. Open the scene `Assets/Scenes/MenuScene.unity`.
3. Verify `ArcNanopaymentClient.x402ServerBaseUrl == http://localhost:4021/item/` on the relevant prefab.
4. Press **Play**.

#### 3.4 Step 3 — Wallet login (browser pop-up) in MenuScene

- On Play the MenuScene auto-spawns `WalletLoginBootstrap`, opens the default browser to `http://127.0.0.1:7777/login`.
- Click **Connect Wallet** → MetaMask asks for accounts.
- Click **Sign In** → MetaMask shows a human-readable SIWE message → Sign.
- The browser tab displays `Bridge active — keep this tab open while playing.` **Do not close it** — Unity will route every owner-side write transaction (`bindPaymentWallet`, `USDC.transfer`, …) through this tab.
- From the MenuScene you can open the **NPC Market** and the **project website** to buy an NPC (or skip this if your wallet already holds at least one NPC NFT). Only after the wallet holds ≥ 1 NPC NFT can you enter the **MainScene** to start playing.

#### 3.5 Step 4 — NPC spawn & economy loop

- After entering MainScene, Unity reads the NPC NFTs owned by the connected wallet, spawns the matching prefabs (Aggressive / Balanced / Conservative).
- Each NPC initializes its **payment wallet** (`NpcPaymentWalletService.EnsureBoundOrRebindAsync`) — first run will trigger one MetaMask confirmation per NPC to `bindPaymentWallet(tokenId, addr)`.
- After binding the NPCs run autonomously: rebalance → consume → trade → buy ERC-1155 items via x402.
- The `MacroEconomyAgent` (toggle the panel with **Tab**) periodically polls the LLM and adjusts world event modifiers.

---

## 简体中文

### 1. 项目介绍

**Sovereign Circuit**（即 *Arc-Chain-Economy-System*）是一个构建在 [Arc Network](https://arc.network/) 之上的实验性 **自治智能体经济基础设施（Autonomous Agentic Economy Infrastructure）**。它验证的核心命题是：**AI NPC 能否成为真正意义上的经济参与者**——拥有自己的钱包、持有资产、为服务付费，并在链上彼此交易。

整个 Demo 把下列模块拼成一个闭环：

- 一个 Unity 游戏世界，里面每一个 NPC 都是 **自治经济体（Autonomous Economic Agent）**，拥有独立的 portfolio 配置、风险偏好和行为树。
- 基于 **ERC-6551 Token Bound Account（TBA）** 的 **链上身份层**——每个 NPC NFT 都附带一个智能合约钱包，用来持有 USDC 与 ERC-1155 游戏道具。
- 基于 **Circle x402 + Gateway Nanopayments** 的 **机器对机器微支付层**，让 NPC 之间（或调用付费 API）可以用零 gas、亚分级别的金额完成支付。
- 一个 **LLM 驱动的宏观调控 Agent（央行大脑）**，通过 MCP 风格的只读工具感知世界，并周期性调整全局经济乘数（通胀 / 流动性紧缩 / 市场繁荣 / 能源短缺）。
- **MetaMask SIWE 登录 + 交易桥**，让桌面端可以真正使用玩家钱包，而本地客户端永远拿不到玩家私钥。

完整的价值流转：

> 玩家钱包登录 → 枚举链上 NPC NFT → 动态生成 NPC → NPC 自主交易 → x402 微支付通过 Circle Gateway 结算 → ERC-1155 物品铸造进 NPC TBA → NPC 财富增长 → NPC NFT 在二级市场被买卖

**TBA 与 PaymentWallet 的区别。** 每个 NPC 在链上同时对应两个完全不同的实体。**TBA（ERC-6551 Token Bound Account）** 是 NPC 的资产托管智能合约钱包——由 NFT 确定性派生而来，用于持有该 NPC 的 USDC 和 ERC-1155 道具库存，NFT 被卖出时其所有权随 NFT 自动转移。**PaymentWallet** 则是另一个本地生成的链下 EOA，通过 `bindPaymentWallet(tokenId, addr)` 注册到链上；它只负责对 EIP-3009 `transferWithAuthorization` 消息签名，从而让 NPC 能够动用共享的 Circle Gateway Wallet 完成 x402 微支付。它本身不持有任何资产，可由 NFT 持有人随时轮换 / 吊销，并且在 NFT 发生任何转移时被自动清空。

### 2. 技术栈

| 层级 | 技术 |
| --- | --- |
| 客户端 / 游戏 | Unity **2022.3.21f1**、URP、Fluid Behavior Tree、NavMesh、Steering Behaviors |
| Web3 客户端 | **Nethereum 5.0** |
| 区块链 | **Arc Testnet**（EVM 兼容，chainId `5042002`），链原生 USDC `0x3600…0000` |
| 智能合约 | **Solidity** + **Foundry**（`forge` / `cast` / `anvil`）+ OpenZeppelin |
| NPC 身份 | **ERC-721**（`NpcCharacter`）+ **ERC-6551**（Registry + Account 实现，通过 `CREATE2` 部署 ERC-1167 极简代理） |
| 游戏资产 | **ERC-1155**（`GameItems`：MarketIntel / EnergyPack / AccessPass / RiskReport / ServiceVoucher） |
| 交易所 | `NpcNFTPricing` + `NpcMarketplace`（参考 sudoswap 的 AMM 曲线） |
| 微支付 | **Circle x402** + **Gateway Nanopayments**（EIP-3009 `TransferWithAuthorization`、EIP-712 类型化签名、批量结算） |
| Seller 后端 | **Node.js + TypeScript + Express + viem + `@circle-fin/x402-batching`** |
| AI 宏观 Agent | LLM tool-loop（**OpenAI / Anthropic Claude / Google Gemini / DeepSeek**）+ 进程内 **MCP** 只读工具 |
| 钱包登录 | **EIP-1193**（MetaMask/Rabby/OKX）+ **EIP-4361 SIWE** + 本地 `HttpListener` 交易桥 |
| 存储 | `PlayerPrefs` 存 session，`Application.persistentDataPath` 存 NPC PaymentWallet vault |

### 3. 启动流程

> Unity 端 **只有在本地 x402 Seller 服务器（`http://localhost:4021/item/`）启动的情况下** 才能完成端到端的 x402 购买。**请务必先启动 seller 服务器。**

#### 3.1 前置依赖

- Node.js ≥ 18
- Unity **2022.3.21f1**
- 一个在 Arc Testnet 上有 USDC 的钱包（[Circle Faucet](https://faucet.circle.com) 领取）
- 桌面版 MetaMask，并把 Arc Testnet 加入网络：RPC `https://rpc.testnet.arc.network`、chainId `5042002`

#### 3.2 Step 1 — 启动 x402 Seller 服务器

```bash
# 仓库：https://github.com/SovereignCircuitLabs/sovereign-circuit-server
cd <你的路径>/unity_nanopayments_server

# 安装依赖
npm install

# 一次性：生成 CLIENT_PRIVATE_KEY / SERVER_PRIVATE_KEY / SERVER_ADDRESS
npm run setup
# → 把控制台输出的值粘到 .env

# 用 https://faucet.circle.com 给 CLIENT 地址领 Arc Testnet USDC
# 然后把 USDC 充值到 Circle Gateway Wallet（只需执行一次）
npm run deposit -- 1     # 往 Gateway 充入 1 USDC

# 启动付费 API，监听 4021 端口
npm run server
```

正常会打印：
```
[x402-seller] listening on http://localhost:4021
```

可以另开一个终端验证：
```bash
curl -i http://localhost:4021/item/1     # 应返回 HTTP/1.1 402 Payment Required
```

#### 3.3 Step 2 — 打开 Unity 工程

1. 在 **Unity Hub** 中用 Unity **2022.3.21f1** 打开当前目录。
2. 打开场景 `Assets/Scenes/MenuScene.unity`。
3. 在相关 prefab 上确认 `ArcNanopaymentClient.x402ServerBaseUrl == http://localhost:4021/item/`。
4. 点 **Play**。

#### 3.4 Step 3 — MenuScene 钱包登录（浏览器弹窗）

- 进入 Play 后，MenuScene 会自动 spawn `WalletLoginBootstrap`，并在默认浏览器中打开 `http://127.0.0.1:7777/login`。
- 点 **Connect Wallet** → MetaMask 弹窗请求账户连接。
- 点 **Sign In** → MetaMask 显示一段人类可读的 SIWE 消息 → 点 Sign 签名。
- 浏览器随后显示 `Bridge active — keep this tab open while playing.` —— **不要关闭该标签页**，Unity 之后所有 owner 的写交易（`bindPaymentWallet`、`USDC.transfer` …）都通过这个 tab 让 MetaMask 签名。
- 在 MenuScene 中可以打开 **NPC Market** 与 **项目官网**，购买 NPC（如果你的钱包里已经持有 NPC NFT 则可跳过此步）。**只有当钱包持有 ≥ 1 个 NPC NFT 时，才能进入 MainScene 正式开始游戏。**

#### 3.5 Step 4 — NPC 生成与经济循环

- 进入 MainScene 后，Unity 会读取当前钱包持有的 NPC NFT，动态生成对应的 NPC 预制体（Aggressive / Balanced / Conservative）。
- 每个 NPC 初始化时会调用 `NpcPaymentWalletService.EnsureBoundOrRebindAsync` —— 首次运行会为每个 NPC 弹一次 `bindPaymentWallet(tokenId, addr)` 的 MetaMask 确认。
- 绑定完成后，NPC 进入自治循环：重平衡 → 消费 → 链上交易 → 通过 x402 购买 ERC-1155 物品。
- 按 **Tab** 打开 `MacroEconomyAgent` 控制面板，可以看到 LLM 周期性输出策略并改写全局事件乘数。

---

## 繁體中文

### 1. 專案介紹

**Sovereign Circuit**（即 *Arc-Chain-Economy-System*）是一個建構在 [Arc Network](https://arc.network/) 之上的實驗性 **自治智能體經濟基礎設施（Autonomous Agentic Economy Infrastructure）**。它要驗證的核心命題是：**AI NPC 是否能夠成為真正意義上的經濟參與者**——擁有自己的錢包、持有資產、為服務付費，並在鏈上彼此交易。

整個 Demo 把下列模組拼成一個閉環：

- 一個 Unity 遊戲世界，每個 NPC 都是 **自治經濟體（Autonomous Economic Agent）**，擁有獨立的 portfolio 設定、風險偏好與行為樹。
- 基於 **ERC-6551 Token Bound Account（TBA）** 的 **鏈上身份層**——每個 NPC NFT 都附帶一個智能合約錢包，用來持有 USDC 與 ERC-1155 遊戲道具。
- 基於 **Circle x402 + Gateway Nanopayments** 的 **機器對機器微支付層**，讓 NPC 之間（或呼叫付費 API）可以用零 gas、亞分級別的金額完成支付。
- 一個 **LLM 驅動的宏觀調控 Agent（央行大腦）**，透過 MCP 風格的唯讀工具感知世界，並週期性調整全域經濟乘數（通膨 / 流動性緊縮 / 市場繁榮 / 能源短缺）。
- **MetaMask SIWE 登入 + 交易橋**，讓桌面端可以真正使用玩家錢包，本地客戶端永遠拿不到玩家私鑰。

完整的價值流轉：

> 玩家錢包登入 → 列舉鏈上 NPC NFT → 動態生成 NPC → NPC 自主交易 → x402 微支付透過 Circle Gateway 結算 → ERC-1155 物品鑄造進 NPC TBA → NPC 財富增長 → NPC NFT 於二級市場被買賣

**TBA 與 PaymentWallet 的差異。** 每個 NPC 在鏈上同時對應兩個完全不同的實體。**TBA（ERC-6551 Token Bound Account）** 是 NPC 的資產託管智能合約錢包——由 NFT 確定性派生而來，用於持有該 NPC 的 USDC 與 ERC-1155 道具庫存，NFT 被賣出時其所有權隨 NFT 自動轉移。**PaymentWallet** 則是另一個本地生成的鏈下 EOA，透過 `bindPaymentWallet(tokenId, addr)` 註冊到鏈上；它只負責對 EIP-3009 `transferWithAuthorization` 訊息簽名，藉此讓 NPC 能夠動用共享的 Circle Gateway Wallet 完成 x402 微支付。它本身不持有任何資產，可由 NFT 持有人隨時輪換 / 撤銷，並且在 NFT 發生任何轉移時被自動清空。

### 2. 技術堆疊

| 層級 | 技術 |
| --- | --- |
| 客戶端 / 遊戲 | Unity **2022.3.21f1**、URP、Fluid Behavior Tree、NavMesh、Steering Behaviors |
| Web3 客戶端 | **Nethereum 5.0** |
| 區塊鏈 | **Arc Testnet**（EVM 相容，chainId `5042002`），鏈原生 USDC `0x3600…0000` |
| 智能合約 | **Solidity** + **Foundry**（`forge` / `cast` / `anvil`）+ OpenZeppelin |
| NPC 身份 | **ERC-721**（`NpcCharacter`）+ **ERC-6551**（Registry + Account 實作，透過 `CREATE2` 部署 ERC-1167 極簡代理） |
| 遊戲資產 | **ERC-1155**（`GameItems`：MarketIntel / EnergyPack / AccessPass / RiskReport / ServiceVoucher） |
| 交易所 | `NpcNFTPricing` + `NpcMarketplace`（參考 sudoswap 的 AMM 曲線） |
| 微支付 | **Circle x402** + **Gateway Nanopayments**（EIP-3009 `TransferWithAuthorization`、EIP-712 類型化簽名、批次結算） |
| Seller 後端 | **Node.js + TypeScript + Express + viem + `@circle-fin/x402-batching`** |
| AI 宏觀 Agent | LLM tool-loop（**OpenAI / Anthropic Claude / Google Gemini / DeepSeek**）+ 進程內 **MCP** 唯讀工具 |
| 錢包登入 | **EIP-1193**（MetaMask/Rabby/OKX）+ **EIP-4361 SIWE** + 本機 `HttpListener` 交易橋 |
| 儲存 | `PlayerPrefs` 存 session、`Application.persistentDataPath` 存 NPC PaymentWallet vault |

### 3. 啟動流程

> Unity 端 **只有在本機 x402 Seller 伺服器（`http://localhost:4021/item/`）啟動的情況下** 才能完成端對端的 x402 購買。**請務必先啟動 seller 伺服器。**

#### 3.1 前置需求

- Node.js ≥ 18
- Unity **2022.3.21f1**
- 一個在 Arc Testnet 上持有 USDC 的錢包（[Circle Faucet](https://faucet.circle.com) 領取）
- 桌面版 MetaMask，並把 Arc Testnet 加入網路：RPC `https://rpc.testnet.arc.network`、chainId `5042002`

#### 3.2 Step 1 — 啟動 x402 Seller 伺服器

```bash
# 倉庫：https://github.com/SovereignCircuitLabs/sovereign-circuit-server
cd <你的路徑>/unity_nanopayments_server

# 安裝相依套件
npm install

# 一次性：產生 CLIENT_PRIVATE_KEY / SERVER_PRIVATE_KEY / SERVER_ADDRESS
npm run setup
# → 把控制台輸出的值貼到 .env

# 用 https://faucet.circle.com 給 CLIENT 位址領 Arc Testnet USDC
# 然後把 USDC 充值到 Circle Gateway Wallet（僅需執行一次）
npm run deposit -- 1     # 向 Gateway 充入 1 USDC

# 啟動付費 API，監聽 4021 連接埠
npm run server
```

正常會印出：
```
[x402-seller] listening on http://localhost:4021
```

可以另開一個終端驗證：
```bash
curl -i http://localhost:4021/item/1     # 應回傳 HTTP/1.1 402 Payment Required
```

#### 3.3 Step 2 — 開啟 Unity 專案

1. 在 **Unity Hub** 中用 Unity **2022.3.21f1** 開啟此目錄。
2. 開啟場景 `Assets/Scenes/MenuScene.unity`。
3. 在相關 prefab 上確認 `ArcNanopaymentClient.x402ServerBaseUrl == http://localhost:4021/item/`。
4. 點 **Play**。

#### 3.4 Step 3 — MenuScene 錢包登入（瀏覽器彈窗）

- 進入 Play 後，MenuScene 會自動 spawn `WalletLoginBootstrap`，並在預設瀏覽器開啟 `http://127.0.0.1:7777/login`。
- 點 **Connect Wallet** → MetaMask 彈窗請求帳戶連線。
- 點 **Sign In** → MetaMask 顯示一段人類可讀的 SIWE 訊息 → 點 Sign 簽名。
- 瀏覽器隨後顯示 `Bridge active — keep this tab open while playing.` —— **請勿關閉該分頁**，Unity 之後所有 owner 的寫入交易（`bindPaymentWallet`、`USDC.transfer`…）都會透過這個分頁讓 MetaMask 簽名。
- 在 MenuScene 中可以開啟 **NPC Market** 與 **專案官網**，購買 NPC（若你的錢包已持有 NPC NFT 則可略過此步）。**只有當錢包持有 ≥ 1 個 NPC NFT 時，才能進入 MainScene 正式開始遊戲。**

#### 3.5 Step 4 — NPC 生成與經濟循環

- 進入 MainScene 後，Unity 會讀取目前錢包持有的 NPC NFT，動態生成對應的 NPC 預製體（Aggressive / Balanced / Conservative）。
- 每個 NPC 初始化時會呼叫 `NpcPaymentWalletService.EnsureBoundOrRebindAsync` —— 首次執行會為每個 NPC 跳一次 `bindPaymentWallet(tokenId, addr)` 的 MetaMask 確認。
- 綁定完成後，NPC 進入自治循環：重平衡 → 消費 → 鏈上交易 → 透過 x402 購買 ERC-1155 物品。
- 按 **Tab** 開啟 `MacroEconomyAgent` 控制面板，可以看到 LLM 週期性輸出策略並改寫全域事件乘數。

---

## License

See [`LICENSE`](./LICENSE).
