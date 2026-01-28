# Beauty Or Death🌲🧙‍♀️🔫

[![Unity Version](https://img.shields.io/badge/Unity-2022.3.55f1c1-blue.svg)](https://unity.com/)
[![Networking](https://img.shields.io/badge/Network-Mirror-orange.svg)](https://mirror-networking.com/)
[![Genre](https://img.shields.io/badge/Genre-Asymmetric%20PVP-red.svg)](#)

**2025-2026 软件设计比赛参赛作品**  



## 📜 背景故事

森林深处栖息着拥有千年修为的**古树**，其蕴含的“日月精华”是炼制**返老还童药水**的核心材料。
*   **女巫阵营**：曾是容颜枯槁的老巫婆，为了重获青春，她们潜入秘境企图盗走古树。
*   **猎人阵营**：受家族使命驱使，代代守护森林。对他们而言，每一棵树都是不容侵犯的至宝。

这场对决，是女巫对美丽的病态追求，还是猎人对自然意志的终极守护？**“要么重获美丽，要么死在林中。”**



## 🎮 游戏规则

*   **建议人数**：3人及以上（推荐比例：猎人 30%，女巫 70%）。
*   **比赛限时**：3 - 5 分钟。
*   **胜利条件**：
    *   **女巫胜利**：每名存活女巫均成功盗取一棵古树并集结于传送门，共同启动撤离。
    *   **猎人胜利**：在时限内成功阻止女巫撤离，或击杀/抓捕所有女巫。
*   **复活机制**：女巫被击杀后会化身为脆弱的**青蛙**，需逃往传送门复活，此时被击杀将永久出局。


## ✨ 核心功能

### 1. 核心玩法逻辑
*   **变身系统 (Morph System)**：女巫通过射线检测物体，可瞬间获取目标物体的网格（Mesh）与材质（Material）完成伪装，并动态适配碰撞盒。支持长按左键恢复真身。
*   **非对称竞技**：针对不同阵营设计了差异化的移动速度、体力回复速度以及技能逻辑（女巫侧重隐匿，猎人侧重搜寻）。
*   **阵营视觉系统 (Team Vision)**：
    *   **队友高亮**：基于 Shader 的 Outline 描边技术，队友之间可隔墙可见（紫色代表女巫，青色代表猎人）。
    *   **伪装遮掩**：女巫变身后，其头顶 UI 自动隐藏，确保伪装的真实性。

### 2. 联机大厅与网络同步
*   **动态准备系统**：实时同步玩家准备状态，支持房主端全员就绪后的自动倒计时（5s）与场景无缝切换。
*   **玩家身份持久化**：支持在大厅内实时修改昵称，利用 `PlayerSettings` 跨场景持久化玩家数据。
*   **中途加入 (Late Join)**：服务器自动判断游戏状态，对中途加入的玩家执行身份分配策略（默认为猎人）。

### 3. 社交与交互
*   **分频道聊天系统**：
    *   **[ALL] 频道**：全体玩家可见。
    *   **[TEAM] 频道**：利用网络标签实现仅同阵营可见的战略交流。
*   **多视角切换**：支持第一人称 (FPS) 的代入感与第三人称 (TPS) 的环境观察视角自由切换。



## 🛠️ 技术架构

本项目采用**分布式逻辑控制**，确保服务器权威（Server Authoritative）的同时保证客户端流畅体验。

#### 🎮 玩家对象
| 类名 | 继承关系 | 职责 |
| :--- | :--- | :--- |
| **PlayerScript** | NetworkBehaviour | **[大厅专用]** 处理准备状态、改名请求、大厅聊天。 |
| **GamePlayer** | NetworkBehaviour (Abstract) | **[游戏专用]** 抽象基类。处理 CharacterController 移动、HP/MP 同步、视角控制、聊天输入拦截。 |
| **WitchPlayer** | : GamePlayer | 实现女巫特有的 `Attack` 逻辑与属性配置。 |
| **HunterPlayer** | : GamePlayer | 实现猎人特有的 `Attack` 逻辑与属性配置。 |

#### ⚙️ 管理器
*   **GameManager (NetworkBehaviour)**: 
    *   **全生命周期管理**: 由 `NetworkManager` 在服务器启动时生成，跨场景不销毁。
    *   **倒计时**: 维护 `gameTimer` SyncVar。
    *   **角色生成**: 负责在 `SpawnPlayerForConnection` 中根据 `pendingRoles` 生成对应的 Prefab，并处理中途加入的兜底逻辑。
*   **MyNetworkManager**: 
    *   扩展自 Mirror，负责连接流程控制。
    *   判断当前场景，决定是生成大厅玩家还是游戏角色。
*   **SceneScript**: 
    *   **[客户端 UI]** 负责游戏内的 HUD 更新 (血条、蓝条、倒计时、目标文本) 及暂停菜单逻辑。



## ⌨️ 操作指南

| 按键 | 功能 |
| :--- | :--- |
| **W / A / S / D** | 角色移动 |
| **LMB (左键)** | **女巫**: 变身目标物体 (短按) / 恢复真身 (长按) <br> **猎人**: 开火射击 |
| **T** | 切换 第一人称 / 第三人称 视角 |
| **/** | 开启聊天框 |
| **Tab** | (聊天开启时) 切换 全局/队伍 频道 |
| **ESC** | 关闭聊天框 / 呼出暂停菜单 |



## 🚀 快速开始

1.  **克隆仓库**:
    ```bash
    git clone https://github.com/YourUsername/BeautyOrDeath.git
    ```
2.  **环境配置**:
    *   使用 Unity 2022.3.55f1c1 版本打开。
    *   确保已导入 **Mirror Networking** 插件。
3.  **构建方案**:
    *   打开 `StartMenu` 场景。
    *   在 `NetworkManager` 组建中设置目标服务器 IP（本地测试请使用 `localhost`）。
    *   build一个dedicated server并运行，启动编辑器join即可



