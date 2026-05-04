# Supertech Auto Upload Video

`supertech-AutoUploadVideo` 是运行在 OBS 录制电脑上的 Windows 桌面客户端。它负责监听 OBS 视频保存目录，识别新生成的 `.mp4` 文件，按文件名规则匹配 `supertech-program-manager` 中的活动节目，并通过云存储 SDK 断点直传视频，上传完成后通知 WEB 后端写入数据库。

## 功能概览

- 登录 `supertech-program-manager` 后端并保存访问令牌。
- 选择活动，自动加载该活动下的节目列表。
- 设置 OBS 视频监控目录，只处理 `.mp4` 文件。
- 使用 `FileSystemWatcher` 加周期扫描双保险，避免漏掉录制文件。
- 等待文件写入稳定后再入队，避免 OBS 尚未写完就上传。
- 支持文件名规则解析，例如 `{节目号}-{节目名}-{录制时间}`。
- 本地 SQLite 持久化上传队列，程序重启后任务仍可查看和继续处理。
- 使用 Qiniu C# SDK 执行断点续传直传，服务端不转发视频流量。
- 显示上传进度、已上传大小、状态和错误信息。
- 支持人工上传、暂停、取消上传。
- 支持查看已上传视频、打开链接、复制链接、删除视频。
- 最小化后保留任务栏状态，并通过托盘图标颜色显示工作状态。

## 当前技术栈

- .NET WPF 桌面应用
- Target Framework: `net7.0-windows`
- SQLite: `Microsoft.Data.Sqlite`
- Qiniu SDK: `Qiniu`
- 后端依赖：`supertech-program-manager`

说明：原计划推荐 .NET 8，但当前电脑只安装了 .NET 7 SDK，因此首版项目目标为 `net7.0-windows`。安装 .NET 8 SDK 后，可将 `Supertech.AutoUploadVideo.csproj` 升级为 `net8.0-windows`。

## 目录结构

```text
supertech-AutoUploadVideo/
├─ Models/
│  └─ AppModels.cs              # 活动、节目、上传任务、配置等模型
├─ Services/
│  ├─ ApiClient.cs              # 与 supertech-program-manager 后端通信
│  ├─ AppSettingsService.cs     # 本地配置读写
│  ├─ FileNameRuleParser.cs     # 文件名规则解析与节目匹配
│  ├─ FolderMonitorService.cs   # OBS 目录监听与文件稳定检测
│  └─ UploadQueueRepository.cs  # SQLite 上传队列
├─ Storage/
│  └─ CloudUploaders.cs         # 云存储上传抽象与 Qiniu 实现
├─ MainWindow.xaml              # 主界面
├─ MainWindow.xaml.cs           # 界面交互和上传调度
├─ NuGet.config                 # 项目级 NuGet 源
└─ Supertech.AutoUploadVideo.csproj
```

## 运行要求

- Windows 10 或更高版本。
- .NET 7 SDK 或更高版本。
- `supertech-program-manager` 后端可访问。
- WEB 后端已配置云存储。
- 当前首版桌面直传支持 Qiniu。

检查 .NET 环境：

```powershell
dotnet --info
```

构建和运行：

```powershell
cd F:\code\supertech-AutoUploadVideo
dotnet build
dotnet run
```

## 后端接口依赖

客户端使用 `supertech-program-manager` 的以下接口：

```text
POST   /api/admin/login
GET    /api/admin/activities
GET    /api/admin/activities/{activity_id}/programs
POST   /api/upload/desktop/init
POST   /api/upload/desktop/complete
POST   /api/upload/desktop/abort
GET    /api/upload/desktop/videos?activity_id={activity_id}
DELETE /api/upload/desktop/videos/{video_id}
```

上传流程：

1. 客户端登录后获得 JWT。
2. 用户选择活动。
3. 客户端监听 OBS 目录并识别新视频。
4. 客户端根据文件名规则匹配节目。
5. 客户端调用 `/api/upload/desktop/init` 获取直传凭证。
6. 客户端使用云存储 SDK 直传视频。
7. 上传成功后调用 `/api/upload/desktop/complete`。
8. 后端创建 `Video` 记录，并更新对应 `Program` 的视频状态。

## 使用方法

1. 启动程序。
2. 如需修改服务器地址，点击登录窗口右上角齿轮按钮并填写地址，例如：

```text
http://localhost:8000/api
```

