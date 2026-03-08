using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace StartupManagerPro
{
    public partial class AddStartupDialog : Window
    {
        public string StartupName { get; private set; } = "";
        public string StartupPath { get; private set; } = "";
        public int AddType { get; private set; } = 0;

        public AddStartupDialog(Window owner)
        {
            Logger.Info("AddStartupDialog 构造函数开始");
            try
            {
                InitializeComponent();
                Owner = owner;
                if (TypeBox != null) TypeBox.SelectedIndex = 0;
                Logger.Info("AddStartupDialog 初始化成功");
            }
            catch (Exception ex)
            {
                Logger.Error("AddStartupDialog 初始化失败", ex);
                MessageBox.Show($"对话框初始化失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "\u53EF\u6267\u884C\u6587\u4EF6|*.exe|\u6240\u6709\u6587\u4EF6|*.*" };
            if (dlg.ShowDialog() == true) PathBox.Text = dlg.FileName;
        }

        private void TypeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (TypeBox == null || RegistryLocationPanel == null) return;
            AddType = TypeBox.SelectedIndex;
            
            // 显示注册表位置选择面板
            RegistryLocationPanel.Visibility = (AddType == 1) ? Visibility.Visible : Visibility.Collapsed;
            
            // 更新提示文字
            UpdateInfoText();
        }
        
        private void UpdateInfoText()
        {
            if (InfoText == null) return;
            
            InfoText.Text = AddType switch
            {
                0 => "提示：启动文件夹方式最简单，无需特殊权限。程序会在用户登录时自动启动。",
                1 => "提示：注册表方式适合需要开机自启的程序。当前用户仅对当前账户生效，系统全局对所有账户生效。",
                2 => "提示：计划任务方式功能强大，支持多种触发条件。需要管理员权限。",
                3 => "提示：Windows 服务方式适合后台程序，开机即启动无需登录。需要管理员权限。",
                _ => ""
            };
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(PathBox.Text))
            {
                MessageBox.Show("\u8BF7\u586B\u5199\u540D\u79F0\u548C\u8DEF\u5F84", "\u63D0\u793A", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(PathBox.Text))
            {
                MessageBox.Show($"\u8DEF\u5F84\u4E0D\u5B58\u5728\uFF1A{PathBox.Text}", "\u9519\u8BEF", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            StartupName = NameBox.Text.Trim();
            StartupPath = PathBox.Text.Trim();

            bool success = AddType switch
            {
                0 => AddToStartupFolder(),
                1 => AddToRegistry(),
                2 => AddToTaskScheduler(),
                3 => AddToService(),
                _ => false
            };

            if (success) DialogResult = true;
        }

        private bool AddToStartupFolder()
        {
            try
            {
                var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var shortcutPath = Path.Combine(startupPath, $"{StartupName}.lnk");
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-Command \"$WshShell=New-Object -ComObject WScript.Shell; $S=$WshShell.CreateShortcut('{shortcutPath}'); $S.TargetPath='{StartupPath}'; $S.Save()\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using (var p = Process.Start(psi)) { p.WaitForExit(); }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"\u6DFB\u52A0\u5931\u8D25\uFF1A{ex.Message}", "\u9519\u8BEF", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool AddToRegistry()
        {
            try
            {
                // 根据用户选择决定使用 HKCU 还是 HKLM
                bool useHKLM = RegistryLocationBox?.SelectedIndex == 1;
                var root = useHKLM ? Registry.LocalMachine : Registry.CurrentUser;
                string location = useHKLM ? "系统全局" : "当前用户";
                
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null)
                {
                    MessageBox.Show($"无法打开注册表键", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                
                key.SetValue(StartupName, StartupPath);
                MessageBox.Show($"✅ 成功添加到注册表\n\n名称：{StartupName}\n路径：{StartupPath}\n位置：{location}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool AddToTaskScheduler()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/Create /TN \"{StartupName}\" /TR \"{StartupPath}\" /SC ONLOGON /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                
                using (var p = Process.Start(psi)) { p.WaitForExit(); }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"\u6DFB\u52A0\u5931\u8D25\uFF1A{ex.Message}", "\u9519\u8BEF", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool AddToService()
        {
            try
            {
                var nssmPath = FindNSSM();
                if (string.IsNullOrEmpty(nssmPath))
                {
                    MessageBox.Show("未找到 NSSM 工具", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // 安装服务
                RunCommand(nssmPath, $"install \"{StartupName}\" \"{StartupPath}\"");
                RunCommand(nssmPath, $"set \"{StartupName}\" AppDirectory \"{Path.GetDirectoryName(StartupPath)}\"");
                RunCommand(nssmPath, $"set \"{StartupName}\" AppNoConsole 1"); // 后台运行
                
                // 默认自动
                RunCommand("sc", $"config \"{StartupName}\" start= auto");
                
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void RunCommand(string file, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            };
            using (var p = Process.Start(psi)) { p.WaitForExit(); }
        }

        private string FindNSSM()
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var baseDir = string.IsNullOrEmpty(exePath) ? 
                AppDomain.CurrentDomain.BaseDirectory : 
                System.IO.Path.GetDirectoryName(exePath);
            
            var paths = new[]
            {
                System.IO.Path.Combine(baseDir, "tools", "nssm.exe"),
                System.IO.Path.Combine(baseDir, "tool", "nssm.exe"),
                @"Z:\Apps\StartupManager-v3.1\SingleFile\tool\nssm.exe",
                @"Z:\Apps\StartupManager-v3.1\SingleFile\tools\nssm.exe",
                @"Z:\Apps\StartupManager-v3\tools\nssm.exe",
                @"Z:\Tools\nssm.exe",
                @"C:\ProgramData\chocolatey\tools\nssm.exe"
            };
            
            foreach (var path in paths)
            {
                if (System.IO.File.Exists(path)) return path;
            }
            return "";
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        
        /// <summary>
        /// 根据分类设置默认类型
        /// </summary>
        public void SetDefaultType(string category)
        {
            if (TypeBox == null) return;
            
            int defaultIndex = category switch
            {
                "Folder" => 0,   // 启动文件夹
                "Task" => 2,     // 计划任务
                "Registry" => 1, // 注册表
                "Service" => 3,  // Windows 服务
                _ => 0           // 默认启动文件夹
            };
            
            TypeBox.SelectedIndex = defaultIndex;
            AddType = defaultIndex;
        }
    }
}
