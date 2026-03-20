using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace plink4
{
    internal static class LastTransactionHandler
    {
        public static int Run(ArgsModel model)
        {
            Logger.Info("Starting LastTransactionHandler");

            try
            {
                if (model == null)
                    throw new Exception("ArgsModel is null.");

                if (string.IsNullOrWhiteSpace(model.Ip))
                    throw new Exception("IP missing.");

                var term = CommandRouter.ConnectTerminal(model);

                var rows = GetHistoryTransactions(term, model, 10);

                if (rows.Count == 0)
                    rows = GetLastTransactions(term, model, 10);

                WriteLastTransactions(rows);

                Logger.Info("LastTransaction complete count=" + rows.Count);
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("LastTransaction fatal: " + ex.Message);
                Logger.Error(ex.ToString());
                WriteError(ex.Message);
                return 1;
            }
        }

        private static List<LastTxnRow> GetHistoryTransactions(object term, ArgsModel a, int takeCount)
        {
            var list = new List<LastTxnRow>();

            if (term == null) throw new Exception("term is null.");
            if (a == null) throw new Exception("ArgsModel is null.");

            var report = PoslinkReflection.RequireProperty(term, "Report", "POSLinkSemiIntegration.Report is null.");
            Logger.Info("GetHistoryTransactions ENTER");

            string[] edcCandidates =
            {
                "Credit",
                "Debit",
                "Ebt",
                "Gift",
                "Cash",
                "Loyalty",
                "QrPayment",
                "Other"
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var edcName in edcCandidates)
            {
                try
                {
                    Logger.Info("History total check EDC=" + edcName);

                    object reqTotal = PoslinkReflection.CreateRequest("LocalDetailReport");
                    object rspTotal = PoslinkReflection.CreateResponse("LocalDetailReport");

                    SetEnumIfExists(reqTotal, "EdcType", edcName);
                    TrySetEnumCandidates(reqTotal, "TransactionType", "NotSet");
                    TrySetEnumCandidates(reqTotal, "CardType", "NotSet");
                    TrySetEnumCandidates(reqTotal, "TransactionResultType", "NotSet");

                    DumpObject("LocalDetailReport.total.req", reqTotal);

                    int rcTotal = PoslinkReflection.InvokeTxMethod(report, "LocalDetailReport", reqTotal, ref rspTotal);

                    Logger.Info($"LocalDetailReport total rc={rcTotal} EDC={edcName}");

                    if (rspTotal != null)
                        DumpObject("LocalDetailReport.total.rsp", rspTotal);

                    if (rcTotal != 0 || rspTotal == null)
                        continue;

                    string totalRaw = ReadString(rspTotal, "TotalRecord");
                    if (!int.TryParse(totalRaw, out int total) || total <= 0)
                    {
                        Logger.Info($"EDC={edcName} TotalRecord invalid: '{totalRaw}'");
                        continue;
                    }

                    Logger.Info($"EDC={edcName} TotalRecord={total}");

                    for (int i = total - 1; i >= 0 && list.Count < takeCount; i--)
                    {
                        try
                        {
                            object req = PoslinkReflection.CreateRequest("LocalDetailReport");
                            object rsp = PoslinkReflection.CreateResponse("LocalDetailReport");

                            SetEnumIfExists(req, "EdcType", edcName);
                            TrySetEnumCandidates(req, "TransactionType", "NotSet");
                            TrySetEnumCandidates(req, "CardType", "NotSet");
                            TrySetEnumCandidates(req, "TransactionResultType", "NotSet");

                            SetStringOrIntIfExists(req, "RecordNumber", i.ToString());

                            int rc = PoslinkReflection.InvokeTxMethod(report, "LocalDetailReport", req, ref rsp);

                            if (rc != 0 || rsp == null)
                                continue;

                            var row = BuildRowFromResponse(rsp, edcName);

                            if (IsEmptyRow(row))
                                continue;

                            if (row.RecordNumber == 0)
                                row.RecordNumber = i;

                            string key = BuildRowKey(row);

                            if (!seen.Add(key))
                                continue;

                            list.Add(row);
                        }
                        catch
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info($"GetHistoryTransactions EDC={edcName} failed: {ex.Message}");
                }
            }

            list.Sort((x, y) => y.RecordNumber.CompareTo(x.RecordNumber));

            for (int i = 0; i < list.Count; i++)
                list[i].Index = i + 1;

            if (list.Count > takeCount)
                list = list.GetRange(0, takeCount);

            Logger.Info("GetHistoryTransactions final count=" + list.Count);
            return list;
        }

        private static void SetStringOrIntIfExists(object obj, string propName, string value)
        {
            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite) return;

            try
            {
                var t = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;

                if (t == typeof(string))
                    pi.SetValue(obj, value);
                else if (t == typeof(int))
                    pi.SetValue(obj, int.Parse(value));
                else
                    pi.SetValue(obj, Convert.ChangeType(value, t));

                Logger.Info($"SetStringOrIntIfExists: {obj.GetType().Name}.{propName} = {value}");
            }
            catch (Exception ex)
            {
                Logger.Info($"SetStringOrIntIfExists failed: {propName}={value} => {ex.Message}");
            }
        }

        private static LastTxnRow BuildRowFromItem(object item, string fallbackType)
        {
            var amtInfo = PoslinkReflection.GetProperty(item, "AmountInformation");
            var acctInfo = PoslinkReflection.GetProperty(item, "AccountInformation");
            var traceInfo = PoslinkReflection.GetProperty(item, "TraceInformation");
            var hostInfo = PoslinkReflection.GetProperty(item, "HostInformation");
            var hostTrace = PoslinkReflection.GetProperty(item, "HostTraceInformation");
            var txnInfo = PoslinkReflection.GetProperty(item, "ReportTransactionInformation");
            var cardInfo = PoslinkReflection.GetProperty(item, "CardInformation");

            var row = new LastTxnRow
            {
                Type = ReadString(item, "EdcType", "TransactionType", "Type", "CardType"),
                RecordNumber = ReadInt(item, "RecordNumber"),
                TransactionNumber = ReadString(item, "TransactionNumber", "ReferenceNumber", "TraceNumber", "InvoiceNumber", "TransactionId"),
                AuthNumber = ReadString(item, "AuthorizationCode", "AuthCode", "ApprovalNumber", "AuthNumber"),
                Amount = ReadDecimal(item, "ApprovedAmount", "Amount", "TransactionAmount", "TotalAmount", "BaseAmount"),
                Last4 = ReadLast4(item)
            };

            if (string.IsNullOrWhiteSpace(row.Type))
                row.Type = fallbackType;

            if (row.Amount == 0m && amtInfo != null)
                row.Amount = ReadDecimal(amtInfo, "ApprovedAmount", "Amount", "TransactionAmount", "TotalAmount", "BaseAmount");

            if (string.IsNullOrWhiteSpace(row.Last4) && acctInfo != null)
                row.Last4 = ReadLast4(acctInfo);

            if (string.IsNullOrWhiteSpace(row.Last4) && cardInfo != null)
                row.Last4 = ReadLast4(cardInfo);

            if (string.IsNullOrWhiteSpace(row.TransactionNumber) && traceInfo != null)
                row.TransactionNumber = ReadString(traceInfo, "TransactionNumber", "ReferenceNumber", "TraceNumber", "InvoiceNumber", "TransactionId", "EcrReferenceNumber", "HostReferenceNumber");

            if (string.IsNullOrWhiteSpace(row.TransactionNumber) && hostTrace != null)
                row.TransactionNumber = ReadString(hostTrace, "HostReferenceNumber", "ReferenceNumber", "TransactionNumber", "TraceNumber");

            if (string.IsNullOrWhiteSpace(row.TransactionNumber) && txnInfo != null)
                row.TransactionNumber = ReadString(txnInfo, "TransactionNumber", "ReferenceNumber", "InvoiceNumber", "TraceNumber");

            if (string.IsNullOrWhiteSpace(row.AuthNumber) && hostInfo != null)
                row.AuthNumber = ReadString(hostInfo, "AuthorizationCode", "AuthCode", "ApprovalNumber", "AuthNumber");

            if (string.IsNullOrWhiteSpace(row.AuthNumber) && traceInfo != null)
                row.AuthNumber = ReadString(traceInfo, "AuthorizationCode", "AuthCode", "ApprovalNumber", "AuthNumber");

            return row;
        }

        private static bool IsEmptyRow(LastTxnRow row)
        {
            if (row == null) return true;

            return
                string.IsNullOrWhiteSpace(row.TransactionNumber) &&
                string.IsNullOrWhiteSpace(row.Last4) &&
                string.IsNullOrWhiteSpace(row.AuthNumber) &&
                row.Amount == 0m;
        }

        private static List<LastTxnRow> GetLastTransactions(object term, ArgsModel a, int takeCount)
        {
            var list = new List<LastTxnRow>();

            Logger.Info("LastTransactionHandler.GetLastTransactions ENTER");

            if (term == null) throw new Exception("term is null.");
            if (a == null) throw new Exception("ArgsModel is null.");

            var report = PoslinkReflection.RequireProperty(term, "Report", "POSLinkSemiIntegration.Report is null.");
            Logger.Info("report type=" + report.GetType().FullName);

            string[] edcCandidates =
            {
                "Credit",
                "Debit",
                "Ebt",
                "Gift",
                "Cash",
                "Loyalty",
                "QrPayment",
                "Other"
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var edcName in edcCandidates)
            {
                try
                {
                    Logger.Info("Trying latest EDC = " + edcName);

                    var latest = GetSingleDetail(report, a, edcName, null, true);
                    if (latest == null || IsEmptyRow(latest))
                        continue;

                    string key = BuildRowKey(latest);
                    if (seen.Add(key))
                        list.Add(latest);
                }
                catch (Exception ex)
                {
                    Logger.Info("Latest EDC " + edcName + " failed: " + ex.Message);
                }
            }

            if (list.Count < takeCount)
            {
                foreach (var edcName in edcCandidates)
                {
                    for (int recordNo = 1; recordNo <= 200 && list.Count < takeCount; recordNo++)
                    {
                        try
                        {
                            var row = GetSingleDetail(report, a, edcName, recordNo, false);
                            if (row == null || IsEmptyRow(row))
                                continue;

                            string key = BuildRowKey(row);
                            if (seen.Add(key))
                                list.Add(row);
                        }
                        catch (Exception ex)
                        {
                            Logger.Info($"EDC={edcName} record={recordNo} failed: {ex.Message}");
                        }
                    }
                }
            }

            list.Sort((x, y) => y.RecordNumber.CompareTo(x.RecordNumber));

            for (int i = 0; i < list.Count; i++)
                list[i].Index = i + 1;

            if (list.Count > takeCount)
                list = list.GetRange(0, takeCount);

            Logger.Info("LastTransactionHandler fallback count=" + list.Count);
            return list;
        }

        private static string BuildRowKey(LastTxnRow row)
        {
            if (row == null) return "";

            return string.Join("|",
                Safe(row.TransactionNumber),
                Safe(row.Last4),
                row.Amount.ToString("0.00"),
                Safe(row.AuthNumber),
                row.RecordNumber.ToString());
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

            int col1 = Math.Max(1, "#".Length);
            int col2 = Math.Max(4, "card".Length);
            int col3 = Math.Max(6, "amount".Length);
            int col4 = Math.Max(4, "auth".Length);

            foreach (var row in rows)
            {
                string c1 = Safe(row.TransactionNumber);
                string c2 = Safe(row.Last4);
                string c3 = row.Amount.ToString("0.00");
                string c4 = Safe(row.AuthNumber);

                if (c1.Length > col1) col1 = c1.Length;
                if (c2.Length > col2) col2 = c2.Length;
                if (c3.Length > col3) col3 = c3.Length;
                if (c4.Length > col4) col4 = c4.Length;
            }

            sb.AppendLine(
                PadRight("#", col1) + " | " +
                PadRight("card", col2) + " | " +
                PadLeft("amount", col3) + " | " +
                PadRight("auth", col4));

            foreach (var row in rows)
            {
                sb.AppendLine(
                    PadRight(Safe(row.TransactionNumber), col1) + " | " +
                    PadRight(Safe(row.Last4), col2) + " | " +
                    PadLeft(row.Amount.ToString("0.00"), col3) + " | " +
                    PadRight(Safe(row.AuthNumber), col4));
            }

            File.WriteAllText(AppConfig.Last10Transactions, sb.ToString());
        }

        private static string PadRight(string value, int width)
        {
            value = value ?? "";
            return value.PadRight(width);
        }

        private static string PadLeft(string value, int width)
        {
            value = value ?? "";
            return value.PadLeft(width);
        }

        private static LastTxnRow GetSingleDetail(object report, ArgsModel a, string edcName, int? recordNumber, bool useLastTransactionFlag)
        {
            object req = PoslinkReflection.CreateRequest("LocalDetailReport");
            object rsp = PoslinkReflection.CreateResponse("LocalDetailReport");

            Logger.Info("LocalDetailReport req type=" + req.GetType().FullName);

            PoslinkRequestBuilder.ApplyTrace(req, a.RefNum);

            SetEnumIfExists(req, "EdcType", edcName);
            TrySetEnumCandidates(req, "TransactionType", "NotSet");
            TrySetEnumCandidates(req, "CardType", "NotSet");
            TrySetEnumCandidates(req, "TransactionResultType", "NotSet");

            if (useLastTransactionFlag)
                TrySetEnumCandidates(req, "LastTransaction", "Retrieve");
            else
                TrySetEnumCandidates(req, "LastTransaction", "NotRetrieve");

            if (recordNumber.HasValue)
                SetIntIfExists(req, "RecordNumber", recordNumber.Value);

            int rc = PoslinkReflection.InvokeTxMethod(report, "LocalDetailReport", req, ref rsp);

            Logger.Info("LocalDetailReport rc=" + rc);
            Logger.Info("LocalDetailReport rsp type=" + (rsp == null ? "(null)" : rsp.GetType().FullName));

            if (rc != 0 || rsp == null)
                return null;

            string responseCode = ReadString(rsp, "ResponseCode");
            string responseMessage = ReadString(rsp, "ResponseMessage");

            bool ok =
                string.IsNullOrWhiteSpace(responseCode) ||
                responseCode == "0" ||
                responseCode == "000000";

            if (!ok)
            {
                Logger.Info("LocalDetailReport non-zero response: " + responseCode + " / " + responseMessage);
                return null;
            }

            return BuildRowFromResponse(rsp, edcName);
        }

        private static LastTxnRow BuildRowFromResponse(object rsp, string fallbackType)
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
                Type = ReadString(rsp, "EdcType", "TransactionType"),
                Amount = ReadAmountInCents(amtInfo, "ApprovedAmount", "Amount", "TransactionAmount", "TotalAmount", "BaseAmount"),
                Last4 = ReadLast4(acctInfo),
                RecordNumber = ReadInt(rsp, "RecordNumber"),
                TransactionNumber = ReadString(traceInfo, "TransactionNumber", "ReferenceNumber", "TraceNumber", "InvoiceNumber", "TransactionId", "EcrReferenceNumber", "HostReferenceNumber"),
                AuthNumber = ReadString(hostInfo, "AuthorizationCode", "AuthCode", "ApprovalNumber", "AuthNumber")
            };

            if (string.IsNullOrWhiteSpace(row.Type))
                row.Type = fallbackType;

            if (string.IsNullOrWhiteSpace(row.TransactionNumber) && hostTrace != null)
                row.TransactionNumber = ReadString(hostTrace, "HostReferenceNumber", "ReferenceNumber", "TransactionNumber", "TraceNumber");

            if (string.IsNullOrWhiteSpace(row.TransactionNumber) && txnInfo != null)
                row.TransactionNumber = ReadString(txnInfo, "TransactionNumber", "ReferenceNumber", "InvoiceNumber");

            if (string.IsNullOrWhiteSpace(row.TransactionNumber))
                row.TransactionNumber = ReadString(rsp, "ReferenceNumber", "TransactionNumber");

            if (string.IsNullOrWhiteSpace(row.AuthNumber) && traceInfo != null)
                row.AuthNumber = ReadString(traceInfo, "AuthorizationCode", "AuthCode", "ApprovalNumber", "AuthNumber");

            if (row.Amount == 0m)
                row.Amount = ReadAmountInCents(rsp, "Amount", "ApprovedAmount", "TransactionAmount", "TotalAmount", "BaseAmount");

            if (string.IsNullOrWhiteSpace(row.Last4))
                row.Last4 = ReadLast4(cardInfo);

            if (string.IsNullOrWhiteSpace(row.Last4))
                row.Last4 = ReadLast4(rsp);

            return row;
        }

        private static void WriteError(string msg)
        {
            EnsureOutputFolder();

            var sb = new StringBuilder();
            sb.AppendLine("ResultCode: ERROR");
            sb.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(Safe(msg));

            File.WriteAllText(AppConfig.Last10Transactions, sb.ToString());
        }

        private static void EnsureOutputFolder()
        {
            var dir = Path.GetDirectoryName(AppConfig.Last10Transactions);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "" : value.Replace("|", " ");
        }

        private static string ReadString(object obj, params string[] names)
        {
            if (obj == null || names == null) return "";

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var val = PoslinkReflection.GetProperty(obj, name);
                if (val == null) continue;

                var s = Convert.ToString(val);
                if (!string.IsNullOrWhiteSpace(s))
                    return s.Trim();
            }

            return "";
        }

        private static decimal ReadDecimal(object obj, params string[] names)
        {
            if (obj == null || names == null) return 0m;

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var val = PoslinkReflection.GetProperty(obj, name);
                if (val == null) continue;

                string raw = Convert.ToString(val)?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                raw = raw.Replace(",", "");

                if (!decimal.TryParse(raw, out decimal parsed))
                    continue;

                decimal normalized = NormalizePoslinkAmount(parsed, raw);

                Logger.Info($"ReadDecimal: {obj.GetType().Name}.{name} raw='{raw}' parsed={parsed} normalized={normalized:0.00}");

                return normalized;
            }

            return 0m;
        }
        private static decimal ReadAmountInCents(object obj, params string[] names)
        {
            if (obj == null || names == null) return 0m;

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var val = PoslinkReflection.GetProperty(obj, name);
                if (val == null) continue;

                string raw = Convert.ToString(val)?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                raw = raw.Replace(",", "");

                if (!decimal.TryParse(raw, out decimal parsed))
                    continue;

                Logger.Info($"ReadAmountInCents: {obj.GetType().Name}.{name} raw='{raw}' parsed={parsed}");

                return parsed / 100m;
            }

            return 0m;
        }
        private static decimal NormalizePoslinkAmount(decimal parsed, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return parsed;

            raw = raw.Trim();

            // already a true decimal amount like 2.54 or 19.99
            if (parsed != decimal.Truncate(parsed))
                return parsed;

            // integer-like values are usually cents in POSLink
            // 254 -> 2.54
            // 10000 -> 100.00
            // 254.00 -> 2.54
            if (Math.Abs(parsed) >= 100m)
                return parsed / 100m;

            // keep tiny whole-number amounts as-is
            // 0, 1, 2, etc.
            return parsed;
        }
        private static int ReadInt(object obj, params string[] names)
        {
            if (obj == null || names == null) return 0;

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var val = PoslinkReflection.GetProperty(obj, name);
                if (val == null) continue;

                try { return Convert.ToInt32(val); } catch { }

                if (int.TryParse(Convert.ToString(val), out var i))
                    return i;
            }

            return 0;
        }

        private static string ReadLast4(object obj)
        {
            var raw = ReadString(obj, "Last4", "CardNumber", "MaskedPan", "AccountNumber", "Pan", "Account", "PrimaryAccountNumber", "CardNum");

            if (string.IsNullOrWhiteSpace(raw))
                return "";

            raw = raw.Trim();

            var digits = new StringBuilder();
            foreach (char c in raw)
            {
                if (char.IsDigit(c))
                    digits.Append(c);
            }

            string d = digits.ToString();
            if (d.Length >= 4)
                return d.Substring(d.Length - 4);

            return raw.Length <= 4 ? raw : raw.Substring(raw.Length - 4);
        }

        private static void SetIntIfExists(object obj, string propName, int value)
        {
            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite) return;

            try
            {
                if (pi.PropertyType == typeof(int) || pi.PropertyType == typeof(int?))
                    pi.SetValue(obj, value);
                else if (pi.PropertyType == typeof(string))
                    pi.SetValue(obj, value.ToString());
                else
                    pi.SetValue(obj, Convert.ChangeType(value, Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType));

                Logger.Info($"SetIntIfExists: {obj.GetType().Name}.{propName} = {value}");
            }
            catch (Exception ex)
            {
                Logger.Info($"SetIntIfExists failed: {propName} => {ex.Message}");
            }
        }

        private static void SetEnumIfExists(object obj, string propName, string enumName)
        {
            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite) return;

            var t = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
            if (!t.IsEnum) return;

            try
            {
                var val = Enum.Parse(t, enumName, true);
                pi.SetValue(obj, val);
                Logger.Info($"SetEnumIfExists: {obj.GetType().Name}.{propName} = {enumName}");
            }
            catch (Exception ex)
            {
                Logger.Info($"SetEnumIfExists failed: {propName}={enumName} => {ex.Message}");
                DumpEnumValues(t, $"{obj.GetType().Name}.{propName}");
            }
        }

        private static bool TrySetEnumCandidates(object obj, string propName, params string[] candidates)
        {
            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite) return false;

            var t = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
            if (!t.IsEnum) return false;

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;

                try
                {
                    var val = Enum.Parse(t, candidate, true);
                    pi.SetValue(obj, val);
                    Logger.Info($"TrySetEnumCandidates: {obj.GetType().Name}.{propName} = {candidate}");
                    return true;
                }
                catch
                {
                }
            }

            Logger.Info($"TrySetEnumCandidates failed: {obj.GetType().Name}.{propName}");
            DumpEnumValues(t, $"{obj.GetType().Name}.{propName}");
            return false;
        }

        private static void DumpEnumValues(Type enumType, string label)
        {
            try
            {
                if (enumType == null || !enumType.IsEnum) return;

                var names = Enum.GetNames(enumType);
                Logger.Info(label + " enum values:");
                foreach (var n in names)
                    Logger.Info("  " + n);
            }
            catch (Exception ex)
            {
                Logger.Info("DumpEnumValues failed for " + label + ": " + ex.Message);
            }
        }

        private static string DescribeValue(object val)
        {
            if (val == null) return "(null)";
            if (val is string s) return s;
            if (val is IEnumerable && !(val is string)) return val.GetType().FullName + " [enumerable]";
            return Convert.ToString(val);
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
                    Logger.Info("  " + label + "." + pi.Name + " = " + DescribeValue(val));
                }
                catch (Exception ex)
                {
                    Logger.Info("  " + label + "." + pi.Name + " = (error: " + ex.Message + ")");
                }
            }
        }
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