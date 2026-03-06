# Beauty Or Death (不美丽，毋宁死) 🌲🧙‍♀️🔫

[![Unity Version](https://img.shields.io/badge/Unity-2022.3.55f1c1-blue.svg)](https://unity.com/)
[![Networking](https://img.shields.io/badge/Network-Mirror-orange.svg)](https://mirror-networking.com/)
[![Genre](https://img.shields.io/badge/Genre-3D%20Asymmetric%20PVP-red.svg)](#)
[![Status](https://img.shields.io/badge/Status-Active%20Development-brightgreen.svg)](#)

**2025-2026 软件设计比赛参赛作品**  
《Beauty Or Death》是一款基于 Unity 与 Mirror 框架开发的 **3D 非对称多人物理对抗游戏**。它巧妙地结合了“Prop Hunt（躲猫猫）”的拟态隐藏机制与类似“Dead by Daylight（黎明杀机）”的战术逃生/追捕博弈，为您呈现一场关于魔法、贪婪与守护的林中战争。

---

## 📜 背景故事 (Backstory)

森林深处栖息着拥有千年修为的**古树**，其蕴含的“日月精华”是炼制**返老还童药水**的核心材料。
*   **🧙‍♀️ 女巫阵营**：曾是容颜枯槁的老巫婆，为了重获青春，她们潜入秘境，企图附身并盗走古树。
*   **🔫 猎人阵营**：受家族使命驱使，代代守护森林。对他们而言，每一棵树都是不容侵犯的至宝，任何盗窃者都必须被处决。

这场对决，是女巫对美丽的病态追求，还是猎人对自然意志的终极守护？
**“要么重获美丽，要么死在林中。”**

---

## 🎮 核心玩法与阵营特色 (Gameplay & Roles)

### 🧙‍♀️ 女巫阵营 (Witches)
**目标**：寻找地图上的“古树”，附身并将其运送至**复活传送门 (Portal)**。所有存活女巫完成目标即可获胜！
*   **万物拟态**：短按左键可瞬间变身成地图上的普通树木、石头或小动物（继承动物的奔跑动画与速度），完美融入环境。
*   **多人共乘 (Co-op Driving)**：右键附身并连根拔起巨大的“古树”。古树沉重且行动缓慢，但**其他女巫可以作为“乘客”跳上古树**，汇聚方向键输入，合力“飙树”冲向传送门！
*   **Roguelite 局内强化**：在躲避猎人的同时“侦察”森林中的树木，积累进度后可获得三选一的随机赐福（如最大生命值提升、技能范围翻倍、透视猎人视野等）。
*   **复活赛机制**：首次被击杀后会化身为脆弱的**小青蛙**，获得短暂无敌。只要能极限逃生跳入传送门，就能满血满状态复活！

### 🔫 猎人阵营 (Hunters)
**目标**：在倒计时结束前，利用武器和技能猎杀所有女巫，或阻止她们运走足够的古树。
*   **冷热兵器交替**：装备猎枪（远程点射）、拳头（近战硬直）与兜网发射器（禁锢女巫）。
*   **残酷处决**：当女巫被陷阱或兜网困住时，猎人可靠近按下 `F` 键执行高伤害的残忍处决。
*   **硬核追踪**：通过释放猎犬寻迹、利用声波扫描女巫经过的残影脚印，不放过任何蛛丝马迹。

---

## ✨ 系统级亮点功能 (Key Systems)

### 1. 深度定制的技能与道具池
游戏在大厅阶段提供战前 Build 搭配：
*   **女巫道具 (Items)**：魔法扫帚（解锁二段跳）、隐身斗篷（大幅加速+隐形）、生命护符（抵挡一次致命伤并触发无敌）。
*   **女巫法术 (Skills)**：致盲迷雾 (Mist)、幻影分身 (Decoy)、树木诅咒 (Curse)、混乱震荡 (Chaos)。
*   **猎人战技 (Skills)**：召唤猎犬追踪 (Dog)、足迹/鬼影扫描 (Scan)、捕熊陷阱 (Trap)、范围震地减速 (Shockwave)。

### 2. 健壮的网络大厅与动态房间系统
*   **多房间子进程架构**：基于 `LobbyServer.cs` 实现。主服务器作为大厅，当玩家创建房间时，会自动分配空闲端口并**拉起一个独立的子进程 (MyGameServer.exe)** 运行该对局，互不干扰，进程结束自动回收资源。
*   **大厅深度自定义**：房主可实时拉动 UI 滑杆调节**局内变量**（如：猎人移动速度、女巫血量、友军伤害开启、陷阱挣脱难度、阵营分配比例等），参数通过 `SyncVar` 实时全网同步。

### 3. 高级视觉与物理同步
*   **延迟补偿与动画推算**：针对 Mirror 中高延迟导致的动画卡顿，系统通过比对镜像玩家（Proxy）的 Position Delta，实时推算水平速度，平滑驱动 Animator。
*   **动态碰撞体适配**：女巫变身为任意 Prop（如细长的树或低矮的青蛙）时，系统会自动重算 `CharacterController` 的 `height` 与 `radius`，防止卡地/穿模。
*   **阵营透视系统 (Team Vision)**：队友之间通过 Shader Outline 隔墙可见（紫/青色）；若队友被猎人兜网抓住，描边会瞬间变为**红色警报**。

### 4. 沉浸式结算舞台 (Victory Zone)
*   对局结束后无缝切入颁奖舞台场景。
*   胜利阵营的角色将面向镜头播放专属庆祝群舞（附带专属 BGM），失败阵营则在背景中低头播放失落动画。胜负者站位由代码动态生成排布。

---

## ⌨️ 操作指南 (Controls)

| 按键 | 功能描述 |
| :--- | :--- |
| **W / A / S / D** | 角色移动 |
| **Shift (按住)** | 奔跑（变身后继承动物的奔跑速度） |
| **Space (空格)** | 跳跃 / （被陷阱抓住时）连按挣脱 / （乘客状态）跳车 |
| **LMB (左键)** | **女巫**: 短按变身准星物体，长按恢复人形 <br> **猎人**: 武器开火 |
| **RMB (右键)** | **女巫**: 长按附身“古树”成为驾驶员 |
| **Q / E** | 释放战前选择的 [技能1] 与 [技能2] |
| **F** | **女巫**: 激活战前选择的[特殊道具] <br> **猎人**: 对被困女巫执行 [处决] |
| **1 / 2 / 3 或 滚轮** | **猎人**: 切换 猎枪 / 兜网 / 拳头 |
| **T** | 切换 第一人称 (FPS) / 第三人称 (TPS) 视角 |
| **Tab** | 查看对局玩家列表与延迟 (Ping) / 聊天时切换频道 |
| **/** 和 **Enter** | 开启/发送 局内聊天（支持 ALL/TEAM 频道） |

---

## 🛠️ 技术架构地图 (Architecture Map)

为方便二次开发，核心脚本按职责划分如下：

*   `Core/`
    *   `GameManager.cs` - 全局状态机，掌管游戏时间、古树交付进度判定、胜负结算与跨场景数据传递。
    *   `MyNetworkManager.cs` - 继承自 Mirror 的网络管家，处理玩家的连接、断线以及子进程的启动匹配。
*   `Player/`
    *   `GamePlayer.cs` - 抽象基类，封装物理移动计算（保留惯性手感）、血量/蓝量同步、聊天与伤害处理。
    *   `WitchPlayer.cs` - 女巫核心，囊括了动态 Mesh 获取、`hostNetId` 乘客系统以及 Roguelite 奖励逻辑。
    *   `HunterPlayer.cs` - 猎人核心，管理武器切换、攻击动作帧事件回调（Animation Event）及处决逻辑。
*   `Skills/` & `Props/`
    *   `SkillBase.cs` / `WitchItemBase.cs` - 标准化技能/道具基类，自带冷却换算与 `Command` 通信封装。
    *   `PropTarget.cs` - 挂载于场景万物，提供材质高亮、动态隐藏、侦察标记状态同步。
*   `Lobby/`
    *   `LobbyServer.cs` - 监听客户端建房请求，使用 `Process.Start` 动态调度服务器端口集群。

---

## 🚀 部署与开发运行 (Deployment & Setup)

### 📌 本地开发调试
1.  安装 **Unity 2022.3.x** 或以上版本。
2.  克隆本仓库并在 Unity Hub 中打开。
3.  在 Build Settings 中确保包含场景顺序：`StartMenu` -> `ConnectRoom` -> `LobbyRoom` -> `MyScene`。
4.  在编辑器中运行 `StartMenu` 场景，选择 `Localhost` 即可作为 Host 体验。

### 🐧 Linux 独立服务器部署 (Dedicated Server)
游戏采用 Room-Instanced（一房一进程）架构，需在 Linux 上开放一段端口范围（例如 7771-7780）供子进程使用。

```bash
记得在inspector中修改LobbyServer的public string publicIP = "你的公网IP";(本机测试用 127.0.0.1)
# 1. 开放所需端口 (根据实际 LobbyServer 配置修改范围)
sudo firewall-cmd --zone=public --add-port=7771-7780/udp --permanent
sudo firewall-cmd --reload

# 2. 清理旧进程与数据
killall -9 build_linux.x86_64
rm -rf build_linux_Data 

# 3. 赋予执行权限并后台启动主大厅服务器进程
chmod +x build_linux.x86_64   
ln -s build_linux.x86_64 MyGameServer.exe
# 确保这个链接文件也有执行权限
chmod +x MyGameServer.exe
chmod -R 755 /unity/
or chmod -R 777 /unity/
nohup ./build_linux.x86_64 -batchmode -nographics > server_log.txt 2>&1 &

# 此时玩家连接大厅并点击“创建房间”后，主进程会自动派生携带 -port 参数的子游戏进程
```
[本地开发启动方式](游戏启动方法.md)
