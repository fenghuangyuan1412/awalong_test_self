# 技术决策记录（TECH_DECISIONS）

> 按《阿瓦隆安卓版-Agent开发提示词》要求，记录关键选型与理由。决策随阶段更新，旧决策标注状态。

## D1 · 预览版与安卓壳技术栈（2026-09-19，已采纳）

**决策**：预览版采用「纯 JS 规则引擎 + 移动端 H5 界面」；安卓端采用「原生 Android 壳（Java，零第三方依赖）+ WebView 承载已验证的 H5 游戏」，产出可直接安装的 APK。

**背景**：提示词的环境前提是"本机已安装 Unity 与 Android 构建模块"。实测本机（2026-09-19）：

| 组件 | 提示词前提 | 实际环境 |
|---|---|---|
| Unity 2022.3 LTS / Unity 6 + Android 模块 | 已安装 | **未安装**（安装需数 GB 下载 + 交互式 Unity 账号激活，无法自动化） |
| JDK 17+ | 需要 | ✅ JDK 19（D:\programme\java） |
| Android SDK / Gradle | 需要 | ❌ 初始未装（本次已通过命令行装齐） |

**备选对比**：

| 方案 | 优点 | 缺点 | 结论 |
|---|---|---|---|
| A. 立即装 Unity 走原路线 | 与提示词完全一致 | 数 GB 下载 + 许可激活必须人工交互；当天无法产出可玩 App | 暂缓（见 D2） |
| B. 原生壳 + WebView 承载已验证 H5 | 当天产出真 APK；游戏逻辑零改动复用已测代码；包体 <2MB；零第三方依赖 | 界面为 H5（性能/原生感弱于原生 UI） | **采纳（第一步）** |
| C. Kotlin/Compose 原生重写 | 完全原生体验 | 与提示词指定的 Unity 栈不符；重写全部 UI 与引擎移植，周期长 | 不采纳 |

**对提示词架构约束的遵守**：
- 规则与界面分离：`core.js`（纯逻辑、零 DOM、数据驱动）↔ 计划中的 C# Core 层同构，移植成本低
- 数据驱动：角色表/任务表全部集中在数据对象，禁止流程代码硬编码规则数字
- minSdk 24 ✅；完全离线、不申请任何权限（隐私合规天然满足）

## D2 · 正式版引擎路线（状态：已落地，2026-09-19）

**决策**：正式版按提示词采用 Unity（团结引擎 1.6.13 = Unity 2022.3.61t14）+ C#。

**环境事实（本机验证）**：
1. 编辑器：`D:\ai\Editor\Editor\Tuanjie.exe`（注：外层 `D:\ai\Editor` 为早期不完整安装，缺包管理器 Server，勿用）
2. 许可：Cowork 账号内的 "Unity Personal" 许可证（2026-09-19 激活，到期自动续），batchmode 可用
3. Android 工具链：AndroidPlayer（含 NDK r23b / OpenJDK 11，已展平目录）+ 工作区 `android-sdk`（build-tools 34 / platform 34）
4. 已知坑：`cmdline-tools` 的 sdkmanager 需 Java 17+，已在 `android-sdk/cmdline-tools/latest/bin/sdkmanager.bat` 内固定 `JAVA_HOME=D:\programme\java`；
   Gradle 依赖走 `~/.gradle/init.d/mirror.gradle` 阿里云镜像，否则海外源超时
5. 语言级别 C# 9（勿用 C# 10 的对象初始化器冒号语法）

**代码组织**：`_unity_staging/`（入库单一事实来源）→ `sync_unity.py` → `avalon-unity/`（引擎工程，Library 等生成物不入库）。
构建命令：`Tuanjie.exe -batchmode -executeMethod Avalon.EditorTools.BuildScript.BuildAndroid`。

**当前 APK 与正式版的关系**：`avalon-preview` 的 H5 APK（v0.2.0）为热座玩法先行验证；
Unity 版 `release/avalon-unity-debug.apk` 为 Phase 2 交付物，热座交互设计（传递确认页、双轨道、私密出票）已 1:1 移植。

## D3 · 联机方案（状态：沿用提示词，Phase 4 前最终确认）

首选 Unity Netcode for GameObjects + UGS 免费层；备选 Photon Fusion（回合制更友好）。
若届时仍采用原生路线，则改用自建 WebSocket 房主权威方案——Phase 4 启动前出对比文档。

## D5 · 美术方案（2026-09-19，已采纳，Phase 3 首批）

**决策**：界面美术采用**原创 SVG 矢量资产**（`avalon-preview/art.js`）：金色线稿徽章 + 队伍色渐变底盘。

**背景**：提示词推荐 Meowa 技能生成位图资产，但其依赖 `MEOWART_API_KEY`，当前环境未配置。
对比：

| 方案 | 优点 | 缺点 | 结论 |
|---|---|---|---|
| Meowa AI 位图生成 | 表现力强 | 需要 API Key（未配置）；位图需多分辨率切图、包体增量 | 暂缓（Key 就绪后可为角色生成立绘插画） |
| **原创 SVG 矢量** | 零依赖、可无损缩放、包体增量≈0、风格绝对统一、无版权风险 | 细腻度低于手绘插画 | **采纳（当前批次）** |

**首批资产清单**（全部原创，来源：本项目手工绘制）：
- 角色徽章立绘 ×8：梅林（法帽星辉）/ 派西维尔（鸢盾）/ 忠臣（交叉双剑）/ 刺客（短刃血滴）/
  莫甘娜（新月）/ 莫德雷德（骷髅）/ 奥伯伦（之眼）/ 爪牙（爪痕）
- UI 图标 ×4：皇冠（队长）/ 文档（日志）/ 书本（规则）/ 信息（关于）
- 贴图 ×2：圆桌纹理背景（桌面页）、金色四角花纹（身份卡边框）

**替换路径**：`AvalonArt.roleIcon(roleId, size)` 为唯一入口；将来接入 AI 插画时，
按同尺寸位图替换各角色的 `EMBLEMS` 实现即可，界面层零改动。

## D4 · 构建工具链（已采纳）

- Gradle 8.9 + AGP 8.5.2 + compileSdk 34 + Build-Tools 34.0.0（命令行构建，不依赖 Android Studio）
- SDK/Gradle 安装于工作区 `_tools/`、`android-sdk/`（gitignore，不入库）
- 构建：`python build_apk.py`（同步 assets → 调 Gradle → 产物复制到 `release/`）
