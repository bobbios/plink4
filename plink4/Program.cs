using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace plink4
{
    internal static class AppConfig
    {
        public const int PortAlways = 10009;
        public const int TimeoutMs = 120000;

        public const string LogPath = @"C:\newretail\card\plink4.txt";
        public const string OutResponse = @"C:\newretail\card\response.txt";
        public const string OutResponse2 = @"C:\newretail\card\response2.txt";
    }

    internal static class Logger
    {
        public static void EnsureFolders()
        {
            EnsureFolder(AppConfig.LogPath);
            EnsureFolder(AppConfig.OutResponse);
            EnsureFolder(AppConfig.OutResponse2);
        }

        public static void Info(string msg,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0) => Write("INFO", msg, member, file, line);

        public static void Error(string msg,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0) => Write("ERROR", msg, member, file, line);

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
        }

        private static void EnsureFolder(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }

    internal sealed class ArgsModel
    {
        public string RefNum;
        public string Amount;
        public string CardType;   // CREDIT / DEBIT / EBT_CASHBENEFIT / EBT_FOODSTAMP
        public string TxnType;    // SALE / RETURN / INQUIRY / ADJUST
        public string Ip;
        public string TcpFlag;
        public string ArgPort;
        public string OriginalRef;

        public string PreTipFlag;   // optional in your legacy calls
        public string ApprovalCode; // ADJUST
        public string TransactionId;// ADJUST
    }

    internal static class ArgsParser
    {
        private static string GetArg(string[] args, int i)
            => (args != null && i >= 0 && i < args.Length && args[i] != null) ? args[i].Trim() : "";

        public static bool TryParse(string[] args, out ArgsModel m, out string err)
        {
            m = null;
            err = null;

            // ✅ Your Paradox call is 7 args:
            // ref amount cardType txnType ip tcpFlag port
            if (args == null || args.Length < 7)
            {
                err = "Missing required arguments. Expected: ref amount cardType txnType ip tcpFlag port [origRef] [preTipFlag] [approvalCode] [transactionId]";
                return false;
            }

            string refNum = GetArg(args, 0);
            string amount = GetArg(args, 1);
            string cardType = GetArg(args, 2).ToUpperInvariant();
            string txnType = GetArg(args, 3).ToUpperInvariant();
            string ip = GetArg(args, 4);
            string tcpFlag = GetArg(args, 5);
            string argPort = GetArg(args, 6);

            // optional
            string origRef = GetArg(args, 7);

            // legacy optional extras (plink3 had pretip etc)
            string preTip = (GetArg(args, 8) == "1") ? "1" : "0";
            string appr = GetArg(args, 9);
            string transId = GetArg(args, 10);

            // ADJUST shorter layout: args[8]=ApprovalCode, args[9]=TransactionId
            if (txnType == "ADJUST")
            {
                preTip = "0";
                appr = GetArg(args, 8);
                transId = GetArg(args, 9);
            }

            m = new ArgsModel
            {
                RefNum = refNum,
                Amount = amount,
                CardType = cardType,
                TxnType = txnType,
                Ip = ip,
                TcpFlag = tcpFlag,
                ArgPort = argPort,
                OriginalRef = origRef,
                PreTipFlag = preTip,
                ApprovalCode = appr,
                TransactionId = transId
            };

            // ✅ add RefNum validation (important)
            if (string.IsNullOrEmpty(m.RefNum)) { err = "RefNum missing"; return false; }
            if (string.IsNullOrEmpty(m.Amount)) { err = "Amount missing"; return false; }
            if (string.IsNullOrEmpty(m.CardType)) { err = "CardType missing"; return false; }
            if (string.IsNullOrEmpty(m.TxnType)) { err = "TxnType missing"; return false; }
            if (string.IsNullOrEmpty(m.Ip)) { err = "IP missing"; return false; }

            return true;
        }
    }

    internal class Program
    {
        static int Main(string[] args)
        {
            Logger.EnsureFolders();
            Logger.Info("-----------------------------");
            Logger.Info("Start plink4");
            Logger.Info("Args count = " + (args == null ? 0 : args.Length));
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                    Logger.Info($"args[{i}] = '{args[i]}'");
            }

            if (!ArgsParser.TryParse(args, out var model, out var err))
            {
                Logger.Error(err);
                LegacyResponseWriter.WriteLegacy(model?.CardType ?? "", model?.TxnType ?? "", ok: false,
                    responseMessage: err, responseCode: "ERR", authCode: "");
                return 2;
            }

            Logger.Info($"ref={model.RefNum} amt={model.Amount} cardType={model.CardType} type={model.TxnType} ip={model.Ip} tcpFlag={model.TcpFlag} argPort={model.ArgPort} origRef={model.OriginalRef} preTip={model.PreTipFlag}");

            try
            {
                // connect
                var term = PoslinkSemi.ConnectTcp(model.Ip, AppConfig.PortAlways, AppConfig.TimeoutMs);

                // route
                int rc;
                object rspObj;

                if (string.Equals(model.TxnType, "ADJUST", StringComparison.OrdinalIgnoreCase))
                {
                    // leave as stub unless you want it now
                    rc = 9;
                    LegacyResponseWriter.WriteLegacy(model.CardType, model.TxnType, ok: false,
                        responseMessage: "ADJUST not wired in plink4 test harness yet", responseCode: "NA", authCode: "");
                    return rc;
                }

                if (model.CardType == "CREDIT")
                    rc = PoslinkSemi.DoCredit(term, model, out rspObj);
                else if (model.CardType == "DEBIT")
                    rc = PoslinkSemi.DoDebit(term, model, out rspObj);
                else if (model.CardType == "EBT_CASHBENEFIT" || model.CardType == "EBT_FOODSTAMP")
                    rc = PoslinkSemi.DoEbt(term, model, out rspObj);
                else
                {
                    LegacyResponseWriter.WriteLegacy(model.CardType, model.TxnType, ok: false,
                        responseMessage: "Unsupported card type: " + model.CardType, responseCode: "BAD", authCode: "");
                    return 6;
                }

                // Always write response2 dump for mapping
                LegacyResponseWriter.WriteDump(rspObj);

                // Write legacy response.txt in your Paradox-friendly layout
                LegacyResponseWriter.WriteFromRsp(model.CardType, model.TxnType, rc == 0, rspObj);

                Logger.Info("Done. rc=" + rc);
                return rc;
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal: " + ex.Message);
                Logger.Error(ex.ToString());
                LegacyResponseWriter.WriteLegacy(model.CardType, model.TxnType, ok: false,
                    responseMessage: ex.Message, responseCode: "EX", authCode: "");
                return 1;
            }
        }
    }
}