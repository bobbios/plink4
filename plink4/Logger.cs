using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace plink4
{
    internal static class Logger
    {
        // Fixed subfolder for **only** the daily program logs
        private const string LogFolderName = "log";

        // Full path to log folder: c:\newretail\card\log\
        private static readonly string LogDirectory = Path.Combine(
            @"c:\newretail\card",
            LogFolderName);

        public static void EnsureFolders()
        {
            // Create c:\newretail\card\log\ if it doesn't exist
            if (!Directory.Exists(LogDirectory))
                Directory.CreateDirectory(LogDirectory);

            // Keep ensuring the other output folders (they stay in their original places)
            EnsureFolder(AppConfig.OutResponse);
            EnsureFolder(AppConfig.OutResponse2);
            EnsureFolder(AppConfig.BatchResponse);
            EnsureFolder(AppConfig.LastTransactionResponse);
        }

        public static void LogStartup(string[] args)
        {
            Info("*****************************");
            Info("*****************************");
        }

        public static void Info(string msg,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            Write("INFO", msg, member, file, line);
        }
        public static void Debug(string msg,
    [CallerMemberName] string member = "",
    [CallerFilePath] string file = "",
    [CallerLineNumber] int line = 0)
        {
            Write("INFO", msg, member, file, line);
        }
        public static void Error(string msg,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            Write("ERROR", msg, member, file, line);
        }

        private static void Write(string level, string msg, string member, string file, int line)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var location = $"{fileName}.{member}.{line}";

            // Daily file **inside** the log folder
            string datePart = DateTime.Now.ToString("yy-MM-dd");
            string logFileName = $"plink4-{datePart}.txt";
            string logFilePath = Path.Combine(LogDirectory, logFileName);

            var lineText = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level}: {location} {msg}{Environment.NewLine}";

            try
            {
                // Ensure log folder exists
                Directory.CreateDirectory(LogDirectory);

                // Append to daily log file inside log folder
                File.AppendAllText(logFilePath, lineText);

                // Write to console
                Console.WriteLine(lineText);
            }
            catch (Exception ex)
            {
                // Fallback: console only
                Console.WriteLine($"[LOGGER ERROR] Cannot write to {logFilePath}: {ex.Message}");
                Console.WriteLine(lineText);
            }
        }

        private static void EnsureFolder(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}