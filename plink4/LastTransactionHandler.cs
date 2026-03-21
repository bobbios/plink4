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
            Logger.Debug("LastTransactionHandler.Run start");

            if (model == null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(model.Ip)) throw new ArgumentException("IP is required.");

            object terminal = null;

            try
            {
                Logger.Debug("Connecting terminal " + model.Ip);

                terminal = CommandRouter.ConnectTerminal(model)
                    ?? throw new InvalidOperationException("Terminal connection failed.");

                Logger.Debug("Terminal connected");

                var rows = GetHistoryTransactions(terminal, model, 10);

                Logger.Debug("HistoryTransactions count=" + rows.Count);

                WriteLastTransactions(rows);

                Logger.Debug("WriteLastTransactions completed");

                System.Threading.Thread.Sleep(150);

                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("LastTransactionHandler error: " + ex.ToString());
                WriteError(ex.Message);
                return 1;
            }
            finally
            {
                Logger.Debug("Closing terminal");

                try
                {
                    if (terminal != null)
                    {
                        var t = terminal.GetType();

                        var mi =
                            t.GetMethod("Close", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase, null, Type.EmptyTypes, null) ??
                            t.GetMethod("Disconnect", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase, null, Type.EmptyTypes, null) ??
                            t.GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase, null, Type.EmptyTypes, null);

                        if (mi != null)
                        {
                            Logger.Debug("Invoking terminal close method: " + mi.Name);
                            mi.Invoke(terminal, null);
                        }
                        else if (terminal is IDisposable d)
                        {
                            Logger.Debug("Invoking IDisposable.Dispose");
                            d.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug("Terminal close error: " + ex.Message);
                }
            }
        }

        private static List<LastTxnRow> GetHistoryTransactions(object terminal, ArgsModel model, int takeCount)
        {
            var list = new List<LastTxnRow>();

            object report = PoslinkReflection.RequireProperty(terminal, "Report", "Report property is null.");

            string[] edcTypes = { "Credit", "Debit", "Ebt" };

            foreach (var edc in edcTypes)
            {
                Logger.Debug("GetHistoryTransactions total request edc=" + edc);

                object reqTotal = PoslinkReflection.CreateRequest("LocalDetailReport");
                object rspTotal = PoslinkReflection.CreateResponse("LocalDetailReport");

                SetEnumIfExists(reqTotal, "EdcType", edc);
                TrySetEnumCandidates(reqTotal, "TransactionType", "NotSet");
                TrySetEnumCandidates(reqTotal, "CardType", "NotSet");
                TrySetEnumCandidates(reqTotal, "TransactionResultType", "NotSet");

                int rcTotal = PoslinkReflection.InvokeTxMethod(report, "LocalDetailReport", reqTotal, ref rspTotal);
                if (rcTotal != 0 || rspTotal == null)
                {
                    Logger.Debug("GetHistoryTransactions total request failed edc=" + edc + " rc=" + rcTotal);
                    continue;
                }

                string totalRaw = ReadString(rspTotal, "TotalRecord");
                int total;
                if (!int.TryParse(totalRaw, out total) || total <= 0)
                {
                    Logger.Debug("GetHistoryTransactions no history for edc=" + edc + " totalRaw=" + totalRaw);
                    continue;
                }

                Logger.Debug("GetHistoryTransactions edc=" + edc + " total=" + total);

                for (int i = total - 1; i >= 0; i--)
                {
                    object req = PoslinkReflection.CreateRequest("LocalDetailReport");
                    object rsp = PoslinkReflection.CreateResponse("LocalDetailReport");

                    SetEnumIfExists(req, "EdcType", edc);
                    TrySetEnumCandidates(req, "TransactionType", "NotSet");
                    TrySetEnumCandidates(req, "CardType", "NotSet");
                    TrySetEnumCandidates(req, "TransactionResultType", "NotSet");
                    SetStringOrIntIfExists(req, "RecordNumber", i.ToString());

                    int rc = PoslinkReflection.InvokeTxMethod(report, "LocalDetailReport", req, ref rsp);
                    if (rc != 0 || rsp == null)
                        continue;

                    var row = BuildRowFromResponse(rsp, edc);
                    if (IsEmptyRow(row))
                        continue;

                    if (row.RecordNumber == 0)
                        row.RecordNumber = i;

                    // use actual history number for display
                    row.Index = row.RecordNumber;

                    list.Add(row);
                }
            }

            Logger.Debug("GetHistoryTransactions full count before sort/trim=" + list.Count);

            list.Sort((x, y) => y.RecordNumber.CompareTo(x.RecordNumber));

            if (list.Count > takeCount)
                list = list.GetRange(0, takeCount);

            Logger.Debug("GetHistoryTransactions returned count after trim=" + list.Count);

            return list;
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
            var outputPath = AppConfig.Last10Transactions;

            if (string.IsNullOrWhiteSpace(outputPath))
                throw new Exception("AppConfig.Last10Transactions is blank.");

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("ResultCode: 0");
            sb.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("--------------------------------");

            if (rows == null || rows.Count == 0)
            {
                sb.AppendLine("# | card | amount | auth");
                sb.AppendLine("No transactions found.");
                File.WriteAllText(outputPath, sb.ToString());
                return;
            }

            int col1 = "#".Length;
            int col2 = "card".Length;
            int col3 = "amount".Length;
            int col4 = "auth".Length;

            foreach (var r in rows)
            {
                col1 = Math.Max(col1, r.Index.ToString().Length);
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
                PadRight(r.Index.ToString(), col1) + " | " +
                    PadRight(Safe(r.Last4), col2) + " | " +
                    PadLeft(r.Amount.ToString("0.00"), col3) + " | " +
                    PadRight(Safe(r.AuthNumber), col4));
            }

            File.WriteAllText(outputPath, sb.ToString());
        }

        private static void WriteError(string message)
        {
            var outputPath = AppConfig.Last10Transactions;

            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = @"C:\plink\responses\last10transactions.txt";

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("ResultCode: ERROR");
            sb.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(Safe(message));

            File.WriteAllText(outputPath, sb.ToString());
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