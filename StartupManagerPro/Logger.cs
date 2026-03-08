using System;
using System.Diagnostics;

namespace StartupManagerPro
{
    public static class Logger
    {
        public static void Info(string message) => Debug.WriteLine($"[INFO] {message}");
        public static void Error(string message, Exception ex = null) => Debug.WriteLine($"[ERROR] {message} - {ex?.Message}");
    }
}