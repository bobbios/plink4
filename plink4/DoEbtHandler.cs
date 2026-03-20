using System;
using System.Globalization;

namespace plink4
{
    internal static class DoEbtHandler
    {
        public static int Run(object term, ArgsModel model, out object response)
        {
            response = null;

            if (term == null) throw new ArgumentNullException(nameof(term));
            if (model == null) throw new ArgumentNullException(nameof(model));

            try
            {
                object transaction = PoslinkReflection.RequireProperty(
                    term,
                    "Transaction",
                    "Transaction property is null on terminal object."
                );

                object request = PoslinkReflection.CreateRequest("DoEbt");

                // You can try removing this line if InvokeTxMethod populates response directly
                object responseObj = PoslinkReflection.CreateResponse("DoEbt");

                ApplyTransactionType(request, model);
                ApplyTrace(request, model);
                ApplyEbtType(request, model);
                ApplyAmounts(request, model);

                int returnCode = PoslinkReflection.InvokeTxMethod(
                    transaction,
                    "DoEbt",
                    request,
                    ref responseObj
                );

                response = responseObj;
                return returnCode;
            }
            catch (Exception)
            {
                throw; // Let CommandRouter handle exceptions
            }
        }

        private static void ApplyTransactionType(object req, ArgsModel model)
        {
            string txnType = (model.TxnType ?? "").Trim().ToUpperInvariant();

            switch (txnType)
            {
                case "SALE":
                    PoslinkReflection.SetEnumProperty(req, "TransactionType", "Sale", "Purchase");
                    break;

                case "RETURN":
                    PoslinkReflection.SetEnumProperty(req, "TransactionType", "Return", "Refund");
                    PoslinkReflection.SetEnumProperty(req, "OriginalTransactionType", "Sale", "Purchase");
                    break;

                case "BALANCE":
                case "BALANCEINQUIRY":
                case "BALANCE_INQUIRY":
                    PoslinkReflection.SetEnumProperty(req, "TransactionType", "BalanceInquiry", "Balance");
                    break;

                case "VOID":
                    PoslinkReflection.SetEnumProperty(req, "TransactionType", "Void");
                    break;

                default:
                    throw new ArgumentException($"Unsupported EBT transaction type: {model.TxnType}");
            }
        }

        private static void ApplyTrace(object req, ArgsModel model)
        {
            var trace = PoslinkReflection.GetOrCreateProperty(req, "TraceInformation");
            if (trace == null) return;

            PoslinkReflection.SetProperty(trace, "EcrReferenceNumber", model.RefNum);
            PoslinkReflection.SetProperty(trace, "InvoiceNumber", model.RefNum);

            // Uncomment only if you later support original reference numbers
            // if (!string.IsNullOrWhiteSpace(model.OriginalRef) && model.OriginalRef != "0")
            // {
            //     PoslinkReflection.SetProperty(trace, "OriginalReferenceNumber", model.OriginalRef);
            //     PoslinkReflection.SetProperty(trace, "OriginalEcrReferenceNumber", model.OriginalRef);
            // }
        }

        private static void ApplyEbtType(object req, ArgsModel model)
        {
            var account = PoslinkReflection.GetOrCreateProperty(req, "AccountInformation");
            if (account == null) return;

            string cardType = (model.CardType ?? "").Trim().ToUpperInvariant();

            switch (cardType)
            {
                case "EBT_FOOD":
                case "EBT_FOODSTAMP":
                    PoslinkReflection.SetProperty(account, "EbtType", "FOODSTAMP");
                    PoslinkReflection.SetEnumProperty(account, "EbtType", "FOODSTAMP", "FoodStamp", "Food", "F");
                    break;

                case "EBT_CASH":
                case "EBT_CASHBENEFIT":
                    PoslinkReflection.SetProperty(account, "EbtType", "CashBenefits");
                    PoslinkReflection.SetEnumProperty(account, "EbtType", "CashBenefits", "CASHBENEFITS");
                    break;

                default:
                    throw new ArgumentException($"Unsupported EBT card type: {model.CardType}");
            }
        }

        private static void ApplyAmounts(object req, ArgsModel model)
        {
            string txnType = (model.TxnType ?? "").Trim().ToUpperInvariant();
            string cardType = (model.CardType ?? "").Trim().ToUpperInvariant();

            var amounts = PoslinkReflection.GetOrCreateProperty(req, "AmountInformation");
            if (amounts == null) return;

            // No amount needed for balance inquiries
            if (txnType == "BALANCE" || txnType == "BALANCEINQUIRY" || txnType == "BALANCE_INQUIRY")
                return;

            decimal saleAmount = ParseAmount(model.Amount);
            decimal cashBack = ParseAmount(model.Surcharge);

            TrySetMoney(amounts, "TransactionAmount", saleAmount);
            if (txnType == "RETURN")
            {
                TrySetMoney(amounts, "OriginalAmount", saleAmount);
                if (cardType == "EBT_CASH" || cardType == "EBT_CASHBENEFIT")
                    TrySetMoney(amounts, "CashBackAmount", 0m);
            }
            else
            {
                if ((cardType == "EBT_CASH" || cardType == "EBT_CASHBENEFIT") && cashBack > 0)
                    TrySetMoney(amounts, "CashBackAmount", cashBack);
            }
        }

        private static bool TrySet(object obj, string propName, object value)
        {
            return PoslinkReflection.SetProperty(obj, propName, value);
        }

        private static void TrySetMoney(object obj, string propName, decimal amount)
        {
            if (TrySet(obj, propName, amount)) return;

            if (TrySet(obj, propName, amount.ToString("0.00", CultureInfo.InvariantCulture))) return;

            int cents = (int)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
            if (TrySet(obj, propName, cents)) return;
            if (TrySet(obj, propName, cents.ToString(CultureInfo.InvariantCulture))) return;

            // silent fail – continue without setting this field
        }

        private static decimal ParseAmount(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0m;

            value = value.Trim().Replace("$", "").Replace(",", "");

            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
                return result;

            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out result))
                return result;

            return 0m;
        }
    }
}