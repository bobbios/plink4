using System;
using System.IO;
using System.Reflection;

namespace plink4
{
    internal static class DoBatchCloseHandler
    {
        public static int Run(ArgsModel model)
        {
            try
            {
                object terminal = CommandRouter.ConnectTerminal(model)
                    ?? throw new InvalidOperationException("Terminal connection failed.");

                object target = PoslinkReflection.GetProperty(terminal, "Batch")
                              ?? PoslinkReflection.GetProperty(terminal, "Transaction")
                              ?? terminal;

                if (target == null)
                    throw new InvalidOperationException("Could not find Batch or Transaction object on terminal.");

                object req = PoslinkReflection.CreateRequest("BatchClose");
                object rsp = PoslinkReflection.CreateResponse("BatchClose");

                PoslinkRequestBuilder.ApplyTrace(req, model.RefNum);

                int rc = PoslinkReflection.InvokeTxMethod(target, "BatchClose", req, ref rsp);

                LegacyResponseWriter.WriteDump(rsp);
                WriteBatchCloseResponse(rc, rsp);

                return rc;
            }
            catch (Exception ex)
            {
                WriteBatchCloseResponse(1, null);

                LegacyResponseWriter.WriteLegacy(
                    model?.CardType ?? "",
                    model?.TxnType ?? "",
                    false,
                    ex.Message,
                    "EX",
                    ""
                );

                return 1;
            }
        }

        private static void WriteBatchCloseResponse(int rc, object rsp)
        {
            string responseCode = GetString(rsp, "ResponseCode");
            string responseMessage = GetString(rsp, "ResponseMessage");

            bool ok = rc == 0 &&
                      (string.IsNullOrWhiteSpace(responseCode) ||
                       responseCode == "000000" ||
                       responseCode == "0");

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

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v))
                    return v;

            return "";
        }
    }
}