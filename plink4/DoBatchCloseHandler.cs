using System;
using System.IO;
using System.Reflection;

namespace plink4
{
    internal static class DoBatchCloseHandler
    {
        public static int Run(ArgsModel model)
        {
            Logger.Info("Starting DoBatchCloseHandler");

            try
            {
                var term = CommandRouter.ConnectTerminal(model);

                var report = PoslinkReflection.GetProperty(term, "Report");
                if (report != null)
                {
                    Logger.Info("report type=" + report.GetType().FullName);

                    foreach (var m in report.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name.IndexOf("Batch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.Name.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.Name.IndexOf("Summary", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.Name.IndexOf("Report", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Logger.Info("  report method: " + m.Name);
                        }
                    }
                }
                else
                {
                    Logger.Info("report = null");
                }

                Logger.Info("term type=" + term.GetType().FullName);

                foreach (var pi in term.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        var v = pi.GetValue(term);
                        Logger.Info("  term." + pi.Name + " = " + (v == null ? "(null)" : v.GetType().FullName));
                    }
                    catch
                    {
                        Logger.Info("  term." + pi.Name + " = (error reading)");
                    }
                }

                object target =
                    PoslinkReflection.GetProperty(term, "Batch") ??
                    PoslinkReflection.GetProperty(term, "Transaction") ??
                    term;

                if (target == null)
                    throw new Exception("Could not find Batch or Transaction object on terminal.");

                Logger.Info("BatchClose target type=" + target.GetType().FullName);

                foreach (var m in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name.IndexOf("Batch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.Name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Logger.Info("  method: " + m.Name);
                    }
                }

                var req = PoslinkReflection.CreateRequest("BatchClose");
                object rsp = PoslinkReflection.CreateResponse("BatchClose");

                Logger.Info("req type=" + req.GetType().FullName);

                PoslinkRequestBuilder.ApplyTrace(req, model.RefNum);

                int rc = PoslinkReflection.InvokeTxMethod(target, "BatchClose", req, ref rsp);

                Logger.Info("BatchClose returned rc=" + rc);


                DumpObject("rsp.TotalCount", GetProp(rsp, "TotalCount"));
                DumpObject("rsp.TotalAmount", GetProp(rsp, "TotalAmount"));
                DumpObject("rsp.TipCount", GetProp(rsp, "TipCount"));
                DumpObject("rsp.TipAmount", GetProp(rsp, "TipAmount"));
                DumpObject("rsp.HostInformation", GetProp(rsp, "HostInformation"));
                DumpObject("rsp.TorInformation", GetProp(rsp, "TorInformation"));


                Logger.Info("rsp type=" + (rsp == null ? "(null)" : rsp.GetType().FullName));

                if (rsp != null)
                {
                    Logger.Info("--- BatchClose RESPONSE PROPERTIES ---");

                    foreach (var pi in rsp.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        try
                        {
                            var val = pi.GetValue(rsp);
                            Logger.Info("  rsp." + pi.Name + " = " + (val == null ? "(null)" : val.ToString()));
                        }
                        catch (Exception ex)
                        {
                            Logger.Info("  rsp." + pi.Name + " = (error: " + ex.Message + ")");
                        }
                    }
                }

                LegacyResponseWriter.WriteDump(rsp);
                WriteBatchCloseResponse(rc, rsp);

                Logger.Info("DoBatchCloseHandler complete rc=" + rc);
                return rc;
            }
            catch (Exception ex)
            {
                Logger.Error("DoBatchCloseHandler fatal: " + ex.Message);
                Logger.Error(ex.ToString());

                WriteBatchCloseResponse(1, null);

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

        private static void WriteBatchCloseResponse(int rc, object rsp)
        {
            string responseCode = GetString(rsp, "ResponseCode");
            string responseMessage = GetString(rsp, "ResponseMessage");

            bool ok = rc == 0 &&
                      (string.IsNullOrWhiteSpace(responseCode) || responseCode == "000000" || responseCode == "0");

            string resultCode = ok ? "0" : FirstNonEmpty(responseCode, rc.ToString());
            string resultTxt = ok ? "OK" : FirstNonEmpty(responseMessage, "ERROR");

            object totalCount = GetProp(rsp, "TotalCount");
            object totalAmount = GetProp(rsp, "TotalAmount");
            object tipCount = GetProp(rsp, "TipCount");
            object tipAmount = GetProp(rsp, "TipAmount");
            object hostInfo = GetProp(rsp, "HostInformation");

            string creditCount = GetString(totalCount, "CreditCount");
            string debitCount = GetString(totalCount, "DebitCount");
            string ebtCount = GetString(totalCount, "EbtCount");

            string creditAmount = GetString(totalAmount, "CreditAmount");
            string debitAmount = GetString(totalAmount, "DebitAmount");
            string ebtAmount = GetString(totalAmount, "EbtAmount");

            string creditTipCount = GetString(tipCount, "CreditTipCount");
            string debitTipCount = GetString(tipCount, "DebitTipCount");

            string creditTipAmount = GetString(tipAmount, "CreditTipAmount");
            string debitTipAmount = GetString(tipAmount, "DebitTipAmount");

            string batchNum = FirstNonEmpty(
                GetString(hostInfo, "BatchNumber"),
                GetString(rsp, "BatchNumber"),
                GetString(rsp, "BatchNo")
            );

            string timeStamp = FirstNonEmpty(GetString(rsp, "LocalDateTime"), GetString(rsp, "TimeStamp"));
            string tid = FirstNonEmpty(GetString(rsp, "TerminalId"), GetString(rsp, "Tid"));
            string mid = GetString(rsp, "Mid");

            string text =
                "ResultCode: " + resultCode + "\r\n" +
                "ResultTxt: " + resultTxt + "\r\n" +
                "ResponseCode: " + responseCode + "\r\n" +
                "ResponseMessage: " + responseMessage + "\r\n" +
                "CreditCount: " + creditCount + "\r\n" +
                "CreditAmount: " + creditAmount + "\r\n" +
                "DebitCount: " + debitCount + "\r\n" +
                "DebitAmount: " + debitAmount + "\r\n" +
                "EbtCount: " + ebtCount + "\r\n" +
                "EbtAmount: " + ebtAmount + "\r\n" +
                "CreditTipCount: " + creditTipCount + "\r\n" +
                "CreditTipAmount: " + creditTipAmount + "\r\n" +
                "DebitTipCount: " + debitTipCount + "\r\n" +
                "DebitTipAmount: " + debitTipAmount + "\r\n" +
                "TimeStamp: " + timeStamp + "\r\n" +
                "Tid: " + tid + "\r\n" +
                "Mid: " + mid + "\r\n" +
                "BatchNumber: " + batchNum + "\r\n";

            var dir = Path.GetDirectoryName(AppConfig.BatchResponse);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(AppConfig.BatchResponse, text);
            Logger.Info("Batch close response written to " + AppConfig.BatchResponse);
        }

        private static object GetProp(object obj, string prop)
        {
            if (obj == null) return null;
            var pi = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
            return pi?.GetValue(obj);
        }

        private static string GetString(object obj, string prop)
        {
            if (obj == null) return "";
            try
            {
                var pi = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null) return "";
                var val = pi.GetValue(obj);
                return val == null ? "" : Convert.ToString(val);
            }
            catch
            {
                return "";
            }
        }


        private static void DumpObject(string label, object obj)
        {
            if (obj == null)
            {
                Logger.Info(label + " = (null)");
                return;
            }

            Logger.Info(label + " type=" + obj.GetType().FullName);

            foreach (var pi in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    var val = pi.GetValue(obj);
                    Logger.Info("  " + label + "." + pi.Name + " = " + (val == null ? "(null)" : val.ToString()));
                }
                catch (Exception ex)
                {
                    Logger.Info("  " + label + "." + pi.Name + " = (error: " + ex.Message + ")");
                }
            }
        }


        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }
            return "";
        }
    }
}