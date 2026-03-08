using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace StartupManagerPro
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<StartupItem> startupItems = new();
        private ObservableCollection<CategoryItem> categories = new();
        private string currentCategory = "All";

        public MainWindow()
        {
            InitializeComponent();
            InitializeCategories();
            LoadAllItems();
            
            if (StartupGrid.ContextMenu != null)
            {
                // 使用 Opened 事件，在 XAML 中已绑定
            }
            
            // 添加选择变化事件处理
            StartupGrid.SelectionChanged += StartupGrid_SelectionChanged;
        }

        private void StartupGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 更新工具栏按钮状态
            UpdateToolbarButtonStates();
        }

        private void UpdateToolbarButtonStates()
        {
            if (StartupGrid == null || EnableBtn == null || DisableBtn == null) return;
            
            var selectedItem = StartupGrid.SelectedItem as StartupItem;
            if (selectedItem == null)
            {
                // 没有选中项时，显示所有按钮
                EnableBtn.Visibility = Visibility.Visible;
                DisableBtn.Visibility = Visibility.Visible;
                EnableBtn.Content = "✅ 启用";
                DisableBtn.Content = "⏸️ 禁用";
                return;
            }
            
            bool isRegistry = (selectedItem.Source == "Registry");
            
            // 注册表项：隐藏启用/禁用按钮
            if (isRegistry)
            {
                EnableBtn.Visibility = Visibility.Collapsed;
                DisableBtn.Visibility = Visibility.Collapsed;
            }
            // 其他类型（包括服务）：显示启用/禁用按钮
            else
            {
                EnableBtn.Visibility = Visibility.Visible;
                DisableBtn.Visibility = Visibility.Visible;
                EnableBtn.Content = "✅ 启用";
                DisableBtn.Content = "⏸️ 禁用";
            }
        }

        private void InitializeCategories()
        {
            categories = new ObservableCollection<CategoryItem>
            {
                new CategoryItem { Icon = "📁", Name = "启动文件夹", Count = 0, Key = "Folder", Type = "Folder" },
                new CategoryItem { Icon = "📅", Name = "计划任务", Count = 0, Key = "Task", Type = "Task" },
                new CategoryItem { Icon = "📝", Name = "注册表", Count = 0, Key = "Registry", Type = "Registry" },
                new CategoryItem { Icon = "⚙️", Name = "Windows 服务", Count = 0, Key = "Service", Type = "Service" }
            };
            CategoryTree.ItemsSource = categories;
        }

        private void LoadAllItems()
        {
            startupItems.Clear();
            LoadFromRegistry(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "当前用户");
            LoadFromRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "系统全局");
            LoadFromStartupFolder();
            LoadFromTaskScheduler();
            LoadServices();
            UpdateCounts();
            UpdateStatusBar();
            StartupGrid.ItemsSource = startupItems;
        }

        private void LoadFromRegistry(RegistryKey root, string subKey, string location)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return;
                
                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName)?.ToString();
                    if (string.IsNullOrEmpty(value)) continue;
                    startupItems.Add(new StartupItem
                    {
                        Name = valueName,
                        Path = value,
                        Location = location,
                        Source = "Registry",
                        IsEnabled = true,
                        Publisher = GetPublisher(value),
                        StartupImpact = GetStartupImpact(value),
                        StatusText = "已启用",
                        CategoryType = "Registry",
                        RegistryRoot = root == Registry.CurrentUser ? "HKCU" : "HKLM"
                    });
                }
            }
            catch (Exception ex) { LogError($"注册表加载失败：{ex.Message}"); }
        }

        private void LoadFromStartupFolder()
        {
            try
            {
                var userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(userStartup))
                {
                    // 加载启用的启动项（.lnk 文件）
                    foreach (var file in Directory.GetFiles(userStartup, "*.lnk"))
                    {
                        startupItems.Add(new StartupItem
                        {
                            Name = Path.GetFileNameWithoutExtension(file),
                            Path = file,
                            Location = "当前用户",
                            Source = "Folder",
                            IsEnabled = true,
                            Publisher = "启动文件夹",
                            StartupImpact = "低",
                            StatusText = "已启用",
                            CategoryType = "Folder"
                        });
                    }

                    // 加载禁用的启动项（.lnk.disabled 文件）
                    foreach (var file in Directory.GetFiles(userStartup, "*.lnk.disabled"))
                    {
                        // 文件名格式：xxx.lnk.disabled
                        var nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                        // 再去掉 .lnk 后缀
                        if (nameWithoutExt.EndsWith(".lnk"))
                        {
                            nameWithoutExt = nameWithoutExt.Substring(0, nameWithoutExt.Length - 4);
                        }

                        // 恢复的路径（去掉 .disabled）
                        var originalPath = file.Substring(0, file.Length - 9); // 去掉 .disabled

                        startupItems.Add(new StartupItem
                        {
                            Name = nameWithoutExt,
                            Path = originalPath,
                            Location = "当前用户",
                            Source = "Folder",
                            IsEnabled = false,
                            Publisher = "启动文件夹",
                            StartupImpact = "低",
                            StatusText = "已禁用",
                            CategoryType = "Folder"
                        });
                    }
                }
            }
            catch (Exception ex) { LogError($"启动文件夹加载失败：{ex.Message}"); }
        }

        private void LoadFromTaskScheduler()
        {
            try
            {
                // 使用 PowerShell 获取计划任务状态
                var startInfo = new ProcessStartInfo 
                { 
                    FileName = "powershell", 
                    Arguments = "-Command \"Get-ScheduledTask | Select-Object TaskName, TaskPath, State | ConvertTo-Json\"", 
                    RedirectStandardOutput = true, 
                    RedirectStandardError = true, 
                    UseShellExecute = false, 
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using (var process = Process.Start(startInfo))
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    if (string.IsNullOrWhiteSpace(output)) return;
                    
                    // 解析 JSON
                    var tasks = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(output);
                    
                    void AddTask(System.Text.Json.JsonElement task)
                    {
                        var taskName = task.GetProperty("TaskName").GetString() ?? "";
                        var taskPath = task.TryGetProperty("TaskPath", out var pathProp) ? pathProp.GetString() ?? "" : "";
                        // State 是枚举值：0=Unknown, 1=Disabled, 2=Queued, 3=Ready, 4=Running
                        var stateValue = task.GetProperty("State").GetInt32();
                        
                        // 过滤系统任务（根据 TaskPath 和 TaskName）
                        if (taskPath.Contains("Microsoft") || taskPath.Contains("Windows") || taskPath.Contains("Office") ||
                            taskPath.Contains("Adobe") || taskPath.Contains("OneDrive") || taskPath.Contains("S-") ||
                            taskName.Contains("Microsoft") || taskName.Contains("Windows") || taskName.Contains("Office") ||
                            taskName.Contains("Adobe") || taskName.Contains("OneDrive") || taskName.Contains("S-") ||
                            taskName.Contains("{") || taskName.Contains("Svc") || taskName.Contains("Platform") ||
                            taskName.Contains("EdgeUpdate") || taskName.Contains("Intel PTT") || taskName.Contains("JavaUpdate"))
                            return;
                        
                        // Ready(3) 和 Running(4) 是启用状态，Disabled(1) 是禁用状态
                        bool isEnabled = (stateValue == 3 || stateValue == 4);
                        string statusText = stateValue switch
                        {
                            1 => "已禁用",
                            3 => "已启用",
                            4 => "已运行",
                            _ => "未知"
                        };
                        
                        startupItems.Add(new StartupItem 
                        { 
                            Name = taskName, 
                            Path = "N/A", 
                            Location = "计划任务", 
                            Source = "Task", 
                            IsEnabled = isEnabled, 
                            Publisher = "计划任务", 
                            StartupImpact = "中", 
                            StatusText = statusText, 
                            CategoryType = "Task" 
                        });
                    }
                    
                    if (tasks.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var task in tasks.EnumerateArray())
                        {
                            AddTask(task);
                        }
                    }
                    else if (tasks.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        AddTask(tasks);
                    }
                }
            }
            catch (Exception ex) { LogError($"计划任务加载失败：{ex.Message}"); }
        }

        private void LoadServices()
        {
            try
            {
                var startInfo = new ProcessStartInfo 
                { 
                    FileName = "sc", 
                    Arguments = "query type= service state= all", 
                    RedirectStandardOutput = true, 
                    UseShellExecute = false, 
                    CreateNoWindow = true 
                };
                using (var process = Process.Start(startInfo))
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    string currentServiceName = "", currentDisplayName = "", currentPath = "", startType = "";
                    bool isRunning = false;
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i].Trim();
                        if (line.StartsWith("SERVICE_NAME"))
                        {
                            if (!string.IsNullOrEmpty(currentServiceName)) 
                                AddServiceItem(currentServiceName, currentDisplayName, currentPath, startType, isRunning);
                            currentServiceName = line.Substring(13).Trim();
                            currentDisplayName = ""; currentPath = ""; startType = ""; isRunning = false;
                        }
                        else if (line.StartsWith("DISPLAY_NAME")) 
                        { 
                            var parts = line.Split(':', 2); 
                            if (parts.Length > 1) currentDisplayName = parts[1].Trim(); 
                        }
                        else if (line.StartsWith("BINARY_PATH_NAME")) 
                        { 
                            var parts = line.Split(':', 2); 
                            if (parts.Length > 1) currentPath = parts[1].Trim().Trim('"'); 
                        }
                        else if (line.Contains("START_TYPE"))
                        {
                            if (line.Contains("AUTO_START")) startType = "AUTO_START";
                            else if (line.Contains("DEMAND_START")) startType = "DEMAND_START";
                            else if (line.Contains("DISABLED")) startType = "DISABLED";
                        }
                        else if (line.Contains("STATE")) 
                        { 
                            isRunning = line.Contains("RUNNING"); 
                        }
                    }
                    if (!string.IsNullOrEmpty(currentServiceName)) 
                        AddServiceItem(currentServiceName, currentDisplayName, currentPath, startType, isRunning);
                }
            }
            catch (Exception ex) { LogError($"服务加载失败：{ex.Message}"); }
        }

        private void AddServiceItem(string serviceName, string displayName, string path, string startType, bool isRunning)
        {
            try
            {
                var displayText = string.IsNullOrEmpty(displayName) ? serviceName : displayName;
                string status;
                bool isEnabled;
                
                if (startType == "DISABLED")
                {
                    status = "已禁用";
                    isEnabled = false;
                }
                else
                {
                    status = isRunning ? "已运行" : "已停止";
                    isEnabled = true;
                }
                
                startupItems.Add(new StartupItem
                {
                    Name = displayText,
                    Path = string.IsNullOrEmpty(path) ? "N/A" : path,
                    Location = "系统服务",
                    Source = "Service",
                    IsEnabled = isEnabled,
                    Publisher = "Windows 服务",
                    StartupImpact = "高",
                    StatusText = status,
                    CategoryType = "Service",
                    ServiceName = serviceName
                });
            }
            catch (Exception ex) { LogError($"添加服务项失败 {serviceName}: {ex.Message}"); }
        }

        private string GetPublisher(string path)
        {
            try 
            { 
                if (File.Exists(path)) 
                { 
                    var versionInfo = FileVersionInfo.GetVersionInfo(path); 
                    return versionInfo.CompanyName ?? "未知"; 
                } 
            } 
            catch { }
            return "未知";
        }

        private string GetStartupImpact(string path)
        {
            try
            {
                if (!File.Exists(path)) return "未知";
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length > 50 * 1024 * 1024) return "高";
                if (fileInfo.Length > 10 * 1024 * 1024) return "中";
                return "低";
            }
            catch { return "未知"; }
        }

        private void UpdateCounts()
        {
            foreach (var cat in categories)
            {
                cat.Count = cat.Key switch
                {
                    "All" => startupItems.Count,
                    "Folder" => startupItems.Count(i => i.Source == "Folder"),
                    "Task" => startupItems.Count(i => i.Source == "Task"),
                    "Registry" => startupItems.Count(i => i.Source == "Registry"),
                    "Service" => startupItems.Count(i => i.Source == "Service"),
                    _ => 0
                };
            }
            CategoryTree.ItemsSource = null;
            CategoryTree.ItemsSource = categories;
        }

        private void UpdateStatusBar() 
        { 
            StatusTotal.Text = $"共 {startupItems.Count} 项"; 
        }

        private void FilterItems()
        {
            var filtered = currentCategory switch
            {
                "Folder" => startupItems.Where(i => i.Source == "Folder"),
                "Task" => startupItems.Where(i => i.Source == "Task"),
                "Registry" => startupItems.Where(i => i.Source == "Registry"),
                "Service" => startupItems.Where(i => i.Source == "Service"),
                _ => startupItems
            };
            StartupGrid.ItemsSource = filtered.ToList();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try { 
                LoadAllItems();
                FilterItems(); // 保持当前筛选
            }
            catch (Exception ex) { LogError($"刷新失败：{ex.Message}"); }
        }

        private void CategoryTree_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryTree.SelectedItem is CategoryItem category)
            {
                // 切换类别时清除所有对勾
                foreach (var item in startupItems)
                {
                    item.IsSelected = false;
                }
                
                currentCategory = category.Key;
                FilterItems();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchTerm = SearchBox?.Text?.ToLower() ?? "";
            if (string.IsNullOrEmpty(searchTerm))
            {
                FilterItems();
                return;
            }
            var filtered = startupItems.Where(i =>
                (!string.IsNullOrEmpty(i.Name) && i.Name.ToLower().Contains(searchTerm)) ||
                (!string.IsNullOrEmpty(i.Publisher) && i.Publisher.ToLower().Contains(searchTerm)) ||
                (!string.IsNullOrEmpty(i.Path) && i.Path.ToLower().Contains(searchTerm))
            ).ToList();
            StartupGrid.ItemsSource = filtered;
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null) SearchBox.Text = "";
            FilterItems();
        }

        private void Enable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItems = GetSelectedItems();
                if (selectedItems.Count == 0)
                {
                    MessageBox.Show("请先选择要启用的项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int successCount = 0;
                foreach (var item in selectedItems)
                {
                    bool success = item.CategoryType switch
                    {
                        "Task" => EnableTask(item),
                        "Service" => EnableService(item),
                        "Folder" => EnableFolder(item),
                        "Registry" => false, // 注册表不支持启用
                        _ => false
                    };
                    if (success) successCount++;
                }
                LoadAllItems();
                FilterItems(); // 保持当前筛选
                if (successCount > 0)
                    MessageBox.Show($"成功启用 {successCount} 个启动项", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.Error($"启用失败：{ex.Message}");
                MessageBox.Show($"启用失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Disable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItems = GetSelectedItems();
                if (selectedItems.Count == 0)
                {
                    MessageBox.Show("请先选择要禁用的项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int successCount = 0;
                foreach (var item in selectedItems)
                {
                    bool success = item.CategoryType switch
                    {
                        "Task" => DisableTask(item),
                        "Service" => DisableService(item),
                        "Folder" => DisableFolder(item),
                        "Registry" => false, // 注册表不支持禁用
                        _ => false
                    };
                    if (success) successCount++;
                }
                LoadAllItems();
                FilterItems(); // 保持当前筛选
                if (successCount > 0)
                    MessageBox.Show($"成功禁用 {successCount} 个启动项", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogError($"禁用失败：{ex.Message}");
            }
        }

        // ========== 计划任务启用禁用 ==========
        private bool EnableTask(StartupItem item)
        {
            try
            {
                var taskName = item.Name;
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/Change /TN \"{taskName}\" /Enable",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                }
                System.Threading.Thread.Sleep(500); // 等待状态更新
                return true;
            }
            catch (Exception ex)
            {
                LogError($"启用计划任务失败：{item.Name} - {ex.Message}");
                return false;
            }
        }

        private bool DisableTask(StartupItem item)
        {
            try
            {
                var taskName = item.Name;
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/Change /TN \"{taskName}\" /Disable",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                }
                System.Threading.Thread.Sleep(500); // 等待状态更新
                return true;
            }
            catch (Exception ex)
            {
                LogError($"禁用计划任务失败：{item.Name} - {ex.Message}");
                return false;
            }
        }

        // ========== 服务启用禁用 ==========
        private bool EnableService(StartupItem item)
        {
            try
            {
                var serviceName = ExtractServiceName(item.Name);
                var psi = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"config \"{serviceName}\" start= auto",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                }
                System.Threading.Thread.Sleep(300); // 等待状态更新
                return true;
            }
            catch (Exception ex)
            {
                LogError($"启用服务失败：{item.Name} - {ex.Message}");
                return false;
            }
        }

        private bool DisableService(StartupItem item)
        {
            try
            {
                var serviceName = ExtractServiceName(item.Name);
                var psi = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"config \"{serviceName}\" start= disabled",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                }
                System.Threading.Thread.Sleep(300); // 等待状态更新
                return true;
            }
            catch (Exception ex)
            {
                LogError($"禁用服务失败：{item.Name} - {ex.Message}");
                return false;
            }
        }

        // ========== 启动文件夹启用禁用 ==========
        private bool EnableFolder(StartupItem item)
        {
            try
            {
                var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var shortcutPath = Path.Combine(startupPath, $"{item.Name}.lnk");
                var disabledPath = Path.Combine(startupPath, $"{item.Name}.lnk.disabled");

                // 如果存在 .disabled 文件，重命名回来
                if (File.Exists(disabledPath))
                {
                    File.Move(disabledPath, shortcutPath);
                    return true;
                }
                
                // 如果快捷方式已存在，不需要操作
                if (File.Exists(shortcutPath))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogError($"启用启动文件夹项失败：{item.Name} - {ex.Message}");
                return false;
            }
        }

        private bool DisableFolder(StartupItem item)
        {
            try
            {
                var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var shortcutPath = Path.Combine(startupPath, $"{item.Name}.lnk");
                var disabledPath = Path.Combine(startupPath, $"{item.Name}.lnk.disabled");

                // 如果快捷方式存在，重命名为 .disabled
                if (File.Exists(shortcutPath))
                {
                    File.Move(shortcutPath, disabledPath);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogError($"禁用启动文件夹项失败：{item.Name} - {ex.Message}");
                return false;
            }
        }

        private void RunCommand(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (!string.IsNullOrEmpty(error)) LogError($"命令执行错误：{error}");
                }
            }
            catch (Exception ex)
            {
                LogError($"命令执行失败：{file} {args} - {ex.Message}");
                throw;
            }
        }

        private void ServiceAuto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StartupGrid?.SelectedItem is StartupItem item && item.Source == "Service")
                {
                    var serviceName = ExtractServiceName(item.Name);
                    SetServiceAutoStart(serviceName, true);
                    LoadAllItems();
                    FilterItems(); // 保持当前筛选
                }
            }
            catch { }
        }

        private void ServiceManual_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StartupGrid?.SelectedItem is StartupItem item && item.Source == "Service")
                {
                    var serviceName = ExtractServiceName(item.Name);
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "sc",
                        Arguments = $"config \"{serviceName}\" start= demand",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Verb = "runas"
                    };
                    using (var process = Process.Start(startInfo))
                    {
                        process.WaitForExit();
                    }
                    LoadAllItems();
                    FilterItems(); // 保持当前筛选
                }
            }
            catch { }
        }

        private void ServiceStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItems = GetSelectedItems().Where(i => i.Source == "Service").ToList();
                if (selectedItems.Count == 0) return;
                
                foreach (var item in selectedItems)
                {
                    var serviceName = ExtractServiceName(item.Name);
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "net",
                        Arguments = $"stop \"{serviceName}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Verb = "runas"
                    };
                    using (var process = Process.Start(startInfo))
                    {
                        process.WaitForExit();
                    }
                }
                LoadAllItems();
                FilterItems(); // 保持当前筛选
            }
            catch { }
        }

        private void ServiceStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItems = GetSelectedItems().Where(i => i.Source == "Service").ToList();
                if (selectedItems.Count == 0) return;
                
                foreach (var item in selectedItems)
                {
                    var serviceName = ExtractServiceName(item.Name);
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "net",
                        Arguments = $"start \"{serviceName}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Verb = "runas"
                    };
                    using (var process = Process.Start(startInfo))
                    {
                        process.WaitForExit();
                    }
                }
                LoadAllItems();
                FilterItems(); // 保持当前筛选
            }
            catch { }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItems = GetSelectedItems();
                if (selectedItems.Count == 0) return;
                
                var result = MessageBox.Show($"确定要删除选中的 {selectedItems.Count} 个启动项吗？", 
                    "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    foreach (var item in selectedItems.ToList())
                    {
                        bool deleted = item.Source switch
                        {
                            "Registry" => DeleteFromRegistry(item),
                            "Folder" => DeleteFromFolder(item),
                            "Task" => DeleteFromTaskScheduler(item),
                            "Service" => DeleteService(item),
                            _ => false
                        };
                        if (deleted) startupItems.Remove(item);
                    }
                    UpdateCounts();
                    UpdateStatusBar();
                    FilterItems();
                }
            }
            catch { }
        }

        private bool DeleteFromRegistry(StartupItem item)
        {
            try
            {
                var root = item.RegistryRoot == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                var runPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                
                using var key = root.OpenSubKey(runPath, true);
                if (key != null && key.GetValue(item.Name) != null)
                {
                    key.DeleteValue(item.Name);
                    return true;
                }
            }
            catch (Exception ex) { LogError($"删除注册表启动项失败：{ex.Message}"); }
            return false;
        }

        private bool DeleteFromFolder(StartupItem item)
        {
            try
            {
                var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var shortcutPath = Path.Combine(startupPath, $"{item.Name}.lnk");
                var disabledPath = Path.Combine(startupPath, $"{item.Name}.lnk.disabled");
                
                // 删除启用的快捷方式
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                    return true;
                }
                
                // 删除禁用的快捷方式
                if (File.Exists(disabledPath))
                {
                    File.Delete(disabledPath);
                    return true;
                }
            }
            catch (Exception ex) { LogError($"删除启动文件夹快捷方式失败：{ex.Message}"); }
            return false;
        }

        private bool DeleteFromTaskScheduler(StartupItem item)
        {
            try
            {
                var taskName = item.Name.Replace("任务 ", "");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/Delete /TN \"{taskName}\" /F",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using (var process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex) { LogError($"删除计划任务失败：{ex.Message}"); }
            return false;
        }

        private bool DeleteService(StartupItem item)
        {
            try
            {
                var serviceName = ExtractServiceName(item.Name);
                
                // 先停止服务
                var stopStartInfo = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"stop \"{serviceName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using (var stopProcess = Process.Start(stopStartInfo))
                {
                    stopProcess.WaitForExit();
                }
                
                System.Threading.Thread.Sleep(1000);
                
                // 删除服务
                var deleteStartInfo = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"delete \"{serviceName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using (var deleteProcess = Process.Start(deleteStartInfo))
                {
                    deleteProcess.WaitForExit();
                    return deleteProcess.ExitCode == 0;
                }
            }
            catch (Exception ex) { LogError($"删除服务失败：{ex.Message}"); }
            return false;
        }

        private string ExtractServiceName(string displayName)
        {
            try
            {
                var start = displayName.LastIndexOf('(');
                var end = displayName.LastIndexOf(')');
                if (start >= 0 && end > start)
                    return displayName.Substring(start + 1, end - start - 1);
                return displayName.Replace("服务 ", "").Trim();
            }
            catch
            {
                return displayName.Replace("服务 ", "").Trim();
            }
        }

        private bool SetServiceAutoStart(string serviceName, bool enable)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"config \"{serviceName}\" start= {(enable ? "auto" : "disabled")}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using (var process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                LogError($"设置服务启动类型失败：{ex.Message}");
                return false;
            }
        }

        private void AddNew_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new AddStartupDialog(this);
                dialog.SetDefaultType(currentCategory);
                bool? result = dialog.ShowDialog();
                if (result == true) 
                {
                    LoadAllItems();
                    FilterItems(); // 保持当前筛选
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenLocation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StartupGrid.SelectedItem is StartupItem item)
                {
                    // 注册表：打开注册表编辑器并定位到指定键
                    if (item.Source == "Registry")
                    {
                        var rootKey = item.RegistryRoot == "HKLM" ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
                        var fullPath = $"{rootKey}\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
                        
                        // 设置注册表编辑器的 LastKey 值，使其打开时定位到指定位置
                        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit", true))
                        {
                            if (key != null)
                            {
                                key.SetValue("LastKey", fullPath);
                            }
                        }
                        
                        // 打开注册表编辑器
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "regedit.exe",
                            UseShellExecute = true
                        });
                        return;
                    }
                    
                    // 启动文件夹特殊处理
                    if (item.Source == "Folder")
                    {
                        var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = startupPath,
                            UseShellExecute = true
                        });
                        return;
                    }
                    
                    // 计划任务：打开任务计划程序
                    if (item.Source == "Task")
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "taskschd.msc",
                            UseShellExecute = true
                        });
                        return;
                    }
                    
                    // 服务：打开服务管理器
                    if (item.Source == "Service")
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "services.msc",
                            UseShellExecute = true
                        });
                        return;
                    }
                    
                    if (!string.IsNullOrEmpty(item.Path) && item.Path != "N/A")
                    {
                        // 提取实际的可执行文件路径（去除参数）
                        var exePath = ExtractExePath(item.Path);
                        
                        if (File.Exists(exePath))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = $"/select,\"{exePath}\"",
                                UseShellExecute = true
                            });
                        }
                        else if (Directory.Exists(exePath))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = exePath,
                                UseShellExecute = true
                            });
                        }
                    }
                }
            }
            catch (Exception ex) 
            { 
                Debug.WriteLine($"[OpenLocation_Click] Error: {ex.Message}");
                MessageBox.Show($"打开位置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 从路径中提取实际的可执行文件路径（去除参数）
        private string ExtractExePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            
            // 去除引号
            path = path.Trim('"');
            
            // 如果路径包含空格和参数，提取 .exe 部分
            if (path.Contains(".exe"))
            {
                var exeIndex = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exeIndex > 0)
                {
                    var exePath = path.Substring(0, exeIndex + 4);
                    // 检查是否有引号包裹
                    if (exePath.StartsWith("\"") && !exePath.EndsWith("\""))
                    {
                        exePath = exePath.TrimStart('"');
                    }
                    return exePath;
                }
            }
            
            // 如果路径包含空格，取第一个空格前的部分
            var spaceIndex = path.IndexOf(' ');
            if (spaceIndex > 0)
            {
                var firstPart = path.Substring(0, spaceIndex);
                if (File.Exists(firstPart)) return firstPart;
            }
            
            return path;
        }

        private void Properties_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StartupGrid.SelectedItem is StartupItem item)
                {
                    var message = $"📌 名称：{item.Name}\n";
                    message += $"🏢 厂商：{item.Publisher}\n";
                    message += $"📍 位置：{item.Location}\n";
                    message += $"📊 状态：{item.StatusText}";
                    MessageBox.Show(message, "属性", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[Properties_Click] Error: {ex.Message}"); }
        }

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var selectedItem = StartupGrid.SelectedItem as StartupItem;
            var contextMenu = sender as ContextMenu;
            if (contextMenu == null) return;
            
            // 没有选中项时，隐藏整个菜单
            if (selectedItem == null)
            {
                contextMenu.Visibility = Visibility.Collapsed;
                return;
            }
            
            contextMenu.Visibility = Visibility.Visible;
            
            bool isService = (selectedItem.Source == "Service");
            bool isRegistry = (selectedItem.Source == "Registry");
            
            var enableMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "启用");
            var disableMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "禁用");
            var separator1 = contextMenu.Items.OfType<Separator>().FirstOrDefault();
            var autoMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "自动");
            var manualMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "手动");
            var stopMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "停止");
            var startMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "启动");
            
            // 注册表：隐藏启用/禁用，隐藏服务菜单，隐藏分隔符
            // 服务：隐藏启用/禁用，显示服务菜单，显示分隔符
            // 其他：显示启用/禁用，隐藏服务菜单，隐藏分隔符
            if (enableMenu != null)
            {
                enableMenu.Visibility = (isService || isRegistry) ? Visibility.Collapsed : Visibility.Visible;
            }
            if (disableMenu != null)
            {
                disableMenu.Visibility = (isService || isRegistry) ? Visibility.Collapsed : Visibility.Visible;
            }
            
            // 分隔符：只在服务时显示
            if (separator1 != null)
            {
                separator1.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
            }
            
            // 服务专用菜单：只在服务时显示
            if (autoMenu != null) autoMenu.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
            if (manualMenu != null) manualMenu.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
            if (stopMenu != null) stopMenu.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
            if (startMenu != null) startMenu.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
        }

        // 获取复选框选中的项目
        private List<StartupItem> GetSelectedItems()
        {
            return startupItems.Where(i => i.IsSelected).ToList();
        }

        // 全选/取消全选
        private void SelectAllCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (SelectAllCheckBox == null) return;
            
            bool isChecked = SelectAllCheckBox.IsChecked == true;
            
            // 获取当前显示的项目（考虑筛选）
            var visibleItems = StartupGrid.ItemsSource as System.Collections.IEnumerable;
            if (visibleItems == null) return;
            
            foreach (var item in visibleItems.Cast<StartupItem>())
            {
                item.IsSelected = isChecked;
            }
            
            // 刷新显示
            StartupGrid.Items.Refresh();
        }

        // 左键单击只勾选当前行（单选模式）
        private void StartupGrid_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 获取当前选中的项
            if (StartupGrid.SelectedItem is StartupItem clickedItem)
            {
                // 取消所有行的勾选
                foreach (var item in startupItems)
                {
                    item.IsSelected = false;
                }
                
                // 只勾选当前点击的行
                clickedItem.IsSelected = true;
                StartupGrid.Items.Refresh();
            }
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("启动管理器 Pro v3.0\n\n功能：管理注册表启动项、启动文件夹、计划任务、Windows 服务\n\nOpenClaw 出品", "帮助", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void LogError(string message)
        {
            Debug.WriteLine($"[ERROR] {message}");
        }
    }

    public class StartupItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected 
        { 
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }
        
        public bool IsEnabled { get; set; }
        public string Name { get; set; } = "";
        public string Publisher { get; set; } = "未知";
        public string Path { get; set; } = "";
        public string Location { get; set; } = "";
        public string Source { get; set; } = "";
        public string StartupImpact { get; set; } = "未知";
        public string StatusText { get; set; } = "已禁用";
        public string CategoryType { get; set; } = "";
        public string ServiceName { get; set; } = "";
        public string RegistryRoot { get; set; } = "HKCU";
        
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    public class CategoryItem
    {
        public string Icon { get; set; } = "";
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public string Key { get; set; } = "";
        public string Type { get; set; } = "";
    }
}