using System;
using System.IO;
using System.Linq;
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

            string resultTxt = SafeStr(rspObj, "ResponseMessage");

            string response = GetNestedStr(rspObj, "HostInformation", "HostResponseMessage");

            string transDisplay = BuildTransactionLabel(cardType, txnType);

            string transactionNumber = FirstNonEmpty(
        GetNestedStr(rspObj, "TraceInformation", "RefNum"),
        GetNestedStr(rspObj, "TraceInformation", "ReferenceNumber")
    );

            string cardTypeDisplay;

            switch ((cardType ?? "").ToUpperInvariant())
            {
                case "EBT_FOODSTAMP":
                case "EBT_FOOD":
                    cardTypeDisplay = "EBT_FOOD";
                    break;

                case "EBT_CASHBENEFIT":
                case "EBT_CASH":
                    cardTypeDisplay = "EBT_CASH";
                    break;

                default:
                    cardTypeDisplay = FirstNonEmpty(
                        GetNestedStr(rspObj, "AccountInformation", "CardType"),
                        SafeStr(rspObj, "CardType"),
                        cardType
                    );
                    break;
            }

            string account = Last4(GetNestedStr(rspObj, "AccountInformation", "Account"));

            string entryCode = GetNestedStr(rspObj, "AccountInformation", "EntryMode");

            string entry;

            switch (entryCode)
            {
                case "0":
                    entry = "Manual";
                    break;
                case "1":
                    entry = "Swipe";
                    break;
                case "2":
                    entry = "Contactless";
                    break;
                case "3":
                    entry = "Chip";
                    break;
                case "4":
                    entry = "Fallback";
                    break;
                default:
                    entry = entryCode;
                    break;
            }

            string amountRaw = FirstNonEmpty(
                GetNestedStr(rspObj, "AmountInformation", "ApproveAmount"),
                GetNestedStr(rspObj, "AmountInformation", "ApprovedAmount")
            );
                        string amount = PaxAmount(amountRaw);


            string refNumber = FirstNonEmpty(
                GetNestedStr(rspObj, "TraceInformation", "RefNum"),
                GetNestedStr(rspObj, "TraceInformation", "ReferenceNumber")
            );



            string remainingRaw = GetNestedStr(rspObj, "AmountInformation", "Balance1");

            decimal remainingDec;
            string remainingBalance = decimal.TryParse(remainingRaw, out remainingDec)
                ? "$" + (remainingDec * 0.01m).ToString("0.00")
                : "";

            string extraRaw = FirstNonEmpty(
                GetNestedStr(rspObj, "AmountInformation", "Balance2")
            );
            decimal extraDec;
            string extraBalance = decimal.TryParse(extraRaw, out extraDec)
                ? "$" + (extraDec * 0.01m).ToString("0.00")
                : "";

            var sb = new StringBuilder();

            sb.AppendLine("ResultTxt :" + resultTxt);
            sb.AppendLine(DateTime.Now.ToString("MM/dd/yyyy").PadRight(24) + DateTime.Now.ToString("HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine(transDisplay + ":");
            sb.AppendLine();
            sb.AppendLine(PadLabel("Transaction #:", transactionNumber));
            sb.AppendLine();
            sb.AppendLine(PadLabel("Card Type:", cardTypeDisplay));
            sb.AppendLine();
            sb.AppendLine(PadLabel("Account:", account));
            sb.AppendLine();
            sb.AppendLine(PadLabel("Entry", entry));
            sb.AppendLine();
            sb.AppendLine(PadLabel("Amount:", amount));
            sb.AppendLine();
            sb.AppendLine(PadLabel("Ref. Number:", refNumber));
            sb.AppendLine();
            sb.AppendLine(PadLabel("Auth Code:", authCode));
            sb.AppendLine();
            sb.AppendLine(PadLabel("Response:", response));
            sb.AppendLine();
            sb.AppendLine(PadLabel("Remaining Balance:", remainingBalance));
            sb.AppendLine();
            sb.AppendLine(PadLabel("Extra Balance:", extraBalance));
            sb.AppendLine();

            File.WriteAllText(AppConfig.OutResponse, sb.ToString());
        }
        private static string PaxAmount(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            decimal value;
            if (!decimal.TryParse(raw, out value))
                return "";

            return "$" + (value * 0.01m).ToString("0.00");
        }
        public static void WriteDump(object rsp)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppConfig.OutResponse2));

            var sb = new StringBuilder();

            sb.AppendLine("PLCardPresent " + FirstNonEmpty(
                GetNestedStr(rsp, "AccountInformation", "CardPresentIndicator"),
                "0"
            ));

            string entryModeCode = FirstNonEmpty(
                GetNestedStr(rsp, "AccountInformation", "EntryMode"),
                ""
            );

            string entryModeLabel;

            switch (entryModeCode)
            {
                case "0": entryModeLabel = "Manual"; break;
                case "1": entryModeLabel = "Swipe"; break;
                case "2": entryModeLabel = "Contactless"; break;
                case "3": entryModeLabel = "Chip"; break;
                case "4": entryModeLabel = "Fallback"; break;
                default: entryModeLabel = ""; break;
            }

            sb.AppendLine("PLEntryMode " + entryModeCode +
                (string.IsNullOrWhiteSpace(entryModeLabel) ? "" : " (" + entryModeLabel + ")"));

            sb.AppendLine("PLNameOnCard " + FirstNonEmpty(
                GetNestedStr(rsp, "AccountInformation", "CardHolder"),
                ""
            ));

            sb.AppendLine("AmountDue " + FirstNonEmpty(
                GetNestedStr(rsp, "AmountInformation", "AmountDue"),
                "0"
            ));

            sb.AppendLine("TipAmount " + FirstNonEmpty(
                GetNestedStr(rsp, "AmountInformation", "TipAmount"),
                "0"
            ));

            sb.AppendLine("CashBackAmout " + FirstNonEmpty(
                GetNestedStr(rsp, "AmountInformation", "CashBackAmount"),
                GetNestedStr(rsp, "AmountInformation", "CashBackAmout"),
                "0"
            ));

            sb.AppendLine("MerchantFee " + FirstNonEmpty(
                GetNestedStr(rsp, "AmountInformation", "MerchantFee"),
                "0"
            ));

            sb.AppendLine("TaxAmount " + FirstNonEmpty(
                GetNestedStr(rsp, "AmountInformation", "TaxAmount"),
                "0"
            ));

            sb.AppendLine("ExpDate " + FirstNonEmpty(
                GetNestedStr(rsp, "AccountInformation", "ExpireDate"),
                GetNestedStr(rsp, "AccountInformation", "ExpDate"),
                ""
            ));

            sb.AppendLine("ECRRefNum " + FirstNonEmpty(
                GetNestedStr(rsp, "TraceInformation", "EcrRefNum"),
                GetNestedStr(rsp, "TraceInformation", "ECRRefNum"),
                ""
            ));

            sb.AppendLine();
            sb.AppendLine("---- Full Response Dump ----");

            DumpSection(sb, rsp, "HostInformation");
            AppendSimpleProperty(sb, rsp, "TransactionType");
            DumpSection(sb, rsp, "AmountInformation");
            DumpSection(sb, rsp, "AccountInformation");
            DumpSection(sb, rsp, "TraceInformation");
            DumpSection(sb, rsp, "Restaurant");
            DumpSection(sb, rsp, "PaymentTransInfo");
            DumpSection(sb, rsp, "CardInfo");
            DumpSection(sb, rsp, "MultiMerchant");
            DumpSection(sb, rsp, "PaymentEmvTag");
            DumpSection(sb, rsp, "FleetCard");
            DumpSection(sb, rsp, "VasInformation");
            DumpSection(sb, rsp, "TorInformation");
            AppendSimpleProperty(sb, rsp, "ResponseCode");
            AppendSimpleProperty(sb, rsp, "ResponseMessage");

            File.WriteAllText(AppConfig.OutResponse2, sb.ToString());
        }

        private static void DumpSection(StringBuilder sb, object parent, string sectionName)
        {
            try
            {
                if (parent == null) return;

                var pi = parent.GetType().GetProperty(sectionName, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null) return;

                var sectionObj = pi.GetValue(parent, null);
                if (sectionObj == null)
                {
                    sb.AppendLine(sectionName + ":");
                    return;
                }

                sb.AppendLine(sectionName + ":");

                foreach (var p in sectionObj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    object value = null;
                    try
                    {
                        value = p.GetValue(sectionObj, null);
                    }
                    catch
                    {
                        value = "(unreadable)";
                    }

                    sb.AppendLine("    " + p.Name + ": " + FormatDumpValue(value));
                }
            }
            catch
            {
            }
        }

        private static void AppendSimpleProperty(StringBuilder sb, object obj, string propName)
        {
            sb.AppendLine(propName + ": " + FirstNonEmpty(
                SafeStr(obj, propName),
                "(null)"
            ));
        }

        private static string FormatDumpValue(object value)
        {
            if (value == null) return "(null)";
            return value.ToString();
        }

        private static string EntryModeText(string entryMode)
        {
            switch ((entryMode ?? "").Trim())
            {
                case "0": return " (Manual)";
                case "1": return " (Swipe)";
                case "2": return " (Contactless)";
                case "3": return " (Chip)";
                case "4": return " (Fallback)";
                default: return "";
            }
        }

        private static string BuildTransactionLabel(string cardType, string txnType)
        {
            string ct = (cardType ?? "").Trim().ToUpperInvariant();
            string tt = (txnType ?? "").Trim().ToUpperInvariant();

            // Force correct EBT labels
            if (ct == "EBT_FOOD")
                ct = "EBT_FOODSTAMP";

            if (ct == "EBT_CASH")
                ct = "EBT_CASHBENEFIT";

            if (string.IsNullOrWhiteSpace(ct) && string.IsNullOrWhiteSpace(tt))
                return "TRANSACTION";

            return (ct + " " + tt).Trim();
        }

        private static string PadLabel(string label, string value)
        {
            return label.PadRight(28) + (value ?? "");
        }

        private static string Last4(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length >= 4)
                return digits.Substring(digits.Length - 4);

            return value.Trim();
        }

        private static string FormatAmount(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            decimal dec;
            if (decimal.TryParse(value, out dec))
                return "$" + dec.ToString("0.00");

            return value;
        }

        private static string FormatAmountAllowBlank(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return FormatAmount(value);
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
    }
}