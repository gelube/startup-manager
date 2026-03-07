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
                StartupGrid.ContextMenu.ContextMenuOpening += ContextMenu_Opening;
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
                }
            }
            catch (Exception ex) { LogError($"启动文件夹加载失败：{ex.Message}"); }
        }

        private void LoadFromTaskScheduler()
        {
            try
            {
                var startInfo = new ProcessStartInfo 
                { 
                    FileName = "schtasks", 
                    Arguments = "/query /fo CSV /nh", 
                    RedirectStandardOutput = true, 
                    RedirectStandardError = true, 
                    UseShellExecute = false, 
                    CreateNoWindow = true 
                };
                using (var process = Process.Start(startInfo))
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 3)
                        {
                            var taskName = parts[0].Trim('"');
                            if (taskName.Contains("Microsoft") || taskName.Contains("OneDrive") || 
                                taskName.Contains("Windows") || taskName.Contains("Office") || 
                                taskName.Contains("Adobe") || taskName.Contains("Activation") || 
                                taskName.Contains("S-") || taskName.Contains("{") || 
                                taskName.Contains("Svc") || taskName.Contains("Platform")) continue;
                            
                            startupItems.Add(new StartupItem 
                            { 
                                Name = $"任务 {taskName}", 
                                Path = "N/A", 
                                Location = "计划任务", 
                                Source = "Task", 
                                IsEnabled = true, 
                                Publisher = "计划任务", 
                                StartupImpact = "中", 
                                StatusText = "已启用", 
                                CategoryType = "Task" 
                            });
                        }
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
                var displayText = string.IsNullOrEmpty(displayName) ? serviceName : $"{displayName} ({serviceName})";
                string status;
                bool isEnabled;
                
                if (startType == "DISABLED")
                {
                    status = "已禁用";
                    isEnabled = false;
                }
                else if (startType == "DEMAND_START")
                {
                    status = isRunning ? "已运行（手动）" : "已停止（手动）";
                    isEnabled = isRunning;
                }
                else if (startType == "AUTO_START")
                {
                    status = isRunning ? "已运行（自动）" : "已停止（自动）";
                    isEnabled = true;
                }
                else
                {
                    status = isRunning ? "已运行" : "已停止";
                    isEnabled = isRunning;
                }
                
                startupItems.Add(new StartupItem
                {
                    Name = $"服务 {displayText}",
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
            try { LoadAllItems(); }
            catch (Exception ex) { LogError($"刷新失败：{ex.Message}"); }
        }

        private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (CategoryTree.SelectedItem is CategoryItem category)
            {
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
                if (StartupGrid?.SelectedItems == null || StartupGrid.SelectedItems.Count == 0)
                {
                    MessageBox.Show("请先选择要启用的项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int successCount = 0;
                foreach (var item in StartupGrid.SelectedItems.Cast<StartupItem>())
                {
                    switch (item.CategoryType)
                    {
                        case "Task":
                            var taskName = item.Name.Replace("任务 ", "");
                            RunCommand("schtasks", $"/Change /TN \"{taskName}\" /Enable");
                            successCount++;
                            break;

                        case "Service":
                            var serviceName = ExtractServiceName(item.Name);
                            if (SetServiceAutoStart(serviceName, true))
                            {
                                successCount++;
                            }
                            break;

                        case "Folder":
                            item.IsEnabled = true;
                            item.StatusText = "已启用";
                            successCount++;
                            break;
                        
                        // 注册表不支持启用/禁用
                        case "Registry":
                            break;
                    }
                }
                LoadAllItems();
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
                if (StartupGrid?.SelectedItems == null || StartupGrid.SelectedItems.Count == 0)
                {
                    MessageBox.Show("请先选择要禁用的项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int successCount = 0;
                foreach (var item in StartupGrid.SelectedItems.Cast<StartupItem>())
                {
                    switch (item.CategoryType)
                    {
                        case "Task":
                            var taskName = item.Name.Replace("任务 ", "");
                            RunCommand("schtasks", $"/Change /TN \"{taskName}\" /Disable");
                            successCount++;
                            break;

                        case "Service":
                            var serviceName = ExtractServiceName(item.Name);
                            if (SetServiceAutoStart(serviceName, false))
                            {
                                successCount++;
                            }
                            break;

                        case "Folder":
                            item.IsEnabled = false;
                            item.StatusText = "已禁用";
                            successCount++;
                            break;
                        
                        // 注册表不支持启用/禁用
                        case "Registry":
                            break;
                    }
                }
                LoadAllItems();
                if (successCount > 0)
                    MessageBox.Show($"成功禁用 {successCount} 个启动项", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) 
            { 
                LogError($"禁用失败：{ex.Message}"); 
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
                }
            }
            catch { }
        }

        private void ServiceStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StartupGrid?.SelectedItem is StartupItem item && item.Source == "Service")
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
                    LoadAllItems();
                }
            }
            catch { }
        }

        private void ServiceStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StartupGrid?.SelectedItem is StartupItem item && item.Source == "Service")
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
                    LoadAllItems();
                }
            }
            catch { }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StartupGrid?.SelectedItems == null || StartupGrid.SelectedItems.Count == 0) return;
                
                var result = MessageBox.Show($"确定要删除选中的 {StartupGrid.SelectedItems.Count} 个启动项吗？", 
                    "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    var toRemove = StartupGrid.SelectedItems.Cast<StartupItem>().ToList();
                    foreach (var item in toRemove)
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
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
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
                if (result == true) LoadAllItems();
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
                if (StartupGrid.SelectedItem is StartupItem item && 
                    !string.IsNullOrEmpty(item.Path) && 
                    File.Exists(item.Path))
                {
                    Process.Start("explorer.exe", $"/select,\"{item.Path}\"");
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[OpenLocation_Click] Error: {ex.Message}"); }
        }

        private void Properties_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StartupGrid.SelectedItem is StartupItem item)
                {
                    DetailName.Text = item.Name;
                    DetailPublisher.Text = item.Publisher;
                    DetailType.Text = item.Location;
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[Properties_Click] Error: {ex.Message}"); }
        }

        private void ContextMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            var selectedItem = StartupGrid.SelectedItem as StartupItem;
            if (selectedItem == null) return;
            
            bool isService = (selectedItem.Source == "Service");
            bool isRegistry = (selectedItem.Source == "Registry");
            
            var contextMenu = sender as ContextMenu;
            if (contextMenu == null) return;
            
            var enableMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "启用");
            var disableMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "禁用");
            var autoMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "自动");
            var manualMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "手动");
            var stopMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "停止");
            var startMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "启动");
            
            // 服务：隐藏启用/禁用，显示服务专用菜单
            // 注册表：启用/禁用变灰
            // 其他：启用/禁用可用
            if (enableMenu != null)
            {
                enableMenu.Visibility = isService ? Visibility.Collapsed : Visibility.Visible;
                enableMenu.IsEnabled = !isRegistry;
            }
            if (disableMenu != null)
            {
                disableMenu.Visibility = isService ? Visibility.Collapsed : Visibility.Visible;
                disableMenu.IsEnabled = !isRegistry;
            }
            
            // 服务专用菜单
            if (autoMenu != null) autoMenu.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
            if (manualMenu != null) manualMenu.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
            if (stopMenu != null) stopMenu.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
            if (startMenu != null) startMenu.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
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

    public class StartupItem
    {
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
        public string RegistryRoot { get; set; } = "HKCU";  // HKCU 或 HKLM
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