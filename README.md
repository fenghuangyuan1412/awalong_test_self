# 阿瓦隆（Avalon）数字版

《The Resistance: Avalon》桌游数字化 · 安卓端。

| 目录 | 内容 | 状态 |
|---|---|---|
| `avalon-preview/` | 游戏（H5）：规则引擎 `core.js` + 矢量美术 `art.js` + 热座界面，`avalon-standalone.html` 为单文件分发版 | ✅ 可玩（2700 局模拟验证） |
| `avalon-android/` | 安卓工程（原生壳 + WebView，零第三方依赖，minSdk 24） | ✅ 已产出 APK |
| `release/` | 安装包：`avalon-v0.2.0-debug.apk`（38 KB） | ✅ |
| `docs/` | `GDD.md`（规则基线+房规记录）、`TECH_DECISIONS.md`（技术/美术决策） | ✅ |
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

**v0.2.0 更新**：
- 房规：队长组队时可以不选自己（官方规则要求队长在队内；已记录于 GDD「房规记录」，正式版将做成开关）
- 美术：全部角色换上原创矢量徽章立绘（8 角色 + UI 图标 + 圆桌纹理/卡框角饰），风格统一、包体近零

## 开发路线

按《阿瓦隆安卓版-Agent开发提示词》Phase 0–6 推进，当前进度：

- ✅ **Phase 1（规则引擎）**：`avalon-preview/core.js`，测试 `test/simulate.js`
- ✅ **Phase 2（热座界面 + APK）**：H5 全流程 + 安卓壳
- 🟨 **Phase 3（美术音频）**：首批原创矢量立绘/贴图已上线（`art.js`）；音频与 AI 插画待 API Key 就绪
- ⏸ **Phase 4 联机**：下一阶段（方案对比见 TECH_DECISIONS D3）
- ⬜ Phase 5 打磨 / Phase 6 上架；Unity 正式版见 TECH_DECISIONS D1/D2

## 合规提示

《阿瓦隆》为 Indie Boards & Cards 发行之版权桌游。本仓库仅用于**学习与个人使用**；
商业发布前需取得授权或改为原创主题（见 docs/GDD.md 合规章节）。
