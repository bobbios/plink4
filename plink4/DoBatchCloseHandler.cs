using System;
using System.IO;
using System.Reflection;

namespace plink4
{
    internal static class DoBatchCloseHandler
    {
        private sealed class BatchCloseResult
        {
            public int Rc;
            public object Rsp;
            public bool UsedForceClose;
        }

        public static int Run(ArgsModel model)
        {
            try
            {
                int rc = TransactionUiRunner.RunWithDialog(model, "Closing batch...\nPlease wait.",
                    (object terminal, out object result) =>
                    {
                        object target = PoslinkReflection.GetProperty(terminal, "Batch")
                                      ?? PoslinkReflection.GetProperty(terminal, "Transaction")
                                      ?? terminal;

                        if (target == null)
                            throw new InvalidOperationException("Could not find Batch or Transaction object on terminal.");

                        object req = PoslinkReflection.CreateRequest("BatchClose");
                        object rsp = PoslinkReflection.CreateResponse("BatchClose");

                        PoslinkRequestBuilder.ApplyTrace(req, model.RefNum);

                        int rcInner = PoslinkReflection.InvokeTxMethod(target, "BatchClose", req, ref rsp);
                        bool usedForceClose = false;

                        if (rcInner != 0)
                        {
                            Logger.Info("BatchClose failed (rc=" + rcInner + ", ResponseMessage=" +
                                GetString(rsp, "ResponseMessage") + "); retrying with ForceBatchClose.");

                            object forceReq = PoslinkReflection.CreateRequest("ForceBatchClose");
                            object forceRsp = PoslinkReflection.CreateResponse("ForceBatchClose");

                            int forceRc = PoslinkReflection.InvokeTxMethod(target, "ForceBatchClose", forceReq, ref forceRsp);

                            rcInner = forceRc;
                            rsp = forceRsp;
                            usedForceClose = true;
                        }

                        result = new BatchCloseResult { Rc = rcInner, Rsp = rsp, UsedForceClose = usedForceClose };
                        return rcInner;
                    },
                    out object resultObj,
                    out string errorMessage,
                    allowCancel: false);

                if (rc == TransactionUiRunner.CancelledReturnCode)
                {
                    Logger.Info("BatchClose cancelled by operator.");
                    WriteBatchCloseResponse(1, null);
                    LegacyResponseWriter.WriteLegacy(model.CardType, model.TxnType, false, "CANCELLED", "", "");
                    return rc;
                }

                if (rc == TransactionUiRunner.ConnectionErrorReturnCode || rc == TransactionUiRunner.TimeoutReturnCode)
                {
                    Logger.Error("BatchClose terminal connection error: " + errorMessage);
                    WriteBatchCloseResponse(1, null);
                    LegacyResponseWriter.WriteLegacy(model.CardType, model.TxnType, false, errorMessage ?? "Terminal connection error.", "", "");
                    return rc;
                }

                var batchResult = (BatchCloseResult)resultObj;
                LegacyResponseWriter.WriteDump(batchResult.Rsp);
                WriteBatchCloseResponse(batchResult.Rc, batchResult.Rsp, batchResult.UsedForceClose);

                if (batchResult.Rc != 0)
                {
                    Logger.Error("BatchClose declined: ResponseCode=" + GetString(batchResult.Rsp, "ResponseCode") +
                        " ResponseMessage=" + GetString(batchResult.Rsp, "ResponseMessage"));
                }

                return batchResult.Rc;
            }
            catch (Exception ex)
            {
                Logger.Error($"DoBatchCloseHandler failed: {ex}");

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

        private static void WriteBatchCloseResponse(int rc, object rsp, bool usedForceClose = false)
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

            string failedTransNum = GetString(rsp, "FailedTransactionNumber");
            string failedCount = GetString(rsp, "FailedCount");
            string safFailedCount = GetString(rsp, "SafFailedCount");
            string safFailedTotal = GetString(rsp, "SafFailedTotal");
            string lineNumber = GetString(rsp, "LineNumber");
            string linesMessage = GetString(rsp, "LinesMessage");

            string text =
                "ResultCode: " + resultCode + "\r\n" +
                "ResultTxt: " + resultTxt + "\r\n" +
                "ResponseCode: " + responseCode + "\r\n" +
                "ResponseMessage: " + responseMessage + "\r\n" +
                "ForceBatchClose: " + (usedForceClose ? "Y" : "N") + "\r\n" +
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
                "BatchNumber: " + batchNum + "\r\n" +
                "FailedTransactionNumber: " + failedTransNum + "\r\n" +
                "FailedCount: " + failedCount + "\r\n" +
                "SafFailedCount: " + safFailedCount + "\r\n" +
                "SafFailedTotal: " + safFailedTotal + "\r\n" +
                "LineNumber: " + lineNumber + "\r\n" +
                "LinesMessage: " + linesMessage + "\r\n";

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