# 联机方案（Phase 4）· 自建 WebSocket 房主权威服务器

> 状态：定稿（2026-09-19）。决策依据：用户自有云服务器；TECH_DECISIONS D3。
> Unity Netcode + UGS Relay 依赖 Unity 官方云，与"自有服务器"诉求不符，弃用。

## 1. 拓扑与信任模型

```
Unity 客户端(×N) ── WebSocket(JSON) ── Node 权威服务器（云服务器）
                                        └─ 直接加载 avalon-preview/core.js（已验证零 DOM、UMD 双导出）
```

- **服务器是唯一事实来源**：全量 GameState 只在服务端持有；所有规则调用
  （proposeTeam/vote/playMission/assassinate）由 core.js 在服务端执行，客户端无法伪造。
- **私有视角下发**：客户端只收到"该座位在该时刻允许看到"的字段——
  身份只发给自己（knownInfo 按角色过滤）、任务票只发汇总张数、阶段与公开历史全量。
  抓包即见全量身份的作弊（H5 热座天然不存在此问题，联机必须防）。
- 房主不享有特权进程：所谓"房主"只是先创建房间拿到 6 位房间码的普通客户端。

## 2. 协议（JSON over WS）

信封：`{ "t": "<type>", ...payload }`；服务端每次状态变更后向每个座位下发其视图 `state`。

| 方向 | 类型 | 说明 |
|---|---|---|
| C→S | `create` | `{name}` → 建房，返回房间码（座位0） |
| C→S | `join` | `{code,name}` → 入房（座位1..N），满员/已开始则拒绝 |
| C→S | `start` | 房主发起，需 5–10 人；服务端 createGame 并逐座私发身份 |
| C→S | `propose` | `{members:[座位号]}` |
| C→S | `vote` | `{approve:bool}` |
| C→S | `mission` | `{success:bool}` |
| C→S | `assassinate` | `{target}` |
| C→S | `restart` / `leave` | 同配置重开 / 退出 |
| C→S | `reconnect` | `{code,seat,token}` 断线重连 |
| S→C | `welcome` | `{code,seat,token}` token 用于重连鉴权 |
| S→C | `state` | 该座位的过滤视图（见 §3） |
| S→C | `toast` | 校验失败等即时错误（不改变状态） |

## 3. 视图过滤（服务端组装，客户端永远收不到别人身份）

```
公共：phase/step、round、voteTrack、leader、proposal(公开)、results、failCards、
      history、log、players[{seat,name}]（无 role）、当前该谁行动 actorSeat
私有：me.seat、me.role（仅开局一次）、me.known（knownInfo 结果）
终局：over 时下发全量 players[].role 用于结算页
刺杀：仅刺客收到 target 可选列表提示；其他人只见"刺杀阶段进行中"
```

## 4. 房间生命周期与断线

- 空房 30 分钟回收；开局后有人掉线 → 阶段暂停广播"等待重连"。
- **超时默认行动**（防单人恶意卡房，可配置）：投票超时记反对；任务票超时好人记成功、
  坏人记失败；组队超时队长自动提名"最小座位号凑数队"；刺杀超时随机指认。
- 重连：`reconnect` + seat token 恢复原座位全量视图。

## 5. 部署（用户云服务器）

- 依赖仅 `ws`；`node server.js --port 8080`。systemd 或 pm2 常驻。
- 明文 `ws://` 起步（局域网/内网测试）；公网建议 Nginx/Caddy 反代加 `wss://`（证书 Let's Encrypt）。
- 客户端服务器地址：设置页输入框 + 记住上次（默认 `ws://<host>:<port>`）。

## 6. Unity 客户端改动

- 新增 `Avalon.Networking` asmdef（引用 Core/Game；传输用 `System.Net.WebSockets.ClientWebSocket`，零第三方包）。
- `GameSession` 已把"规则调用"全部收敛到 Engine——联机版新增 `NetSession`：
  本地热座走 Engine，联机走"发送意图 + 收到 state 视图后重建 UI 步骤机"，UIApp 基本不改。
- 设置页加模式选择：热座（现状）/ 联机（建房·加房）。

## 7. 验收（Phase 4 门）

1. 服务端自测脚本：3 个模拟客户端跑通一整局（含刺杀），断言与 core.js 直跑结果一致。
2. 云服务器部署后，两台真机联机完成一局 5 人局。
3. 抓包验证：任何客户端收不到非自己视角的身份字段。
