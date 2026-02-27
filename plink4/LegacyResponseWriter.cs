using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace plink4
{
    internal static class LegacyResponseWriter
    {
        // Write legacy response.txt in a stable, line-index-friendly way (26 lines)
        public static void WriteFromRsp(string cardType, string txnType, bool ok, object rsp)
        {
            // Best-effort mappings (we’ll tighten after you paste response2.txt once)
            string respCode = SafeStr(rsp, "ResponseCode");
            string respMsg = SafeStr(rsp, "ResponseMessage");
            string authCode = SafeStr(rsp, "AuthCode");

            WriteLegacy(cardType, txnType, ok, respMsg, respCode, authCode, rsp);
        }

        public static void WriteLegacy(string cardType, string txnType, bool ok,
            string responseMessage, string responseCode, string authCode, object rspObj = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppConfig.OutResponse));

            // 26 lines – keep order stable so your resultline[] indexes don’t shift randomly.
            // Your Paradox code uses resultline[2] as “ok/otherwise”, and then reads various fixed positions.
            // We’ll keep a consistent layout and you can remap once we see real fields in response2.txt.
            var lines = new string[26];

            lines[0] = "Result: " + (ok ? "OK" : (responseMessage ?? "ERROR"));
            lines[1] = "CardType: " + (cardType ?? "");
            lines[2] = "TxnType: " + (txnType ?? "");
            lines[3] = "Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Keep these in classic key:value format
            lines[4] = "TransType: " + SafeStr(rspObj, "TransactionType");          // often exists
            lines[5] = "Account: " + FirstNonEmpty(
                SafeStr(rspObj, "AccountNumber"),
                SafeStr(rspObj, "MaskedAccount"),
                SafeStr(rspObj, "CardNumber"),
                SafeStr(rspObj, "PAN"));

            lines[6] = "ExpDate: " + FirstNonEmpty(
                SafeStr(rspObj, "ExpireDate"),
                SafeStr(rspObj, "ExpDate"));

            lines[7] = "RecNo: " + FirstNonEmpty(
                SafeStr(rspObj, "RecordNo"),
                SafeStr(rspObj, "RecNo"),
                SafeStr(rspObj, "ReferenceNumber"));

            lines[8] = "Amount: " + FirstNonEmpty(
                SafeStr(rspObj, "ApprovedAmount"),
                SafeStr(rspObj, "TransactionAmount"));

            lines[9] = "HostRef: " + FirstNonEmpty(
                SafeStr(rspObj, "HostRefNum"),
                SafeStr(rspObj, "ReferenceNum"),
                SafeStr(rspObj, "RefNum"));

            lines[10] = "AuthCode: " + (authCode ?? "");
            lines[11] = "ResponseCode: " + (responseCode ?? "");
            lines[12] = "ResponseMessage: " + (responseMessage ?? "");

            // EBT balance (your Paradox reads RemainingBalance sometimes)
            lines[13] = "RemainingBalance: " + FirstNonEmpty(
                SafeStr(rspObj, "RemainingBalance"),
                SafeStr(rspObj, "Balance"),
                SafeStr(rspObj, "AvailableBalance"));

            // filler lines to reach 26 (stable)
            for (int i = 14; i < 26; i++)
                lines[i] = "Field" + (i + 1) + ": " + "";

            File.WriteAllText(AppConfig.OutResponse, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }

        public static void WriteDump(object rsp)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppConfig.OutResponse2));
            File.WriteAllText(AppConfig.OutResponse2, DumpObject(rsp));
        }

        private static string DumpObject(object obj)
        {
            if (obj == null) return "(null)";

            var sb = new StringBuilder();
            var t = obj.GetType();
            sb.AppendLine("TYPE: " + t.FullName);

            foreach (var pi in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = null;
                try { v = pi.GetValue(obj, null); } catch { v = "(unreadable)"; }
                sb.AppendLine(pi.Name + " = " + (v == null ? "(null)" : v.ToString()));
            }

            // also dump nested common sub-objects if present
            DumpSub(sb, obj, "AmountInformation");
            DumpSub(sb, obj, "AccountInformation");
            DumpSub(sb, obj, "TraceInformation");
            DumpSub(sb, obj, "HostInformation");
            DumpSub(sb, obj, "EmvInformation");

            return sb.ToString();
        }

        private static void DumpSub(StringBuilder sb, object obj, string prop)
        {
            try
            {
                var pi = obj.GetType().GetProperty(prop);
                var sub = pi?.GetValue(obj, null);
                if (sub == null) return;

                sb.AppendLine("");
                sb.AppendLine("SUBOBJ: " + prop + " (" + sub.GetType().FullName + ")");
                foreach (var spi in sub.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    object v = null;
                    try { v = spi.GetValue(sub, null); } catch { v = "(unreadable)"; }
                    sb.AppendLine("  " + spi.Name + " = " + (v == null ? "(null)" : v.ToString()));
                }
            }
            catch { }
        }

        private static string SafeStr(object obj, string prop)
        {
            try
            {
                if (obj == null) return "";
                var pi = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
                var v = pi?.GetValue(obj, null);
                return v == null ? "" : v.ToString();
            }
            catch { return ""; }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v;
            return "";
        }
    }
}