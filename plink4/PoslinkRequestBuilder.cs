using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace plink4
{
    internal static class PoslinkRequestBuilder
    {
        public static void ApplyTrace(object req, string refNum)
        {
            if (req == null || string.IsNullOrWhiteSpace(refNum)) return;

            var trace =
                PoslinkReflection.GetProperty(req, "TraceInformation") ??
                PoslinkReflection.GetProperty(req, "TraceInfo") ??
                PoslinkReflection.GetProperty(req, "Trace") ??
                PoslinkReflection.GetProperty(req, "TraceData");

            if (trace == null)
            {
                trace =
                    PoslinkReflection.GetOrCreateProperty(req, "TraceInformation") ??
                    PoslinkReflection.GetOrCreateProperty(req, "TraceInfo") ??
                    PoslinkReflection.GetOrCreateProperty(req, "Trace") ??
                    PoslinkReflection.GetOrCreateProperty(req, "TraceData");
            }

            if (trace == null) return;

            string[] props =
            {
            "EcrReferenceNumber", "InvoiceNumber", "EcrRefNum", "InvoiceNo",
            "InvoiceNum", "RefNo", "RefNum", "ReferenceNumber",
            "TransId", "TransactionId", "TicketNo", "SequenceNumber"
        };

            foreach (var prop in props)
            {
                PoslinkReflection.SetProperty(trace, prop, refNum);

                if (long.TryParse(refNum, out var n))
                    PoslinkReflection.SetProperty(trace, prop, n);
            }
        }

        public static void ApplySaleReturnTransactionType(object req, string txnType)
        {
            var txn = (txnType ?? "").Trim().ToUpperInvariant();

            if (txn == "SALE")
                PoslinkReflection.SetEnumProperty(req, "TransactionType", "Sale");
            else if (txn == "RETURN")
                PoslinkReflection.SetEnumProperty(req, "TransactionType", "Return");
            else
                throw new Exception("Unsupported transaction type: " + txnType);
        }

        public static void ApplyCreditTransactionType(object req, string txnType)
        {
            ApplySaleReturnTransactionType(req, txnType);
        }

        public static void ApplySimpleAmount(object req, string amount)
        {
            var amt =
                PoslinkReflection.GetOrCreateProperty(req, "AmountInformation") ??
                PoslinkReflection.GetOrCreateProperty(req, "AmountInfo");

            if (amt == null) return;

            PoslinkReflection.SetProperty(amt, "TransactionAmount", (amount ?? "").Trim());
        }

        public static void ApplyCreditAmounts(object req, string amount, string surcharge, string txnType)
        {
            var txn = (txnType ?? "").Trim().ToUpperInvariant();

            string finalAmount = (amount ?? "").Trim();
            string surchargeAmount = (surcharge ?? "").Trim();

            if (txn == "SALE")
            {
                long.TryParse(finalAmount, out var baseCents);
                long.TryParse(surchargeAmount, out var surchargeCents);
                finalAmount = (baseCents + surchargeCents).ToString();
            }

            ApplySimpleAmount(req, finalAmount);

            var amt =
                PoslinkReflection.GetOrCreateProperty(req, "AmountInformation") ??
                PoslinkReflection.GetOrCreateProperty(req, "AmountInfo");

            if (amt != null && txn == "SALE" && !string.IsNullOrWhiteSpace(surchargeAmount) && surchargeAmount != "0")
            {
                PoslinkReflection.SetProperty(amt, "SurchargeAmount", surchargeAmount);
                PoslinkReflection.SetProperty(amt, "FeeAmount", surchargeAmount);
            }
        }

        public static void ApplyEbtSettings(object req, ArgsModel a)
        {
            var isCash = string.Equals(a.CardType, "EBT_CASHBENEFIT", StringComparison.OrdinalIgnoreCase);

            var behavior = PoslinkReflection.GetOrCreateProperty(req, "TransactionBehavior");
            var entryMode = behavior != null
                ? PoslinkReflection.GetOrCreateProperty(behavior, "EntryMode")
                : null;

            if (entryMode != null)
            {
                PoslinkReflection.SetProperty(entryMode, "Chip", true);
                PoslinkReflection.SetProperty(entryMode, "Swipe", true);
            }

            var acct =
                PoslinkReflection.GetOrCreateProperty(req, "AccountInformation") ??
                PoslinkReflection.GetOrCreateProperty(req, "AccountInfo");

            if (acct != null)
            {
                PoslinkReflection.SetEnumProperty(
                    acct,
                    "EbtType",
                    isCash ? "Cash" : "Food",
                    isCash ? "CashBenefit" : "FoodStamp",
                    isCash ? "C" : "F");
            }

            var txn = (a.TxnType ?? "").Trim().ToUpperInvariant();
            if (txn == "SALE" || txn == "RETURN")
                ApplySimpleAmount(req, a.Amount);
        }

        public static void ApplyEbtTransactionType(object req, ArgsModel a)
        {
            var txn = (a.TxnType ?? "").Trim().ToUpperInvariant();
            var isCash = string.Equals(a.CardType, "EBT_CASHBENEFIT", StringComparison.OrdinalIgnoreCase);

            if (txn == "SALE")
            {
                if (!PoslinkReflection.SetEnumProperty(
                    req,
                    "TransactionType",
                    isCash ? "EbtCash" : "EbtFood",
                    isCash ? "EBTCash" : "EBTFood",
                    isCash ? "CashBenefit" : "FoodStamp",
                    "Sale"))
                {
                    throw new Exception("Could not set EBT sale transaction type.");
                }
            }
            else if (txn == "RETURN")
            {
                if (!PoslinkReflection.SetEnumProperty(req, "TransactionType", "EbtCashReturn", "EbtFoodReturn", "Return"))
                    throw new Exception("Could not set EBT return transaction type.");
            }
            else if (txn == "INQUIRY" || txn == "INQURY" || txn == "EBTBALANCE")
            {
                if (!PoslinkReflection.SetEnumProperty(req, "TransactionType", "Inquiry", "BalanceInquiry", "Balance"))
                    throw new Exception("Could not set EBT inquiry transaction type.");
            }
            else
            {
                throw new Exception("Unsupported EBT TxnType: " + a.TxnType);
            }
        }
    }
}
