# 阿瓦隆（Avalon）数字版

《The Resistance: Avalon》桌游数字化 · 安卓端。

| 目录 | 内容 | 状态 |
|---|---|---|
| `avalon-preview/` | 游戏（H5）：规则引擎 `core.js` + 热座界面，`avalon-standalone.html` 为单文件分发版 | ✅ 可玩（2700 局模拟验证） |
| `avalon-android/` | 安卓工程（原生壳 + WebView，零第三方依赖，minSdk 24） | ✅ 已产出 APK |
| `release/` | 安装包：`avalon-v0.1.0-debug.apk`（34 KB） | ✅ |
| `docs/` | `GDD.md`（规则基线与来源）、`TECH_DECISIONS.md`（技术决策） | ✅ |
| `_tools/`、`android-sdk/` | 本机构建工具链（Gradle 8.9 / SDK 34，不入库） | 本地 |

## 快速开始

**手机安装（推荐）**：把 `release/avalon-v0.1.0-debug.apk` 发到安卓手机（微信/QB/数据线均可），
点击安装（需允许"安装未知来源应用"），桌面出现金色剑徽"阿瓦隆"图标。

**电脑上游玩**：双击 `avalon-preview/avalon-standalone.html`，或：

```bash
python -m http.server 8000   # 访问 http://localhost:8000/
```

**重新构建 APK**：`python build_apk.py`（自动同步 avalon-preview → assets → Gradle → release/）

## 玩法

5–10 人一台设备轮流传递（热座）。查看身份 → 队长组队 → 全员投票 → 队员秘密出任务票 →
3 胜后刺杀梅林。规则细节见 `docs/GDD.md`（按官方规则书核实）。

## 开发路线

按《阿瓦隆安卓版-Agent开发提示词》Phase 0–6 推进，当前进度：

- ✅ **Phase 1（规则引擎）**：`avalon-preview/core.js`，测试 `test/simulate.js`
- ✅ **Phase 2（热座界面 + APK）**：H5 全流程 + 安卓壳（当前 APK 即本阶段交付物）
- ⏸ **Phase 0 收尾 / Unity 正式版**：Unity 本机未安装（见 docs/TECH_DECISIONS.md D1/D2），就绪后移植 C# Core 并接续联机（Phase 4）
- ⬜ Phase 3 美术音频 / Phase 5 打磨 / Phase 6 上架

## 合规提示

《阿瓦隆》为 Indie Boards & Cards 发行之版权桌游。本仓库仅用于**学习与个人使用**；
商业发布前需取得授权或改为原创主题（见 docs/GDD.md 合规章节）。
