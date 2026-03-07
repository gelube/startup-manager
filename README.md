# Startup Manager Pro v3.0

🚀 一款现代化的 Windows 启动项管理工具，支持注册表、启动文件夹、计划任务和 Windows 服务四种启动方式的管理。

![.NET 6.0](https://img.shields.io/badge/.NET-6.0-512BD4)
![WPF](https://img.shields.io/badge/UI-WPF-512BD4)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6)
![License](https://img.shields.io/badge/License-MIT-green)

---

## ✨ 功能特性

### 🔧 四种启动方式管理

| 类型 | 图标 | 说明 | 权限要求 |
|------|------|------|----------|
| **注册表** | 📝 | HKCU/HKLM Run 键值管理 | 普通用户 |
| **启动文件夹** | 📁 | 用户/全局启动文件夹快捷方式 | 普通用户 |
| **计划任务** | 📅 | Windows 任务计划程序 | 管理员 |
| **Windows 服务** | ⚙️ | NSSM 集成的服务管理 | 管理员 |

### 📋 完整的 CRUD 操作

- **➕ 添加** - 通过对话框添加新启动项，支持四种方式
- **🗑️ 删除** - 删除选中的启动项（支持多选）
- **✅ 启用** - 启用已禁用的启动项
- **⏸️ 禁用** - 禁用启动项（不删除）
- **🔄 刷新** - 实时刷新启动项列表

### 🔍 查看与搜索

- **📋 查看属性** - 显示完整的启动项信息
- **📂 打开文件位置** - 快速定位程序路径
- **🔍 在线搜索** - 一键搜索程序信息
- **🔎 实时搜索** - 按名称/厂商/路径快速过滤

### 🎨 现代化 UI

- 彩色分类树导航（9 种分类）
- 详细的数据列表视图（7 列信息）
- 底部详情面板（显示选中项属性）
- 右键上下文菜单（快捷操作）
- 工具栏快速访问按钮

---

## 📦 安装

### 系统要求

- Windows 10/11
- .NET 6.0 Runtime

### 编译

```bash
# 克隆仓库
git clone https://github.com/gelube/startup-manager.git
cd startup-manager

# 编译
dotnet build -c Release

# 输出位置
# bin/Release/net6.0-windows/StartupManagerPro.exe
```

---

## 🚀 快速开始

1. **启动程序** - 双击 `StartupManagerPro.exe`
2. **浏览启动项** - 左侧分类树选择类型，右侧显示列表
3. **搜索** - 右上方搜索框快速过滤
4. **操作** - 工具栏或右键菜单执行增删改查

---

## 🛠️ 技术细节

### 项目结构

```
startup-manager-v3/
├── App.xaml / App.xaml.cs        # 应用程序入口
├── MainWindow.xaml               # 主窗口 UI
├── MainWindow.xaml.cs            # 主窗口逻辑
├── AddStartupDialog.xaml         # 添加对话框 UI
├── AddStartupDialog.xaml.cs      # 添加对话框逻辑
├── Logger.cs                     # 日志工具类
├── StartupManagerPro.csproj      # 项目文件
├── app.manifest                  # 管理员权限声明
└── tools/
    └── nssm.exe                  # 服务管理工具
```

### 依赖工具

- **NSSM 2.24** - Non-Sucking Service Manager，用于创建和管理 Windows 服务
  - 许可证：GPL v2
  - 自动查找顺序：内置 `tools/nssm.exe` → 系统 PATH → 常见安装位置

### 注册表位置

```
# 当前用户启动项
HKCU\Software\Microsoft\Windows\CurrentVersion\Run

# 本地机器启动项（需要管理员权限）
HKLM\Software\Microsoft\Windows\CurrentVersion\Run
```

### 启动文件夹位置

```
# 当前用户
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup

# 所有用户
C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup
```

---

## ⚠️ 注意事项

### 管理员权限

部分操作需要管理员权限：
- 查看/修改 HKLM 注册表项
- 创建计划任务
- 创建/管理 Windows 服务

程序会自动请求提权（UAC 提示）。

### Windows 服务

使用 NSSM 创建服务时：
- 服务名称必须唯一
- 程序路径必须有效
- 建议设置 `AppDirectory` 确保依赖文件可找到

### 安全建议

- 不要随意添加未知程序到启动项
- 定期检查启动项，移除不需要的程序
- 禁用可疑启动项而非直接删除，便于后续排查

---

## 🔧 开发笔记

### 添加启动项关键逻辑

```csharp
// 启动文件夹 - 创建快捷方式
dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
var shortcut = shell.CreateShortcut(shortcutPath);
shortcut.TargetPath = userSelectedPath; // ← 用户指定的路径
shortcut.Save();

// 注册表 - 写入键值
key.SetValue(startupName, userSelectedPath);

// 计划任务 - schtasks 命令
schtasks /Create /TN "任务名" /TR "程序路径" /SC ONLOGON

// Windows 服务 - NSSM 命令
nssm install "服务名" "程序路径"
```

### 删除启动项逻辑

| 类型 | 删除方法 |
|------|----------|
| 注册表 | `key.DeleteValue(name)` |
| 启动文件夹 | `File.Delete(shortcutPath)` |
| 计划任务 | `schtasks /Delete /TN "任务名" /F` |
| Windows 服务 | `sc delete "服务名"` |

---

## 📄 许可证

本项目采用 MIT 许可证。

NSSM 工具采用 GPL v2 许可证，详见 [NSSM 官网](https://nssm.cc/)。

---

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

---

<p align="center">Made with ❤️ by OpenClaw</p>