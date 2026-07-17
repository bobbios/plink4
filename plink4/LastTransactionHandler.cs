using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace plink4
{
    internal static class LastTransactionHandler
    {
        public static int Run(ArgsModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(model.Ip)) throw new ArgumentException("IP is required.");

            try
            {
                int rc = TransactionUiRunner.RunWithDialog(model, "Retrieving last transactions...\nPlease wait.",
                    (object terminal, out object result) =>
                    {
                        var rows = GetHistoryTransactions(terminal, model, 10);
                        if (rows.Count == 0)
                            rows = GetLastTransactions(terminal, model, 10);

                        result = rows;
                        return 0;
                    },
                    out object resultObj,
                    out string errorMessage);

                if (rc == TransactionUiRunner.CancelledReturnCode)
                {
                    Logger.Info("LastTransaction cancelled by operator.");
                    WriteError("Cancelled by operator.");
                    return rc;
                }

                if (rc == TransactionUiRunner.ConnectionErrorReturnCode || rc == TransactionUiRunner.TimeoutReturnCode)
                {
                    Logger.Error("LastTransaction terminal connection error: " + errorMessage);
                    WriteError(errorMessage ?? "Terminal connection error.");
                    return rc;
                }

                WriteLastTransactions((List<LastTxnRow>)resultObj);
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"LastTransactionHandler failed: {ex}");
                WriteError(ex.Message);
                return 1;
            }
        }

        private static List<LastTxnRow> GetHistoryTransactions(object terminal, ArgsModel model, int takeCount)
        {
            var list = new List<LastTxnRow>();

            object report = PoslinkReflection.RequireProperty(terminal, "Report", "Report property is null.");

            string[] edcTypes = { "Credit", "Debit", "Ebt", "Gift", "Cash", "Loyalty", "QrPayment", "Other" };
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var edc in edcTypes)
            {
                if (sw.ElapsedMilliseconds > FallbackBudgetMs)
                {
                    Logger.Info($"LastTransaction history lookup stopped after {sw.ElapsedMilliseconds}ms (budget {FallbackBudgetMs}ms).");
                    break;
                }

                object reqTotal = PoslinkReflection.CreateRequest("LocalDetailReport");
                object rspTotal = PoslinkReflection.CreateResponse("LocalDetailReport");

                SetEnumIfExists(reqTotal, "EdcType", edc);
                TrySetEnumCandidates(reqTotal, "TransactionType", "NotSet");
                TrySetEnumCandidates(reqTotal, "CardType", "NotSet");
                TrySetEnumCandidates(reqTotal, "TransactionResultType", "NotSet");

                int rcTotal = PoslinkReflection.InvokeTxMethod(report, "LocalDetailReport", reqTotal, ref rspTotal);
                if (rcTotal != 0 || rspTotal == null || !IsSuccessResponseCode(rspTotal)) continue;

                string totalRaw = ReadString(rspTotal, "TotalRecord");
                if (!int.TryParse(totalRaw, out int total) || total <= 0) continue;

                for (int i = total - 1; i >= 0 && list.Count < takeCount; i--)
                {
                    if (sw.ElapsedMilliseconds > FallbackBudgetMs) break;

                    object req = PoslinkReflection.CreateRequest("LocalDetailReport");
                    object rsp = PoslinkReflection.CreateResponse("LocalDetailReport");

                    SetEnumIfExists(req, "EdcType", edc);
                    TrySetEnumCandidates(req, "TransactionType", "NotSet");
                    TrySetEnumCandidates(req, "CardType", "NotSet");
                    TrySetEnumCandidates(req, "TransactionResultType", "NotSet");
                    SetStringOrIntIfExists(req, "RecordNumber", i.ToString());

                    int rc = PoslinkReflection.InvokeTxMethod(report, "LocalDetailReport", req, ref rsp);
                    if (rc != 0 || rsp == null || !IsSuccessResponseCode(rsp)) continue;

                    var row = BuildRowFromResponse(rsp, edc);
                    if (IsEmptyRow(row)) continue;

                    if (row.RecordNumber == 0) row.RecordNumber = i;

                    list.Add(row);
                }
            }

            list.Sort((x, y) => y.RecordNumber.CompareTo(x.RecordNumber));
            for (int i = 0; i < list.Count; i++)
                list[i].Index = i + 1;

            if (list.Count > takeCount)
                list = list.GetRange(0, takeCount);

            return list;
        }

        // Bounds on the per-record probing fallback below: with no local history on the
        // terminal (e.g. demo mode, or a freshly batch-closed terminal), every single probe
        // comes back "not found" and nothing here ever hits takeCount to break out early —
        // so without a hard time budget this can turn into up to 200 records * 8 EDC types
        // = 1600 sequential terminal round trips, hanging the dialog for many minutes.
        private const int MaxRecordProbesPerEdc = 25;
        private const int FallbackBudgetMs = 15000;

        private static List<LastTxnRow> GetLastTransactions(object terminal, ArgsModel model, int takeCount)
        {
            var list = new List<LastTxnRow>();

            object report = PoslinkReflection.RequireProperty(terminal, "Report", "Report property is null.");

            string[] edcTypes = { "Credit", "Debit", "Ebt", "Gift", "Cash", "Loyalty", "QrPayment", "Other" };
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Deliberately not using LastTransaction=Retrieve ("give me your last
            // transaction") here — confirmed against a real terminal that it doesn't scope
            // by EdcType and echoes back one cached record regardless of the filter,
            // producing false-positive rows on a terminal with zero real transactions.
            // The indexed RecordNumber lookup below (same one GetHistoryTransactions uses)
            // is the one proven to correctly report empty when the terminal is empty.
            if (list.Count < takeCount && sw.ElapsedMilliseconds <= FallbackBudgetMs)
            {
                foreach (var edc in edcTypes)
                {
                    for (int rec = 1; rec <= MaxRecordProbesPerEdc && list.Count < takeCount; rec++)
                    {
                        var row = GetSingleDetail(report, model, edc, rec);
                        if (row != null && !IsEmptyRow(row))
                            list.Add(row);

                        if (sw.ElapsedMilliseconds > FallbackBudgetMs) break;
                    }

                    if (sw.ElapsedMilliseconds > FallbackBudgetMs)
                    {
                        Logger.Info($"LastTransaction fallback stopped after {sw.ElapsedMilliseconds}ms (budget {FallbackBudgetMs}ms) — terminal likely has no local history.");
                        break;
                    }
                }
            }

            list.Sort((x, y) => y.RecordNumber.CompareTo(x.RecordNumber));
            for (int i = 0; i < list.Count; i++)
                list[i].Index = i + 1;

            if (list.Count > takeCount)
                list = list.GetRange(0, takeCount);

            return list;
        }

        private static LastTxnRow GetSingleDetail(object report, ArgsModel model, string edc, int? recordNo)
        {
            object req = PoslinkReflection.CreateRequest("LocalDetailReport");
            object rsp = PoslinkReflection.CreateResponse("LocalDetailReport");

            PoslinkRequestBuilder.ApplyTrace(req, model.RefNum);
            SetEnumIfExists(req, "EdcType", edc);
            TrySetEnumCandidates(req, "TransactionType", "NotSet");
            TrySetEnumCandidates(req, "CardType", "NotSet");
            TrySetEnumCandidates(req, "TransactionResultType", "NotSet");
            TrySetEnumCandidates(req, "LastTransaction", "NotRetrieve");

            if (recordNo.HasValue)
                SetIntIfExists(req, "RecordNumber", recordNo.Value);

            int rc = PoslinkReflection.InvokeTxMethod(report, "LocalDetailReport", req, ref rsp);
            if (rc != 0 || rsp == null || !IsSuccessResponseCode(rsp)) return null;

            return BuildRowFromResponse(rsp, edc);
        }

        // The SDK's own execution-result error code only reflects whether the call itself
        // succeeded — a call can succeed while the response's own ResponseCode says
        // "NOT FOUND" (e.g. 100023) with otherwise stale/leftover field values still
        // populated. Callers must check this before trusting a response's fields.
        private static bool IsSuccessResponseCode(object rsp)
        {
            string respCode = ReadString(rsp, "ResponseCode");
            return string.IsNullOrWhiteSpace(respCode) || respCode == "0" || respCode == "000000";
        }

        private static LastTxnRow BuildRowFromResponse(object rsp, string fallbackEdc)
        {
            var amtInfo = PoslinkReflection.GetProperty(rsp, "AmountInformation");
            var acctInfo = PoslinkReflection.GetProperty(rsp, "AccountInformation");
            var traceInfo = PoslinkReflection.GetProperty(rsp, "TraceInformation");
            var hostInfo = PoslinkReflection.GetProperty(rsp, "HostInformation");
            var hostTrace = PoslinkReflection.GetProperty(rsp, "HostTraceInformation");
            var txnInfo = PoslinkReflection.GetProperty(rsp, "ReportTransactionInformation");
            var cardInfo = PoslinkReflection.GetProperty(rsp, "CardInformation");

            var row = new LastTxnRow
            {
                Type = ReadString(rsp, "EdcType", "TransactionType") ?? fallbackEdc,
                Amount = ReadAmountInCents(amtInfo, "ApprovedAmount", "Amount", "TransactionAmount", "TotalAmount", "BaseAmount"),
                Last4 = ReadLast4(acctInfo) ?? ReadLast4(cardInfo) ?? ReadLast4(rsp),
                RecordNumber = ReadInt(rsp, "RecordNumber"),
                TransactionNumber = ReadString(traceInfo, "TransactionNumber", "ReferenceNumber", "TraceNumber", "InvoiceNumber", "TransactionId", "EcrReferenceNumber", "HostReferenceNumber")
                    ?? ReadString(hostTrace, "HostReferenceNumber", "ReferenceNumber", "TransactionNumber", "TraceNumber")
                    ?? ReadString(txnInfo, "TransactionNumber", "ReferenceNumber", "InvoiceNumber")
                    ?? ReadString(rsp, "ReferenceNumber", "TransactionNumber"),
                AuthNumber = ReadString(hostInfo, "AuthorizationCode", "AuthCode", "ApprovalNumber", "AuthNumber")
                    ?? ReadString(traceInfo, "AuthorizationCode", "AuthCode", "ApprovalNumber", "AuthNumber")
            };

            if (row.Amount == 0m)
                row.Amount = ReadAmountInCents(rsp, "Amount", "ApprovedAmount", "TransactionAmount", "TotalAmount", "BaseAmount");

            return row;
        }

        private static bool IsEmptyRow(LastTxnRow row)
        {
            return row == null ||
                   string.IsNullOrWhiteSpace(row.TransactionNumber) &&
                   string.IsNullOrWhiteSpace(row.Last4) &&
                   string.IsNullOrWhiteSpace(row.AuthNumber) &&
                   row.Amount == 0m;
        }

        private static void WriteLastTransactions(List<LastTxnRow> rows)
        {
            EnsureOutputFolder();

            var sb = new StringBuilder();
            sb.AppendLine("ResultCode: 0");
            sb.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("--------------------------------");

            if (rows == null || rows.Count == 0)
            {
                sb.AppendLine("# | card | amount | auth");
                sb.AppendLine("No transactions found.");
                File.WriteAllText(AppConfig.Last10Transactions, sb.ToString());
                return;
            }

            int col1 = "#".Length;
            int col2 = "card".Length;
            int col3 = "amount".Length;
            int col4 = "auth".Length;

            foreach (var r in rows)
            {
                col1 = Math.Max(col1, Safe(r.TransactionNumber).Length);
                col2 = Math.Max(col2, Safe(r.Last4).Length);
                col3 = Math.Max(col3, r.Amount.ToString("0.00").Length);
                col4 = Math.Max(col4, Safe(r.AuthNumber).Length);
            }

            sb.AppendLine(
                PadRight("#", col1) + " | " +
                PadRight("card", col2) + " | " +
                PadLeft("amount", col3) + " | " +
                PadRight("auth", col4));

            foreach (var r in rows)
            {
                sb.AppendLine(
                    PadRight(Safe(r.TransactionNumber), col1) + " | " +
                    PadRight(Safe(r.Last4), col2) + " | " +
                    PadLeft(r.Amount.ToString("0.00"), col3) + " | " +
                    PadRight(Safe(r.AuthNumber), col4));
            }

            File.WriteAllText(AppConfig.Last10Transactions, sb.ToString());
        }

        private static void WriteError(string message)
        {
            EnsureOutputFolder();

            var sb = new StringBuilder();
            sb.AppendLine("ResultCode: ERROR");
            sb.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(Safe(message));
            File.WriteAllText(AppConfig.Last10Transactions, sb.ToString());
        }

        private static void EnsureOutputFolder()
        {
            var dir = Path.GetDirectoryName(AppConfig.Last10Transactions);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static string Safe(string value) => string.IsNullOrWhiteSpace(value) ? "" : value.Replace("|", " ");

        private static string ReadString(object obj, params string[] propNames)
        {
            if (obj == null || propNames == null) return "";

            foreach (var name in propNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var val = PoslinkReflection.GetProperty(obj, name);
                if (val == null) continue;
                var s = Convert.ToString(val)?.Trim();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            return "";
        }

        private static decimal ReadAmountInCents(object obj, params string[] propNames)
        {
            if (obj == null || propNames == null) return 0m;

            foreach (var name in propNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var val = PoslinkReflection.GetProperty(obj, name);
                if (val == null) continue;

                string raw = Convert.ToString(val)?.Trim();
                if (string.IsNullOrWhiteSpace(raw)) continue;
                raw = raw.Replace(",", "");

                if (decimal.TryParse(raw, out decimal parsed))
                    return parsed / 100m;
            }
            return 0m;
        }

        private static int ReadInt(object obj, params string[] propNames)
        {
            if (obj == null || propNames == null) return 0;

            foreach (var name in propNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var val = PoslinkReflection.GetProperty(obj, name);
                if (val == null) continue;

                if (int.TryParse(Convert.ToString(val), out int i))
                    return i;
            }
            return 0;
        }

        private static string ReadLast4(object obj)
        {
            var raw = ReadString(obj, "Last4", "CardNumber", "MaskedPan", "AccountNumber", "Pan", "Account", "PrimaryAccountNumber", "CardNum");
            if (string.IsNullOrWhiteSpace(raw)) return "";

            var digits = new string(raw.Where(char.IsDigit).ToArray());
            return digits.Length >= 4 ? digits.Substring(digits.Length - 4) : digits;
        }

        private static void SetStringOrIntIfExists(object obj, string propName, string value)
        {
            var pi = obj?.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite) return;

            try
            {
                var targetType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
                if (targetType == typeof(string))
                    pi.SetValue(obj, value);
                else if (targetType == typeof(int) && int.TryParse(value, out int intVal))
                    pi.SetValue(obj, intVal);
                else
                    pi.SetValue(obj, Convert.ChangeType(value, targetType));
            }
            catch { /* silent */ }
        }

        private static void SetIntIfExists(object obj, string propName, int value)
        {
            var pi = obj?.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite) return;

            try
            {
                var targetType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
                if (targetType == typeof(int))
                    pi.SetValue(obj, value);
                else if (targetType == typeof(string))
                    pi.SetValue(obj, value.ToString());
                else
                    pi.SetValue(obj, Convert.ChangeType(value, targetType));
            }
            catch { /* silent */ }
        }

        private static void SetEnumIfExists(object obj, string propName, string enumValue)
        {
            var pi = obj?.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite) return;

            var targetType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
            if (!targetType.IsEnum) return;

            try
            {
                var enumVal = Enum.Parse(targetType, enumValue, true);
                pi.SetValue(obj, enumVal);
            }
            catch { /* silent */ }
        }

        private static bool TrySetEnumCandidates(object obj, string propName, params string[] candidates)
        {
            var pi = obj?.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite) return false;

            var targetType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
            if (!targetType.IsEnum) return false;

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                try
                {
                    var val = Enum.Parse(targetType, candidate, true);
                    pi.SetValue(obj, val);
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static string PadRight(string value, int width) => (value ?? "").PadRight(width);
        private static string PadLeft(string value, int width) => (value ?? "").PadLeft(width);
    }

    internal sealed class LastTxnRow
    {
        public int Index { get; set; }
        public int RecordNumber { get; set; }
        public string Type { get; set; }
        public string TransactionNumber { get; set; }
        public string Last4 { get; set; }
        public string AuthNumber { get; set; }
        public decimal Amount { get; set; }
    }
}