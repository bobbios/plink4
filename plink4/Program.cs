using System;
using System.IO;
using System.Reflection;

namespace plink4
{
    internal class Program
    {
        static int Main(string[] args)
        {
            Logger.EnsureFolders();
            //   LoadPosLinkDlls();
            Logger.LogStartup(args);

            ArgsModel model = new ArgsModel();

            try
            {
                if (args == null || args.Length == 0)
                    throw new Exception("No arguments supplied.");

                model.RefNum = args.Length > 0 ? (args[0] ?? "").Trim() : "";
                model.Amount = args.Length > 1 ? (args[1] ?? "").Trim() : "";
                model.CardType = args.Length > 2 ? (args[2] ?? "").Trim().ToUpperInvariant() : "";
                model.Command = model.CardType;
                model.TxnType = args.Length > 3 ? (args[3] ?? "").Trim().ToUpperInvariant() : "";
                model.Ip = args.Length > 4 ? (args[4] ?? "").Trim() : "";
                model.TcpFlag = args.Length > 5 ? (args[5] ?? "").Trim().ToUpperInvariant() : "";
                model.ArgPort = args.Length > 6 ? (args[6] ?? "").Trim() : "";
                model.OriginalRef = args.Length > 7 ? (args[7] ?? "").Trim() : "";
                model.TransactionId = args.Length > 8 ? (args[8] ?? "").Trim() : "";
                model.Surcharge = args.Length > 9 ? (args[9] ?? "").Trim() : "";
                model.PreTipFlag = args.Length > 10 && (args[10] ?? "").Trim() == "1" ? "1" : "0";
                model.ApprovalCode = args.Length > 11 ? (args[11] ?? "").Trim() : "";

                if (string.IsNullOrWhiteSpace(model.RefNum))
                    throw new Exception("RefNum missing.");

                if (string.IsNullOrWhiteSpace(model.CardType))
                    throw new Exception("CardType missing.");

                if (string.IsNullOrWhiteSpace(model.TxnType))
                    throw new Exception("TxnType missing.");

                if (string.IsNullOrWhiteSpace(model.Ip))
                    throw new Exception("IP missing.");

                return CommandRouter.Execute(model);
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal: " + ex.Message);
                Logger.Error(ex.ToString());

                LegacyResponseWriter.WriteLegacy(
                    model.CardType ?? "",
                    model.TxnType ?? "",
                    ok: false,
                    responseMessage: ex.Message,
                    responseCode: "EX",
                    authCode: ""
                );

                return 1;
            }
        }

        private static void LoadPosLinkDlls()
        {
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var dlls = new[]
            {
                "POSLinkCore.dll",
                "POSLinkSemiIntegration.dll",
                "POSLinkAdmin.dll",
                "POSLinkUart.dll"
            };

            foreach (var dll in dlls)
            {
                var path = Path.Combine(baseDir, dll);
                if (File.Exists(path))
                {
                    try
                    {
                        Assembly.LoadFrom(path);
                        Logger.Info("DLL loaded: " + dll);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("DLL load FAILED: " + dll + " -> " + ex.Message);
                    }
                }
                else
                {
                    Logger.Error("DLL not found: " + path);
                }
            }
        }
    }
}