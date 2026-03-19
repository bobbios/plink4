using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace plink4
{
    internal static class Logger
    {
        public static void EnsureFolders()
        {
            EnsureFolder(AppConfig.LogPath);
            EnsureFolder(AppConfig.OutResponse);
            EnsureFolder(AppConfig.OutResponse2);
            EnsureFolder(AppConfig.BatchResponse);
            EnsureFolder(AppConfig.LastTransactionResponse);
        }

        public static void LogStartup(string[] args)
        {
            Info("-----------------------------");
            Info("Start plink4");
            Info("Args count = " + (args == null ? 0 : args.Length));

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                    Info($"args[{i}] = '{args[i]}'");
            }
        }

        public static void Info(string msg,
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
            var location = fileName + "." + member + "." + line;

            var lineText =
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                "  " + level + ": " +
                location + "  " +
                msg +
                Environment.NewLine;

            File.AppendAllText(AppConfig.LogPath, lineText);
            Console.WriteLine(lineText);
        }

        private static void EnsureFolder(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}