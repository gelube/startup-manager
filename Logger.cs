using System;
using System.IO;
using System.Reflection;

namespace StartupManagerPro
{
    public static class Logger
    {
        private static string logFile;
        public static string LogFile 
        { 
            get 
            {
                if (logFile == null)
                {
                    // 单文件模式下使用 AppContext.BaseDirectory
                    var baseDir = AppContext.BaseDirectory;
                    logFile = Path.Combine(baseDir, "startup-manager.log");
                }
                return logFile;
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", $"{message} - {ex?.Message}\nStack: {ex?.StackTrace}");
        }

        public static void Warning(string message)
        {
            Write("WARN", message);
        }

        private static void Write(string level, string message)
        {
            try
            {
                var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}\n";
                File.AppendAllText(LogFile, logLine);
            }
            catch { }
        }
    }
}
