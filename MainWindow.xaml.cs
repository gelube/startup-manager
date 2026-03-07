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
            
            // 缁戝畾鍙抽敭鑿滃崟 Opening 浜嬩欢
            if (StartupGrid.ContextMenu != null)
            {
                StartupGrid.ContextMenu.ContextMenuOpening += ContextMenu_Opening;
            }
        }

        private void InitializeCategories()
        {
            categories = new ObservableCollection<CategoryItem>
            {
                new CategoryItem { Icon = "馃搧", Name = "鍚姩鏂囦欢澶?, Count = 0, Key = "Folder", Type = "Folder" },
                new CategoryItem { Icon = "馃搮", Name = "璁″垝浠诲姟", Count = 0, Key = "Task", Type = "Task" },
                new CategoryItem { Icon = "馃敡", Name = "娉ㄥ唽琛?, Count = 0, Key = "Registry", Type = "Registry" },
                new CategoryItem { Icon = "鈿欙笍", Name = "Windows 鏈嶅姟", Count = 0, Key = "Service", Type = "Service" }
            };
            CategoryTree.ItemsSource = categories;
        }

        private void LoadAllItems()
        {
            startupItems.Clear();
            LoadFromRegistry(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "褰撳墠鐢ㄦ埛");
            LoadFromRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "绯荤粺鍏ㄥ眬");
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
                        StatusText = "宸插惎鐢?,
                        CategoryType = "Registry"
                    });
                }
            }
            catch (Exception ex) { LogError($"娉ㄥ唽琛ㄥ姞杞藉け璐ワ細{ex.Message}"); }
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
                            Location = "褰撳墠鐢ㄦ埛",
                            Source = "Folder",
                            IsEnabled = true,
                            Publisher = "鍚姩鏂囦欢澶?,
                            StartupImpact = "浣?,
                            StatusText = "宸插惎鐢?,
                            CategoryType = "Folder"
                        });
                    }
                }
            }
            catch (Exception ex) { LogError($"鍚姩鏂囦欢澶瑰姞杞藉け璐ワ細{ex.Message}"); }
        }

        private void LoadFromTaskScheduler()
        {
            try
            {
                var startInfo = new ProcessStartInfo { FileName = "schtasks", Arguments = "/query /fo CSV /nh", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
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
                            if (taskName.Contains("Microsoft") || taskName.Contains("OneDrive") || taskName.Contains("Windows") || taskName.Contains("Office") || taskName.Contains("Adobe") || taskName.Contains("Activation") || taskName.Contains("S-") || taskName.Contains("{") || taskName.Contains("Svc") || taskName.Contains("Platform")) continue;
                            startupItems.Add(new StartupItem { Name = $"浠诲姟 {taskName}", Path = "N/A", Location = "璁″垝浠诲姟", Source = "Task", IsEnabled = true, Publisher = "璁″垝浠诲姟", StartupImpact = "涓?, StatusText = "宸插惎鐢?, CategoryType = "Task" });
                        }
                    }
                }
            }
            catch (Exception ex) { LogError($"璁″垝浠诲姟鍔犺浇澶辫触锛歿ex.Message}"); }
        }

        private void LoadServices()
        {
            try
            {
                var startInfo = new ProcessStartInfo { FileName = "sc", Arguments = "query type= service state= all", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
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
                            if (!string.IsNullOrEmpty(currentServiceName)) AddServiceItem(currentServiceName, currentDisplayName, currentPath, startType, isRunning);
                            currentServiceName = line.Substring(13).Trim();
                            currentDisplayName = ""; currentPath = ""; startType = ""; isRunning = false;
                        }
                        else if (line.StartsWith("DISPLAY_NAME")) { var parts = line.Split(':', 2); if (parts.Length > 1) currentDisplayName = parts[1].Trim(); }
                        else if (line.StartsWith("BINARY_PATH_NAME")) { var parts = line.Split(':', 2); if (parts.Length > 1) currentPath = parts[1].Trim().Trim('"'); }
                        else if (line.Contains("START_TYPE"))
                        {
                            if (line.Contains("AUTO_START")) startType = "AUTO_START";
                            else if (line.Contains("DEMAND_START")) startType = "DEMAND_START";
                            else if (line.Contains("DISABLED")) startType = "DISABLED";
                        }
                        else if (line.Contains("STATE")) { isRunning = line.Contains("RUNNING"); }
                    }
                    if (!string.IsNullOrEmpty(currentServiceName)) AddServiceItem(currentServiceName, currentDisplayName, currentPath, startType, isRunning);
                }
            }
            catch (Exception ex) { LogError($"鏈嶅姟鍔犺浇澶辫触锛歿ex.Message}"); }
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
                    status = "宸茬鐢?;
                    isEnabled = false;
                }
                else if (startType == "DEMAND_START")
                {
                    status = isRunning ? "宸茶繍琛岋紙鎵嬪姩锛? : "宸插仠姝紙鎵嬪姩锛?;
                    isEnabled = isRunning;
                }
                else if (startType == "AUTO_START")
                {
                    status = isRunning ? "宸茶繍琛岋紙鑷姩锛? : "宸插仠姝紙鑷姩锛?;
                    isEnabled = true;
                }
                else
                {
                    status = isRunning ? "宸茶繍琛? : "宸插仠姝?;
                    isEnabled = isRunning;
                }
                
                startupItems.Add(new StartupItem
                {
                    Name = $"鏈嶅姟 {displayText}",
                    Path = string.IsNullOrEmpty(path) ? "N/A" : path,
                    Location = "绯荤粺鏈嶅姟",
                    Source = "Service",
                    IsEnabled = isEnabled,
                    Publisher = "Windows 鏈嶅姟",
                    StartupImpact = "楂?,
                    StatusText = status,
                    CategoryType = "Service",
                    ServiceName = serviceName
                });
            }
            catch (Exception ex) { LogError($"娣诲姞鏈嶅姟椤瑰け璐?{serviceName}: {ex.Message}"); }
        }

        private string GetPublisher(string path) { try { if (File.Exists(path)) { var versionInfo = FileVersionInfo.GetVersionInfo(path); return versionInfo.CompanyName ?? "鏈煡"; } } catch { } return "鏈煡"; }
        private string GetStartupImpact(string path) { try { if (!File.Exists(path)) return "鏈煡"; var fileInfo = new FileInfo(path); if (fileInfo.Length > 50 * 1024 * 1024) return "楂?; if (fileInfo.Length > 10 * 1024 * 1024) return "涓?; return "浣?; } catch { return "鏈煡"; } }

        private void UpdateCounts()
        {
            foreach (var cat in categories) { cat.Count = cat.Key switch { "All" => startupItems.Count, "Folder" => startupItems.Count(i => i.Source == "Folder"), "Task" => startupItems.Count(i => i.Source == "Task"), "Registry" => startupItems.Count(i => i.Source == "Registry"), "Service" => startupItems.Count(i => i.Source == "Service"), _ => 0 }; }
            CategoryTree.ItemsSource = null;
            CategoryTree.ItemsSource = categories;
        }

        private void UpdateStatusBar() { StatusTotal.Text = $"鍏?{startupItems.Count} 椤?; }
        private void FilterItems() { var filtered = currentCategory switch { "Folder" => startupItems.Where(i => i.Source == "Folder"), "Task" => startupItems.Where(i => i.Source == "Task"), "Registry" => startupItems.Where(i => i.Source == "Registry"), "Service" => startupItems.Where(i => i.Source == "Service"), _ => startupItems }; StartupGrid.ItemsSource = filtered.ToList(); }
        private void Refresh_Click(object sender, RoutedEventArgs e) { try { LoadAllItems(); } catch (Exception ex) { LogError($"鍒锋柊澶辫触锛歿ex.Message}"); } }
        private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) { if (CategoryTree.SelectedItem is CategoryItem category) { currentCategory = category.Key; FilterItems(); } }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { var searchTerm = SearchBox?.Text?.ToLower() ?? ""; if (string.IsNullOrEmpty(searchTerm)) { FilterItems(); return; } var filtered = startupItems.Where(i => (!string.IsNullOrEmpty(i.Name) && i.Name.ToLower().Contains(searchTerm)) || (!string.IsNullOrEmpty(i.Publisher) && i.Publisher.ToLower().Contains(searchTerm)) || (!string.IsNullOrEmpty(i.Path) && i.Path.ToLower().Contains(searchTerm))).ToList(); StartupGrid.ItemsSource = filtered; }
        private void ClearSearch_Click(object sender, RoutedEventArgs e) { if (SearchBox != null) SearchBox.Text = ""; FilterItems(); }
        private void Enable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info($"Enable_Click 寮€濮嬶紝閫変腑{StartupGrid?.SelectedItems?.Count ?? 0}椤?);
                if (StartupGrid?.SelectedItems == null || StartupGrid.SelectedItems.Count == 0)
                {
                    Logger.Info("娌℃湁閫変腑椤?);
                    MessageBox.Show("璇峰厛閫夋嫨瑕佸惎鐢ㄧ殑椤圭洰", "鎻愮ず", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                foreach (var item in StartupGrid.SelectedItems.Cast<StartupItem>())
                {
                    Logger.Info($"澶勭悊锛歿item.Name}, CategoryType={item.CategoryType}");
                    
                    // 鏍规嵁 item.CategoryType 鎵ц涓嶅悓鐨勫惎鐢ㄦ搷浣?                    switch (item.CategoryType)
                    {
                        case "Folder":  // 鍚姩鏂囦欢澶癸細鏃犳硶鐪熸鍚敤/绂佺敤锛屽彧鏄爣璁?                            item.IsEnabled = true;
                            item.StatusText = "宸插惎鐢?;
                            Logger.Info($"鏂囦欢澶癸細{item.Name} 鏍囪涓哄凡鍚敤");
                            break;
                        
                        case "Task":    // 璁″垝浠诲姟锛氬惎鐢ㄤ换鍔?                            var taskName = item.Name.Replace("浠诲姟 ", "");
                            Logger.Info($"璁″垝浠诲姟锛氭墽琛?schtasks /Change /TN \"{taskName}\" /Enable");
                            RunCommand("schtasks", $"/Change /TN \"{taskName}\" /Enable");
                            item.IsEnabled = true;
                            item.StatusText = "宸插惎鐢?;
                            break;
                        
                        case "Registry":  // 娉ㄥ唽琛細宸茬粡鏄惎鐢ㄧ姸鎬?                            item.IsEnabled = true;
                            item.StatusText = "宸插惎鐢?;
                            Logger.Info($"娉ㄥ唽琛細{item.Name} 鏍囪涓哄凡鍚敤");
                            break;
                    }
                }
                Logger.Info("Enable_Click 瀹屾垚锛屽埛鏂版暟鎹?);
                LoadAllItems();  // 鍒锋柊鎵€鏈夋暟鎹紝纭繚鐘舵€佸悓姝?            }
            catch (Exception ex)
            {
                Logger.Error($"鍚敤澶辫触锛歿ex.Message}");
                MessageBox.Show($"鍚敤澶辫触锛歿ex.Message}", "閿欒", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void Disable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StartupGrid?.SelectedItems == null || StartupGrid.SelectedItems.Count == 0) return;
                
                foreach (var item in StartupGrid.SelectedItems.Cast<StartupItem>())
                {
                    // 鏍规嵁 item.CategoryType 鎵ц涓嶅悓鐨勭鐢ㄦ搷浣?                    switch (item.CategoryType)
                    {
                        case "Folder":  // 鍚姩鏂囦欢澶癸細鏃犳硶鐪熸鍚敤/绂佺敤锛屽彧鏄爣璁?                            item.IsEnabled = false;
                            item.StatusText = "宸茬鐢?;
                            break;
                        
                        case "Task":    // 璁″垝浠诲姟锛氱鐢ㄤ换鍔?                            var taskName = item.Name.Replace("浠诲姟 ", "");
                            RunCommand("schtasks", $"/Change /TN \"{taskName}\" /Disable");
                            item.IsEnabled = false;
                            item.StatusText = "宸茬鐢?;
                            break;
                        
                        case "Registry":  // 娉ㄥ唽琛細宸茬粡鏄惎鐢ㄧ姸鎬?                            item.IsEnabled = false;
                            item.StatusText = "宸茬鐢?;
                            break;
                    }
                }
                LoadAllItems();  // 鍒锋柊鎵€鏈夋暟鎹紝纭繚鐘舵€佸悓姝?            }
            catch (Exception ex) { LogError($"绂佺敤澶辫触锛歿ex.Message}"); }
        }
        
        // 閫氱敤鍛戒护鎵ц
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
                    if (!string.IsNullOrEmpty(error)) LogError($"鍛戒护鎵ц閿欒锛歿error}");
                }
            }
            catch (Exception ex)
            {
                LogError($"鍛戒护鎵ц澶辫触锛歿file} {args} - {ex.Message}");
                throw;
            }
        }
        
        // 鍙抽敭鑿滃崟鏈嶅姟鍔熻兘锛堝搴旂郴缁熸湇鍔＄鐞嗗洓涓姛鑳斤級
        private void ServiceAuto_Click(object sender, RoutedEventArgs e) { try { if (StartupGrid?.SelectedItem is StartupItem item && item.Source == "Service") { var serviceName = ExtractServiceName(item.Name); SetServiceAutoStart(serviceName, true); LoadAllItems(); } } catch { } }  // 鍚姩绫诲瀷锛氳嚜鍔?        private void ServiceManual_Click(object sender, RoutedEventArgs e) { try { if (StartupGrid?.SelectedItem is StartupItem item && item.Source == "Service") { var serviceName = ExtractServiceName(item.Name); var startInfo = new ProcessStartInfo { FileName = "sc", Arguments = $"config \"{serviceName}\" start= demand", UseShellExecute = false, CreateNoWindow = true, Verb = "runas" }; using (var process = Process.Start(startInfo)) { process.WaitForExit(); } LoadAllItems(); } } catch { } }  // 鍚姩绫诲瀷锛氭墜鍔?        private void ServiceStop_Click(object sender, RoutedEventArgs e) { try { if (StartupGrid?.SelectedItem is StartupItem item && item.Source == "Service") { var serviceName = ExtractServiceName(item.Name); var startInfo = new ProcessStartInfo { FileName = "net", Arguments = $"stop \"{serviceName}\"", UseShellExecute = false, CreateNoWindow = true, Verb = "runas" }; using (var process = Process.Start(startInfo)) { process.WaitForExit(); } LoadAllItems(); } } catch { } }  // 鍋滄鏈嶅姟
        private void ServiceStart_Click(object sender, RoutedEventArgs e) { try { if (StartupGrid?.SelectedItem is StartupItem item && item.Source == "Service") { var serviceName = ExtractServiceName(item.Name); var startInfo = new ProcessStartInfo { FileName = "net", Arguments = $"start \"{serviceName}\"", UseShellExecute = false, CreateNoWindow = true, Verb = "runas" }; using (var process = Process.Start(startInfo)) { process.WaitForExit(); } LoadAllItems(); } } catch { } }  // 鍚姩鏈嶅姟
        
        private void Delete_Click(object sender, RoutedEventArgs e) { try { if (StartupGrid?.SelectedItems == null || StartupGrid.SelectedItems.Count == 0) return; var result = MessageBox.Show($"纭畾瑕佸垹闄ら€変腑鐨?{StartupGrid.SelectedItems.Count} 涓惎鍔ㄩ」鍚楋紵", "纭鍒犻櫎", MessageBoxButton.YesNo, MessageBoxImage.Warning); if (result == MessageBoxResult.Yes) { var toRemove = StartupGrid.SelectedItems.Cast<StartupItem>().ToList(); foreach (var item in toRemove) { bool deleted = item.Source switch { "Registry" => DeleteFromRegistry(item), "Folder" => DeleteFromFolder(item), "Task" => DeleteFromTaskScheduler(item), "Service" => DeleteService(item), _ => false }; if (deleted) startupItems.Remove(item); } UpdateCounts(); UpdateStatusBar(); FilterItems(); } } catch { } }
        private bool DeleteFromRegistry(StartupItem item) { try { using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true); if (key != null && key.GetValue(item.Name) != null) { key.DeleteValue(item.Name); return true; } } catch (Exception ex) { LogError($"鍒犻櫎娉ㄥ唽琛ㄥ惎鍔ㄩ」澶辫触锛歿ex.Message}"); } return false; }
        private bool DeleteFromFolder(StartupItem item) { try { var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup); var shortcutPath = Path.Combine(startupPath, $"{item.Name}.lnk"); if (File.Exists(shortcutPath)) { File.Delete(shortcutPath); return true; } } catch (Exception ex) { LogError($"鍒犻櫎鍚姩鏂囦欢澶瑰揩鎹锋柟寮忓け璐ワ細{ex.Message}"); } return false; }
        private bool DeleteFromTaskScheduler(StartupItem item) { try { var taskName = item.Name.Replace("浠诲姟 ", ""); var startInfo = new ProcessStartInfo { FileName = "schtasks", Arguments = $"/Delete /TN \"{taskName}\" /F", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, Verb = "runas" }; using (var process = Process.Start(startInfo)) { process.WaitForExit(); return process.ExitCode == 0; } } catch (Exception ex) { LogError($"鍒犻櫎璁″垝浠诲姟澶辫触锛歿ex.Message}"); } return false; }
        private bool DeleteService(StartupItem item) { try { var serviceName = ExtractServiceName(item.Name); var stopStartInfo = new ProcessStartInfo { FileName = "sc", Arguments = $"stop \"{serviceName}\"", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, Verb = "runas" }; using (var stopProcess = Process.Start(stopStartInfo)) { stopProcess.WaitForExit(); } System.Threading.Thread.Sleep(1000); var deleteStartInfo = new ProcessStartInfo { FileName = "sc", Arguments = $"delete \"{serviceName}\"", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, Verb = "runas" }; using (var deleteProcess = Process.Start(deleteStartInfo)) { deleteProcess.WaitForExit(); return deleteProcess.ExitCode == 0; } } catch (Exception ex) { LogError($"鍒犻櫎鏈嶅姟澶辫触锛歿ex.Message}"); } return false; }
        private string ExtractServiceName(string displayName) { try { var start = displayName.LastIndexOf('('); var end = displayName.LastIndexOf(')'); if (start >= 0 && end > start) return displayName.Substring(start + 1, end - start - 1); return displayName.Replace("鏈嶅姟 ", "").Trim(); } catch { return displayName.Replace("鏈嶅姟 ", "").Trim(); } }
        private bool SetServiceAutoStart(string serviceName, bool enable) { try { var startInfo = new ProcessStartInfo { FileName = "sc", Arguments = $"config \"{serviceName}\" start= {(enable ? "auto" : "disabled")}", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, Verb = "runas" }; using (var process = Process.Start(startInfo)) { process.WaitForExit(); return process.ExitCode == 0; } } catch (Exception ex) { LogError($"璁剧疆鏈嶅姟鍚姩绫诲瀷澶辫触锛歿ex.Message}"); return false; } }
        private void AddNew_Click(object sender, RoutedEventArgs e) { try { var dialog = new AddStartupDialog(this); dialog.SetDefaultType(currentCategory); bool? result = dialog.ShowDialog(); if (result == true) LoadAllItems(); } catch (Exception ex) { MessageBox.Show($"娣诲姞澶辫触锛歿ex.Message}", "閿欒", MessageBoxButton.OK, MessageBoxImage.Error); } }
        private void OpenLocation_Click(object sender, RoutedEventArgs e) { try { if (StartupGrid.SelectedItem is StartupItem item && !string.IsNullOrEmpty(item.Path) && File.Exists(item.Path)) Process.Start("explorer.exe", $"/select,\"{item.Path}\""); } catch (Exception ex) { Debug.WriteLine($"[OpenLocation_Click] Error: {ex.Message}"); } }
        private void Properties_Click(object sender, RoutedEventArgs e) { try { if (StartupGrid.SelectedItem is StartupItem item) { DetailName.Text = item.Name; DetailPublisher.Text = item.Publisher; DetailType.Text = item.Location; } } catch (Exception ex) { Debug.WriteLine($"[Properties_Click] Error: {ex.Message}"); } }
        
        // 鍙抽敭鑿滃崟鎵撳紑鏃讹紝鏍规嵁褰撳墠鍒嗙被鏄剧ず/闅愯棌鍔熻兘
        private void ContextMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            // 鑾峰彇褰撳墠閫変腑鐨勯」鏉ュ垽鏂垎绫?            var selectedItem = StartupGrid.SelectedItem as StartupItem;
            if (selectedItem == null) return;
            
            bool isService = (selectedItem.Source == "Service");
            
            // 鐩存帴閫氳繃 x:Name 鏌ユ壘鑿滃崟椤?            var contextMenu = sender as ContextMenu;
            if (contextMenu == null) return;
            
            // 璁剧疆鑿滃崟椤瑰彲瑙佹€?            var enableMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "鍚敤");
            var disableMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "绂佺敤");
            var autoMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "鑷姩");
            var manualMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "鎵嬪姩");
            var stopMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "鍋滄");
            var startMenu = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == "鍚姩");
            
            if (enableMenu != null) enableMenu.Visibility = isService ? Visibility.Collapsed : Visibility.Visible;
            if (disableMenu != null) disableMenu.Visibility = isService ? Visibility.Collapsed : Visibility.Visible;
            if (autoMenu != null) autoMenu.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
            if (manualMenu != null) manualMenu.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
            if (stopMenu != null) stopMenu.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
            if (startMenu != null) startMenu.Visibility = isService ? Visibility.Visible : Visibility.Collapsed;
        }
        private void Help_Click(object sender, RoutedEventArgs e) { MessageBox.Show("鍚姩绠＄悊鍣?Pro v3.0\n\n鍔熻兘锛氱鐞嗘敞鍐岃〃鍚姩椤广€佸惎鍔ㄦ枃浠跺す銆佽鍒掍换鍔°€乄indows 鏈嶅姟\n\nOpenClaw 鍑哄搧", "甯姪", MessageBoxButton.OK, MessageBoxImage.Information); }
        private void Exit_Click(object sender, RoutedEventArgs e) { Close(); }
        private void LogError(string message) { Debug.WriteLine($"[ERROR] {message}"); }
    }

    public class StartupItem
    {
        public bool IsEnabled { get; set; }
        public string Name { get; set; } = "";
        public string Publisher { get; set; } = "鏈煡";
        public string Path { get; set; } = "";
        public string Location { get; set; } = "";
        public string Source { get; set; } = "";
        public string StartupImpact { get; set; } = "鏈煡";
        public string StatusText { get; set; } = "宸茬鐢?;
        public string CategoryType { get; set; } = "";
        public string ServiceName { get; set; } = "";  // 鏈嶅姟鍚嶇О
    }
    public class CategoryItem { public string Icon { get; set; } = ""; public string Name { get; set; } = ""; public int Count { get; set; } public string Key { get; set; } = ""; public string Type { get; set; } = ""; }
}
