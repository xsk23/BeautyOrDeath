# BeautyOrDeath

🎮 **2526 软设比赛**  
基于 Unity + Mirror 的多人联机非对称对抗游戏。

## 📖 项目简介
本项目是一个基于局域网/广域网的多人联机游戏。玩家在 **LobbyRoom (大厅)** 进行集结、聊天和准备，随后跳转至游戏场景并随机分配身份（女巫或猎人）进行对抗。

## 🏗️ 核心架构 (Architecture)

本项目采用 **大厅/游戏分离 (Lobby-Game Separation)** 的架构设计，以确保网络同步的稳定性和逻辑的解耦。

### 1. 角色系统 (Class Hierarchy)
为了区分大厅交互与游戏内战斗逻辑，我们在不同场景使用了不同的玩家对象：

*   **大厅阶段 (Lobby Phase):**
    *   `PlayerScript`: 负责大厅内的交互逻辑。
        *   功能：玩家准备 (Ready)、更改名字、大厅聊天、同步房间状态。
        *   生命周期：仅存在于 `LobbyRoom` 场景。

*   **游戏阶段 (Game Phase):**
    *   `GamePlayer` **(Abstract Base Class)**: 游戏内角色的抽象基类。
        *   功能：核心移动控制 (CharacterController)、生命值同步、通用攻击接口、摄像机控制。
    *   `WitchPlayer` (继承自 `GamePlayer`):
        *   职业：**女巫**
        *   技能：投掷毒药 (Throw Poison)
    *   `HunterPlayer` (继承自 `GamePlayer`):
        *   职业：**猎人**
        *   技能：射击 (Shoot Gun)

### 2. 核心管理器
*   **GameManager**: 单例管理器。负责在场景切换间隙保存玩家数据（名字、预分配的角色），并在进入游戏场景后负责**角色生成与替换 (Spawn & Replace)**。
*   **MyNetworkManager**: 扩展自 Mirror 的 NetworkManager，处理跨场景的逻辑判断和断线重连。

## 🛠️ 技术栈
*   **引擎**: Unity 2022.3.55f1c1
*   **网络框架**: Mirror (Networking)
*   **UI**: UGUI + TextMeshPro (TMP)


## 📝 开发日志
*   [x] 完成大厅聊天与准备系统。
*   [x] 完成跨场景角色数据保留。
*   [x] 进入游戏时女巫与猎人的职业分配。
*   [x] Linux Dedicated Server 部署支持。

