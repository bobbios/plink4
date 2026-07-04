using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace plink4
{
    internal static class LegacyResponseWriter
    {
        public static void WriteFromRsp(string cardType, string txnType, bool ok, object rsp)
        {
            string respCode = FirstNonEmpty(
                SafeStr(rsp, "ResponseCode")
            );

            string respMsg = FirstNonEmpty(
                SafeStr(rsp, "ResponseMessage"),
                SafeStr(rsp, "HostResponseMessage")
            );

            string authCode = FirstNonEmpty(
                GetNestedStr(rsp, "HostInformation", "AuthorizationCode"),
                GetNestedStr(rsp, "HostInformation", "AuthCode"),
                SafeStr(rsp, "AuthCode")
            );

            WriteLegacy(cardType, txnType, ok, respMsg, respCode, authCode, rsp);
        }

        public static void WriteLegacy(string cardType, string txnType, bool ok,
            string responseMessage, string responseCode, string authCode, object rspObj = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppConfig.OutResponse));

            var lines = new string[26];

            lines[0] = "Result|" + (ok ? "APPROVAL" : (responseMessage ?? "ERROR"));
            lines[1] = "CardType|" + FirstNonEmpty(
                NormalizeCardBrand(GetNestedStr(rspObj, "AccountInformation", "CardType")),
                cardType
            );
            lines[2] = "TxnType|" + (txnType ?? "");
            lines[3] = "Time|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            lines[4] = "TransType|" + FirstNonEmpty(
                SafeStr(rspObj, "TransactionType")
            );

            lines[5] = "Account|" + FirstNonEmpty(
                GetNestedStr(rspObj, "AccountInformation", "Account"),
                SafeStr(rspObj, "AccountNumber"),
                SafeStr(rspObj, "MaskedAccount"),
                SafeStr(rspObj, "CardNumber"),
                SafeStr(rspObj, "PAN")
            );

            lines[6] = "ExpDate|" + FirstNonEmpty(
                GetNestedStr(rspObj, "AccountInformation", "ExpireDate"),
                SafeStr(rspObj, "ExpireDate"),
                SafeStr(rspObj, "ExpDate")
            );

            lines[7] = "RecNo|" + FirstNonEmpty(
                GetNestedStr(rspObj, "TraceInformation", "ReferenceNumber"),
                GetNestedStr(rspObj, "TraceInformation", "RefNum"),
                SafeStr(rspObj, "RecordNo"),
                SafeStr(rspObj, "RecNo"),
                SafeStr(rspObj, "ReferenceNumber")
            );

            lines[8] = "Amount|" + FirstNonEmpty(
                GetNestedStr(rspObj, "AmountInformation", "ApprovedAmount"),
                GetNestedStr(rspObj, "AmountInformation", "ApproveAmount"),
                GetNestedStr(rspObj, "AmountInformation", "TransactionAmount"),
                SafeStr(rspObj, "ApprovedAmount"),
                SafeStr(rspObj, "TransactionAmount")
            );

            lines[9] = "HostRef|" + FirstNonEmpty(
                GetNestedStr(rspObj, "HostInformation", "HostReferenceNumber"),
                GetNestedStr(rspObj, "HostInformation", "HostRefNum"),
                SafeStr(rspObj, "HostRefNum"),
                SafeStr(rspObj, "ReferenceNum"),
                SafeStr(rspObj, "RefNum")
            );

            lines[10] = "AuthCode|" + FirstNonEmpty(
                authCode,
                GetNestedStr(rspObj, "HostInformation", "AuthorizationCode"),
                GetNestedStr(rspObj, "HostInformation", "AuthCode")
            );

            lines[11] = "ResponseCode|" + (responseCode ?? "");
            lines[12] = "ResponseMessage|" + (responseMessage ?? "");

            lines[13] = "RemainingBalance|" + FirstNonEmpty(
                GetNestedStr(rspObj, "AmountInformation", "Balance1"),
                SafeStr(rspObj, "RemainingBalance"),
                SafeStr(rspObj, "Balance"),
                SafeStr(rspObj, "AvailableBalance")
            );

            lines[14] = "Field15|" + FirstNonEmpty(
                GetNestedStr(rspObj, "AmountInformation", "Balance2")
            );

            for (int i = 15; i < 26; i++)
                lines[i] = "Field" + (i + 1) + "|";

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
            sb.AppendLine("TYPE|" + t.FullName);

            foreach (var pi in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = null;
                try { v = pi.GetValue(obj, null); } catch { v = "(unreadable)"; }
                sb.AppendLine(pi.Name + "|" + (v == null ? "" : v.ToString()));
            }

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

                foreach (var spi in sub.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    object v = null;
                    try { v = spi.GetValue(sub, null); } catch { v = "(unreadable)"; }
                    sb.AppendLine(prop + "." + spi.Name + "|" + (v == null ? "" : v.ToString()));
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

        private static string GetNestedStr(object obj, string parentProp, string childProp)
        {
            try
            {
                if (obj == null) return "";
                var p1 = obj.GetType().GetProperty(parentProp, BindingFlags.Public | BindingFlags.Instance);
                var parent = p1?.GetValue(obj, null);
                if (parent == null) return "";

                var p2 = parent.GetType().GetProperty(childProp, BindingFlags.Public | BindingFlags.Instance);
                var val = p2?.GetValue(parent, null);
                return val == null ? "" : val.ToString();
            }
            catch { return ""; }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v;
            return "";
        }

        // AccountInformation.CardType comes back as a friendly name ("Visa",
        // "MasterCard", ...) on this SDK build, but numeric codes are handled too
        // in case a different SDK version reverts to them. "NotSet"/blank (e.g.
        // EBT, or a declined transaction with no card data) falls through to the
        // caller's raw CardType ("CREDIT"/"DEBIT"/"EBT_FOOD"...) instead. The
        // resolved brand is capped to 4 letters (VISA/MAST/AMEX/DISC/...).
        private static string NormalizeCardBrand(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            if (string.Equals(raw, "NotSet", StringComparison.OrdinalIgnoreCase)) return "";

            string brand;
            switch (raw)
            {
                case "01": brand = "Visa"; break;
                case "02": brand = "MasterCard"; break;
                case "03": brand = "AMEX"; break;
                case "04": brand = "Discover"; break;
                case "05": brand = "DinerClub"; break;
                case "06": brand = "enRoute"; break;
                default: brand = raw; break;
            }

            string upper = brand.ToUpperInvariant();
            return upper.Length > 4 ? upper.Substring(0, 4) : upper;
        }
    }
}