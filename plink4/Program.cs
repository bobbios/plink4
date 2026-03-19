using System;
using System.IO;
using System.Reflection;

namespace plink4
{
    internal class Program
    {
        static int Main(string[] args)
        {
            // EnsureFolders first so Logger can write, then load DLLs, then log startup
            Logger.EnsureFolders();
      //      LoadPosLinkDlls();
            Logger.LogStartup(args);

            if (!ArgsParser.TryParse(args, out var model, out var err))
            {
                Logger.Error(err);

                LegacyResponseWriter.WriteLegacy(
                    model?.CardType ?? "",
                    model?.TxnType ?? "",
                    ok: false,
                    responseMessage: err,
                    responseCode: "ERR",
                    authCode: ""
                );

                return 2;
            }

            try
            {
                return CommandRouter.Execute(model);
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal: " + ex.Message);
                Logger.Error(ex.ToString());

                LegacyResponseWriter.WriteLegacy(
                    model.CardType,
                    model.TxnType,
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