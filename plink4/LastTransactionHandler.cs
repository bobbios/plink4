using POSLinkSemiIntegration.Report;
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

                var rows = GetHistoryTransactions(terminal, model, 5);

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

            object report = PoslinkReflection.RequireProperty(
                terminal,
                "Report",
                "Report property is null."
            );

            string[] edcTypes = { "Credit", "Debit", "Ebt" };

            foreach (var edc in edcTypes)
            {
                object reqTotal = PoslinkReflection.CreateRequest("LocalDetailReport");
                object rspTotal = PoslinkReflection.CreateResponse("LocalDetailReport");

                SetEnumIfExists(reqTotal, "EdcType", edc);
                TrySetEnumCandidates(reqTotal, "TransactionType", "NotSet");
                TrySetEnumCandidates(reqTotal, "CardType", "NotSet");
                TrySetEnumCandidates(reqTotal, "TransactionResultType", "NotSet");

                int rcTotal = PoslinkReflection.InvokeTxMethod(report, "LocalDetailReport", reqTotal, ref rspTotal);
                if (rcTotal != 0 || rspTotal == null)
                    continue;

                string totalRaw = ReadString(rspTotal, "TotalRecord", "TotalRecords", "RecordCount");
                int total;
                if (!int.TryParse(totalRaw, out total) || total <= 0)
                    continue;

                int startRecord = Math.Max(0, total - takeCount);

                for (int i = total - 1; i >= startRecord; i--)
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

                    DumpLocalDetailResponse(rsp, edc, i);


                    var row = BuildRowFromResponse(rsp, edc);
                    if (IsEmptyRow(row))
                        continue;

                    if (row.RecordNumber == 0)
                        row.RecordNumber = i;

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

            string txnType = NormalizeTxnType(
                ReadString(
                    txnInfo,
                    "TransactionType",
                    "TransType",
                    "TxnType",
                    "Type"
                )
                ?? ReadString(
                    traceInfo,
                    "TransactionType",
                    "TransType",
                    "TxnType",
                    "Type"
                )
                ?? ReadString(
                    rsp,
                    "TransactionType",
                    "TransType",
                    "TxnType",
                    "Type"
                )
            );

            var row = new LastTxnRow
            {
                EdcType = fallbackEdc,
                Type = NormalizeTxnType(
    ReadString(
        rsp,
        "TransactionType"
    )
),
                Amount = ReadAmountInCents(amtInfo, "ApprovedAmount", "Amount", "TransactionAmount", "TotalAmount", "BaseAmount"),
                Last4 = ReadLast4(acctInfo) ?? ReadLast4(cardInfo) ?? ReadLast4(rsp),
                RecordNumber = ReadInt(rsp, "RecordNumber"),
                TransactionNumber =
                    ReadString(traceInfo, "TransactionNumber", "ReferenceNumber", "TraceNumber", "InvoiceNumber", "TransactionId", "EcrReferenceNumber", "HostReferenceNumber")
                    ?? ReadString(hostTrace, "HostReferenceNumber", "ReferenceNumber", "TransactionNumber", "TraceNumber")
                    ?? ReadString(txnInfo, "TransactionNumber", "ReferenceNumber", "InvoiceNumber")
                    ?? ReadString(rsp, "ReferenceNumber", "TransactionNumber"),
                AuthNumber =
                    ReadString(hostInfo, "AuthorizationCode", "AuthCode", "ApprovalNumber", "AuthNumber")
                    ?? ReadString(traceInfo, "AuthorizationCode", "AuthCode", "ApprovalNumber", "AuthNumber")
            };

            if (string.IsNullOrWhiteSpace(row.Type))
                row.Type = fallbackEdc;

            if (row.Amount == 0m)
                row.Amount = ReadAmountInCents(rsp, "Amount", "ApprovedAmount", "TransactionAmount", "TotalAmount", "BaseAmount");

            return row;
        }

        private static string NormalizeTxnType(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            string s = raw.Trim();

            if (s.Equals("Sale", StringComparison.OrdinalIgnoreCase)) return "SALE";
            if (s.Equals("Return", StringComparison.OrdinalIgnoreCase)) return "RETURN";
            if (s.Equals("Refund", StringComparison.OrdinalIgnoreCase)) return "RETURN";
            if (s.Equals("Void", StringComparison.OrdinalIgnoreCase)) return "VOID";
            if (s.Equals("Adjust", StringComparison.OrdinalIgnoreCase)) return "ADJUST";
            if (s.Equals("Adjustment", StringComparison.OrdinalIgnoreCase)) return "ADJUST";
            if (s.Equals("PreAuth", StringComparison.OrdinalIgnoreCase)) return "PREAUTH";
            if (s.Equals("Pre-Auth", StringComparison.OrdinalIgnoreCase)) return "PREAUTH";
            if (s.Equals("PostAuth", StringComparison.OrdinalIgnoreCase)) return "POSTAUTH";
            if (s.Equals("Post-Auth", StringComparison.OrdinalIgnoreCase)) return "POSTAUTH";
            if (s.Equals("OfflineSale", StringComparison.OrdinalIgnoreCase)) return "OFFLINE SALE";
            if (s.Equals("Force", StringComparison.OrdinalIgnoreCase)) return "FORCE";

            return s.ToUpperInvariant();
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
                throw new Exception("AppConfig.last10Transactions is blank.");

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("ResultCode: 0");
            sb.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("--------------------------------");

            if (rows == null || rows.Count == 0)
            {
                sb.AppendLine("# | type | card | amount | auth");
                sb.AppendLine("No transactions found.");
                File.WriteAllText(outputPath, sb.ToString());
                return;
            }

            int col1 = "#".Length;
            int col2 = "type".Length;
            int col3 = "card".Length;
            int col4 = "amount".Length;
            int col5 = "auth".Length;

            foreach (var r in rows)
            {
                col1 = Math.Max(col1, r.Index.ToString().Length);
                col2 = Math.Max(col2, Safe(r.Type).Length);
                col3 = Math.Max(col3, Safe(r.Last4).Length);
                col4 = Math.Max(col4, r.Amount.ToString("0.00").Length);
                col5 = Math.Max(col5, Safe(r.AuthNumber).Length);
            }

            sb.AppendLine(
                PadRight("#", col1) + " | " +
                PadRight("type", col2) + " | " +
                PadRight("card", col3) + " | " +
                PadLeft("amount", col4) + " | " +
                PadRight("auth", col5));

            foreach (var r in rows)
            {
                sb.AppendLine(
                    PadRight(r.Index.ToString(), col1) + " | " +
                    PadRight(Safe(r.Type), col2) + " | " +
                    PadRight(Safe(r.Last4), col3) + " | " +
                    PadLeft(r.Amount.ToString("0.00"), col4) + " | " +
                    PadRight(Safe(r.AuthNumber), col5));
            }

            File.WriteAllText(outputPath, sb.ToString());
        }

        private static void WriteError(string message)
        {
            var outputPath = AppConfig.Last10Transactions;

            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = @"C:\plink\responses\Last10Transactions.txt";

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
            catch { }
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
            catch { }
        }

        private static void DumpLocalDetailResponse(object rsp, string edc, int recordNumber)
        {
            try
            {
                Logger.Debug("==================================================");
                Logger.Debug("LocalDetailReport Dump");
                Logger.Debug("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Logger.Debug("EDC: " + edc);
                Logger.Debug("Requested RecordNumber: " + recordNumber);
                Logger.Debug("Response Type: " + (rsp == null ? "(null)" : rsp.GetType().FullName));
                Logger.Debug("==================================================");

                DumpObjectProperties(rsp, "rsp", 0);
            }
            catch (Exception ex)
            {
                Logger.Debug("DumpLocalDetailResponse error: " + ex.Message);
            }
        }

        private static void DumpObjectProperties(object obj, string name, int level)
        {
            string indent = new string(' ', level * 2);

            if (obj == null)
            {
                Logger.Debug(indent + name + " = <null>");
                return;
            }

            Type t = obj.GetType();

            if (level > 4)
            {
                Logger.Debug(indent + name + " = <max depth> [" + t.FullName + "]");
                return;
            }

            Logger.Debug(indent + name + " [" + t.FullName + "]");

            PropertyInfo[] props;

            try
            {
                props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            }
            catch
            {
                return;
            }

            foreach (var p in props)
            {
                object val = null;

                try
                {
                    if (p.GetIndexParameters().Length == 0)
                        val = p.GetValue(obj, null);
                }
                catch (Exception ex)
                {
                    Logger.Debug(indent + "  " + p.Name + " = <error: " + ex.Message + ">");
                    continue;
                }

                if (val == null)
                {
                    Logger.Debug(indent + "  " + p.Name + " = <null>");
                    continue;
                }

                Type vt = val.GetType();

                if (vt.IsPrimitive || vt.IsEnum || vt == typeof(string) || vt == typeof(decimal) || vt == typeof(DateTime))
                {
                    Logger.Debug(indent + "  " + p.Name + " = " + Convert.ToString(val));
                }
                else
                {
                    Logger.Debug(indent + "  " + p.Name + " =>");
                    DumpObjectProperties(val, p.Name, level + 1);
                }
            }
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
        public string EdcType { get; set; }
        public string Type { get; set; }
        public string TransactionNumber { get; set; }
        public string Last4 { get; set; }
        public string AuthNumber { get; set; }
        public decimal Amount { get; set; }
    }
}