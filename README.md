# TableGameRoom-Android · TABLE 虚拟美元游戏厅（安卓版）

> **本项目全部由 AI 制作。** 从架构设计、UI 布局、游戏规则实现到构建发布，代码与文档均由 AI 编写，人类仅提出需求与验收。

Unity 安卓端赌场小游戏合集，**完全离线、无数据库、不联网、不涉及真实货币**，所有筹码均为虚拟筹码。

## 玩法

| 玩法 | 说明 |
| --- | --- |
| 🎡 欧洲轮盘 | 单零轮盘 37 个号码，内圈颜色投注 + 外圈号码环，倒三角指针，螺旋落球动画 |
| 🃏 21 点 | 要牌 / 停牌 / 双倍下注，A 的 1/11 计算，庄家 17 点停牌 |
| ♠ 德州扑克 | 翻牌前 → 翻牌 → 转牌 → 河牌四街下注，跟注 / 加注 / 弃牌，摊牌比牌型 |
| 🌸 炸金花 | 三张牌，看牌 / 跟注 / 加注 / 比牌 / 弃牌 |
| 🎭 骗子扑克 | 轮流盖牌声明点数，随时可以质疑；质疑成功对方收牌，失败自己收牌 |

## 特性

- **两列大厅布局** —— 5 种玩法一屏放下，无需滚动
- **普通模式 / 无限模式** —— 两套独立存档，普通模式从 $1,000 起步
- **七档 AI 强度** —— 从休闲到最强平滑过渡，AI 思考时长随难度与下注额变化
- **任务中心** —— 做任务赚奖金，破产时靠任务翻身
- **历史记录** —— 按日期查看每一局的输赢

## 技术栈

- **Unity 6000.3.27f1 LTS**，C# 10，IL2CPP，ARMv7 + ARM64，竖屏 1080×1920
- **纯代码 UGUI** —— 不使用任何 LayoutGroup / ScrollRect，全部手写排布
  - `UI/VStack.cs` —— 竖直堆叠容器（`LayoutGroup` 嵌套 `ContentSizeFitter` 会塌陷高度，故自写）
  - `UI/SimpleScroll.cs` —— 自写滚动容器（`ScrollRect.verticalNormalizedPosition` 在内容高度变化的那几帧会算错，导致页面初始停在中间）
  - `UI/DragPassThrough.cs` —— 把拖拽事件转发给外层滚动容器
- **零第三方依赖** —— 不引入任何插件或包

### 为什么自己写序列化

Unity 的 `JsonUtility` 不支持 `Dictionary`，也不支持顶层 `List`，而 `System.Text.Json` 在 Unity 的 .NET Standard 2.1 下不可用。因此：

- `Core/Json.cs` —— `JsonUtility` 包装层，补上 `List` 容器与 `Dictionary` 的文本编解码
- `Core/WireFormat.cs` —— 联机协议的行式文本格式（`key=value`，点号表示嵌套）

## 目录

```
TableGameRoomMobile/
├── Assets/
│   ├── csc.rsp                     # -langversion:10.0
│   └── Scripts/
│       ├── App.cs                  # 单例：模式、存档、页面路由
│       ├── Bootstrap.cs            # 启动入口，建 Canvas / EventSystem
│       ├── Core/                   # 纯逻辑层（无 UnityEngine 依赖）
│       │   ├── Cards.cs            # 牌与牌堆
│       │   ├── FastPokerScore.cs   # 高性能 5/7 张牌评估器
│       │   ├── AiTuning.cs         # 七档强度调参
│       │   ├── ThinkTiming.cs      # 人机思考时长
│       │   ├── TaskSystem.cs       # 任务系统
│       │   ├── SaveStore.cs        # 存档读写
│       │   ├── Json.cs             # 序列化
│       │   └── WireFormat.cs       # 联机协议编解码
│       ├── Net/
│       │   └── LanNet.cs           # TCP 服务端/客户端 + UDP 广播发现
│       ├── UI/                     # 界面层
│       │   ├── Theme.cs            # 配色 / 尺寸 / 字号
│       │   ├── UiFactory.cs        # 控件工厂
│       │   ├── VStack.cs           # 竖直堆叠
│       │   ├── SimpleScroll.cs     # 滚动容器
│       │   ├── DragPassThrough.cs  # 拖拽转发
│       │   ├── Dialog.cs           # 弹窗
│       │   ├── ModeSelectPage.cs   # 模式选择
│       │   ├── LobbyPage.cs        # 玩法大厅（两列卡片）
│       │   ├── TasksPage.cs        # 任务中心
│       │   └── HistoryPage.cs      # 历史记录
│       ├── Games/                  # 各玩法
│       │   ├── RoulettePage.cs
│       │   ├── BlackjackPage.cs
│       │   ├── PokerPage.cs
│       │   ├── ThreePage.cs
│       │   ├── LiarPage.cs
│       │   ├── PokerAiEngine.cs
│       │   └── ThreeAiEngine.cs
│       └── Editor/
│           └── BuildScript.cs      # 命令行构建入口
├── Packages/
└── ProjectSettings/
```

## 构建

### 环境要求

- Unity 6000.3.27f1 LTS（含 Android Build Support、SDK/NDK/JDK）
- 环境变量 `TABLE_ANDROID_TOOLCHAIN` 指向工具链根目录（下含 `sdk/`、`ndk/`、`jdk/`）

### 命令行构建

```bash
Unity.exe -batchmode -quit -nographics \
  -projectPath <项目路径> \
  -logFile <日志路径> \
  -executeMethod TableGameRoom.Mobile.BuildScript.BuildAndroid
```

产物：`Builds/TableGameRoom.apk`

### 编辑器内

菜单 `TABLE → Build Android APK`。

## 安装

1. 下载 Release 里的 `TableGameRoom.apk`
2. 拖进模拟器（MuMu / 雷电 / 夜神）或传到手机安装
3. 包名 `com.table.gameroom`，最低支持 Android 6.0

## 版本

本地只保留最新 2 个版本，GitHub 上通过 tag 永久保留全部版本。

## 说明

- 所有筹码均为**虚拟筹码**，不涉及任何真实货币、充值或提现
- 程序**完全离线运行**，不使用数据库、不上传任何数据
- 本仓库仅供学习与娱乐用途

## 许可

MIT
