using System.Windows;
using System.IO;

namespace StartupManagerPro
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // 测试日志系统
            var baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
            var testLog = Path.Combine(baseDir, "startup-manager.log");
            try
            {
                File.AppendAllText(testLog, $"[{System.DateTime.Now}] App 启动 - BaseDir: {baseDir}\n");
            }
            catch { }
        }
    }
}