3. 输入管理端账号密码登录，也可以直接按回车提交。密码不会保存在本机。
4. 登录成功后进入主界面，点击“刷新活动”，选择当前录制活动。
5. 点击“选择”，设置 OBS 视频保存目录。
6. 设置文件名规则。
7. 点击“启动监听”。
8. OBS 录制生成 `.mp4` 后，程序会自动识别、匹配、入队并上传。

## 文件名规则

当前支持以下占位符：

```text
{节目号}
{节目名}
{录制时间}
{日期}
```

示例规则：

```text
{节目号}-{节目名}-{录制时间}
```

示例文件：

```text
004-板胡声声-15:34:20.mp4
```

解析结果：

```text
节目号：4
节目名：板胡声声
录制时间：当天 15:34:20
```

匹配策略：

- 优先用节目号匹配活动下的 `sequence_number`。
- 如果节目号缺失，再用节目名匹配。
- 匹配不到时进入“待处理”，不会自动上传。

## 上传状态

任务状态说明：

```text
Ready       已匹配节目，等待上传
Uploading   上传中
Paused      已暂停
Success     上传成功并已写入数据库
Failed      上传失败
Cancelled   已取消
NeedsReview 未匹配节目，需要人工处理
```

状态颜色：

```text
灰色：停止
绿色：监听中，当前空闲
蓝色：上传中
黄色：暂停、取消或待人工处理
红色：错误或上传失败
```

最小化时，程序仍显示在任务栏；托盘图标也会同步状态颜色。

## 本地数据

程序会将配置、队列和断点记录保存到：

```text
%APPDATA%\Supertech\AutoUploadVideo
```

主要内容：

```text
settings.json      # 服务器地址、活动、目录、规则等配置
queue.db           # SQLite 上传队列
resume/            # Qiniu 断点续传记录
```

如果要完全重置客户端状态，可以退出程序后删除该目录。

## 云存储支持

当前实现：

- Qiniu：已支持 SDK 断点续传直传。
- Aliyun OSS：后端目前会返回“不支持桌面直传”的明确提示。
- Tencent COS：后端目前会返回“不支持桌面直传”的明确提示。

设计原则：

- 客户端不直连数据库。
- 客户端不保存云存储永久密钥。
- 客户端只使用后端签发的临时上传凭证。
- 服务端不转发视频文件流量，只负责鉴权、签发凭证、完成登记和删除管理。

Aliyun/Tencent 后续要启用桌面直传，需要在 `supertech-program-manager` 后端补充 STS/CAM 临时凭证签发能力。

## 与 supertech-program-manager 的关系

`supertech-program-manager` 是 WEB 管理端和数据中心，负责：

- 活动管理。
- 节目管理。
- 云存储配置。
- 视频上传任务登记。
- 视频删除与节目状态同步。
- 家长端视频/照片访问。

`supertech-AutoUploadVideo` 是录制电脑上的现场工具，负责：

- 监控 OBS 输出目录。
- 解析录制文件。
- 匹配活动节目。
- 直传视频。
- 通知 WEB 后端入库。

## 常见问题

### 登录失败

确认服务器地址以 `/api` 结尾，例如：

```text
http://localhost:8000/api
```

同时确认 `supertech-program-manager` 后端正在运行，并且账号密码与管理端一致。

### 选择活动后没有节目

确认 WEB 管理端中该活动已经导入或创建节目。客户端只会匹配已有节目，首版不会自动创建节目。

### 文件没有进入队列

检查：

- 监听目录是否为 OBS 实际保存目录。
- 文件扩展名是否为 `.mp4`。
- OBS 是否仍在写入该文件。
- 文件名是否符合当前规则。

程序会等待文件大小稳定并且可独占打开后再入队。

### 上传提示当前云存储不支持直传

当前首版仅 Qiniu 路径具备完整直传实现。Aliyun/Tencent 需要服务端先实现临时凭证签发，不会回退到服务端中转上传。

### 上传中断后如何续传

再次点击该任务的“上传”按钮。Qiniu SDK 会使用本地 `resume/` 目录中的断点记录继续上传。

## 验证命令

构建客户端：

```powershell
dotnet build
```

检查后端是否暴露桌面上传接口：

```powershell
Invoke-WebRequest -UseBasicParsing http://localhost:8000/openapi.json
```

## 后续计划

- 增加节目人工选择/重新匹配界面。
- 增加 Aliyun OSS STS 直传。
- 增加 Tencent COS CAM 临时密钥直传。
- 增加开机自启动配置。
- 增加批量重试和失败筛选。
- 增加上传速度限制和并发数配置。
- 增加更完整的上传历史筛选和本地日志文件。
