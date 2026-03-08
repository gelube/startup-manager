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
        public bool IsBackgroundMode { get; private set; } = true;

        public AddStartupDialog(Window owner)
        {
            Logger.Info("AddStartupDialog \u6784\u9020\u51FD\u6570\u5F00\u59CB");
            try
            {
                InitializeComponent();
                Owner = owner;
                if (TypeBox != null) TypeBox.SelectedIndex = 0;
                if (ServiceModePanel != null) ServiceModePanel.Visibility = Visibility.Collapsed;
                Logger.Info("AddStartupDialog \u521D\u59CB\u5316\u6210\u529F");
            }
            catch (Exception ex)
            {
                Logger.Error("AddStartupDialog \u521D\u59CB\u5316\u5931\u8D25", ex);
                MessageBox.Show($"\u5BF9\u8BDD\u6846\u521D\u59CB\u5316\u5931\u8D25\uFF1A{ex.Message}", "\u9519\u8BEF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "\u53EF\u6267\u884C\u6587\u4EF6|*.exe|\u6240\u6709\u6587\u4EF6|*.*" };
            if (dlg.ShowDialog() == true) PathBox.Text = dlg.FileName;
        }

        private void TypeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (TypeBox == null || ServiceModePanel == null) return;
            AddType = TypeBox.SelectedIndex;
            ServiceModePanel.Visibility = (AddType == 3) ? Visibility.Visible : Visibility.Collapsed;
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
            
            // \u83B7\u53D6\u7528\u6237\u9009\u62E9\u7684\u8FD0\u884C\u6A21\u5F0F
            IsBackgroundMode = BackgroundModeRadio?.IsChecked ?? true;

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
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                key?.SetValue(StartupName, StartupPath);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"\u6DFB\u52A0\u5931\u8D25\uFF1A{ex.Message}", "\u9519\u8BEF", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show("\u672A\u627E\u5230 NSSM \u5DE5\u5177", "\u9519\u8BEF", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // \u5B89\u88C5\u670D\u52A1
                RunCommand(nssmPath, $"install \"{StartupName}\" \"{StartupPath}\"");
                RunCommand(nssmPath, $"set \"{StartupName}\" AppDirectory \"{Path.GetDirectoryName(StartupPath)}\"");
                
                // \u6839\u636E\u7528\u6237\u9009\u62E9\u8BBE\u7F6E\u524D\u53F0/\u540E\u53F0
                RunCommand(nssmPath, $"set \"{StartupName}\" AppNoConsole {(IsBackgroundMode ? "1" : "0")}");
                
                // \u9ED8\u8BA4\u7981\u7528
                RunCommand("sc", $"config \"{StartupName}\" start= disabled");
                
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"\u6DFB\u52A0\u5931\u8D25\uFF1A{ex.Message}", "\u9519\u8BEF", MessageBoxButton.OK, MessageBoxImage.Error);
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
            
            // 更新说明文字
            if (ServiceModePanel != null)
                ServiceModePanel.Visibility = (defaultIndex == 3) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
