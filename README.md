# Beauty Or Death (不美丽，毋宁死) 🌲🧙‍♀️🔫

[![Unity Version](https://img.shields.io/badge/Unity-2022.3.55f1c1-blue.svg)](https://unity.com/)
[![Networking](https://img.shields.io/badge/Network-Mirror-orange.svg)](https://mirror-networking.com/)
[![Genre](https://img.shields.io/badge/Genre-Asymmetric%20PVP-red.svg)](#)

**2025-2026 软件设计比赛参赛作品**  
《Beauty Or Death》是一款基于 Unity 开发的 **3D 非对称对抗联机游戏**。游戏通过“变身躲藏”与“战术博弈”的结合，呈现了一场关于魔法、贪婪与守护的林中战争。

---

## 📜 背景故事 (Backstory)

森林深处栖息着拥有千年修为的**古树**，其蕴含的“日月精华”是炼制**返老还童药水**的核心材料。
*   **女巫阵营**：曾是容颜枯槁的老巫婆，为了重获青春，她们潜入秘境企图盗走古树。
*   **猎人阵营**：受家族使命驱使，代代守护森林。对他们而言，每一棵树都是不容侵犯的至宝。

这场对决，是女巫对美丽的病态追求，还是猎人对自然意志的终极守护？**“要么重获美丽，要么死在林中。”**

---

## 🎮 游戏规则 (Rules)

*   **建议人数**：3人及以上（推荐比例：猎人 30%，女巫 70%）。
*   **比赛限时**：3 - 5 分钟。
*   **胜利条件**：
    *   **女巫胜利**：每名存活女巫均成功盗取一棵古树并集结于传送门，共同启动撤离。
    *   **猎人胜利**：在时限内成功阻止女巫撤离，或击杀/抓捕所有女巫。
*   **复活机制**：女巫被击杀后会化身为脆弱的**青蛙**，需在无敌期内逃往传送门复活，否则将永久出局。

---

## ✨ 核心功能 (Key Features)

### 1. 深度变身系统 (Advanced Morphing)
*   **外观拟态**：女巫通过射线检测，可瞬间获取场景道具的网格（Mesh）与材质（Material）。
*   **动画继承**：当变身为动物（鸡、鹿等）时，系统会动态生成视觉实例并**继承其 Animator 状态机**，使变身后仍具备行走/奔跑动画。
*   **属性同步**：变身后继承目标物体的移动速度。
*   **动态碰撞适配**：根据变身目标的大小，实时调整 `CharacterController` 的高度与半径。

### 2. 阵营与网络同步
*   **分布式权威控制**：基于 Mirror 框架，移动由本地预测，状态由服务器权威验证。
*   **延迟补偿动画**：针对远程镜像玩家（Proxy），系统通过计算 **位置位移差（Position Delta）** 实时推算移动速度，完美解决了 Mirror 中 `velocity` 不回传导致的动画卡顿问题。
*   **动态分队出生点**：`GameManager` 根据玩家阵营自动在 `WitchSpawnPoints` 与 `HunterSpawnPoints` 组中随机分配起始位置。

### 3. 视觉与社交交互
*   **阵营视觉系统 (Team Vision)**：基于边缘着色（Shader Outline）技术，队友间可隔墙可见高亮轮廓（女巫紫色，猎人青色）。
*   **社交系统**：完善的大厅准备系统，局内集成 [ALL] 广播频道与 [TEAM] 阵营私密频道。
*   **双视角切换**：支持第一人称（FPS）代入感与第三人称（TPS）观察视角。

---

## 🛠️ 技术架构 (Technical Architecture)

#### 🎮 玩家对象模型
| 类名 | 继承/职责 | 核心逻辑 |
| :--- | :--- | :--- |
| **PlayerScript** | NetworkBehaviour | **[大厅阶段]** 准备状态同步、实时改名、大厅聊天。 |
| **GamePlayer** | Abstract Class | **[基础框架]** 处理移动、HP/MP 同步、视角控制及聊天拦截。 |
| **WitchPlayer** | : GamePlayer | **[变身核心]** 处理 `CmdMorph`、`PropContainer` 实例化及速度同步。 |
| **HunterPlayer** | : GamePlayer | **[战斗核心]** 处理武器攻击判定及猎人专属技能。 |

#### ⚙️ 系统管理器
*   **GameManager**: 持久化单例，负责 `SyncVar` 游戏倒计时、预分配角色字典及中途加入处理。
*   **MyNetworkManager**: 扩展 Mirror 核心，控制场景无缝切换及 `ReplacePlayerForConnection` 逻辑。
*   **PropDatabase**: 变身资产库，通过 `propID` 映射 Prefab，提供网格提取与实例生成接口。
*   **CreatureMover (Controller)**: 驱动动物逻辑，提供标准化的 `Vert` 与 `State` 动画接口。

---

## ⌨️ 操作指南 (Controls)

| 按键 | 功能 |
| :--- | :--- |
| **W / A / S / D** | 角色移动 |
| **Shift** | 奔跑（变身后继承动物奔跑速度与动画） |
| **LMB (左键)** | **女巫**: 变身目标 (短按) / 恢复真身 (长按) <br> **猎人**: 武器开火 |
| **T** | 切换 第一人称 (FPS) / 第三人称 (TPS) 视角 |
| **/** | 开启聊天框 |
| **Tab** | (聊天开启时) 切换 全局/队伍 频道 |
| **ESC** | 关闭聊天框 / 呼出系统暂停菜单 |

---

linux系统挂载server方式：
sudo firewall-cmd --zone=public --add-port=7777/udp --permanent
sudo firewall-cmd --reload
killall -9 build_linux.x86_64
rm -rf build_linux_Data 
chmod +x build_linux.x86_64   
nohup ./build_linux.x86_64 -batchmode -nographics > server_log.txt 2>&1 &
