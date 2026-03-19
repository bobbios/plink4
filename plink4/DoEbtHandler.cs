using System;
using System.Globalization;
using System.Reflection;

namespace plink4
{
    internal static class DoEbtHandler
    {
        public static int Run(object term, ArgsModel a, out object rsp)
        {
            rsp = null;

            Logger.Info("DoEbtHandler.Run ENTER");
            Logger.Info($"DoEbtHandler.Run ref={a?.RefNum} amt={a?.Amount} txnType={a?.TxnType} cardType={a?.CardType}");

            if (term == null) throw new Exception("term is null.");
            if (a == null) throw new Exception("ArgsModel is null.");

            var tx = PoslinkReflection.RequireProperty(term, "Transaction", "Terminal.Transaction is null.");

            var req = PoslinkReflection.CreateRequest("DoEbt");
            rsp = PoslinkReflection.CreateResponse("DoEbt");

            Logger.Info("DoEbtHandler: req type = " + req.GetType().FullName);
            DumpRequestProperties(req);

            ApplyTransactionType(req, a);
            ApplyTrace(req, a);
            ApplyEbtType(req, a);
            ApplyAmounts(req, a);

            Logger.Info("DoEbtHandler: invoking DoEbt");
            int rc = PoslinkReflection.InvokeTxMethod(tx, "DoEbt", req, ref rsp);

            Logger.Info("DoEbtHandler: done rc=" + rc);
            return rc;
        }

        private static void ApplyTransactionType(object req, ArgsModel a)
        {
            string txnType = (a.TxnType ?? "").Trim().ToUpperInvariant();
            bool ok = false;

            switch (txnType)
            {
                case "SALE":
                    ok = PoslinkReflection.SetEnumProperty(req, "TransactionType", "Sale", "Purchase");
                    Logger.Info("ApplyTransactionType: SALE enumSet=" + ok);
                    break;

                case "RETURN":
                    {
                        bool okReturn = PoslinkReflection.SetEnumProperty(req, "TransactionType", "Return", "Refund");
                        bool okOrig = PoslinkReflection.SetEnumProperty(req, "OriginalTransactionType", "Sale", "Purchase");
                        Logger.Info($"ApplyTransactionType: RETURN enumSet={okReturn} origTxnSet={okOrig}");
                        break;
                    }

                case "BALANCE":
                case "BALANCEINQUIRY":
                case "BALANCE_INQUIRY":
                    ok = PoslinkReflection.SetEnumProperty(req, "TransactionType", "BalanceInquiry", "Balance");
                    Logger.Info("ApplyTransactionType: BALANCE enumSet=" + ok);
                    break;

                case "VOID":
                    ok = PoslinkReflection.SetEnumProperty(req, "TransactionType", "Void");
                    Logger.Info("ApplyTransactionType: VOID enumSet=" + ok);
                    break;

                default:
                    throw new Exception("Unsupported EBT txn type: " + a.TxnType);
            }
        }

        private static void ApplyTrace(object req, ArgsModel a)
        {
            var trace = PoslinkReflection.GetOrCreateProperty(req, "TraceInformation");
            if (trace == null)
            {
                Logger.Info("ApplyTrace: TraceInformation not found");
                return;
            }

            DumpChildProperties("TraceInformation", trace);

            TrySet(trace, "EcrReferenceNumber", a.RefNum);
            TrySet(trace, "InvoiceNumber", a.RefNum);

            // ONLY set original refs if you actually have the original sale ref
            if (!string.IsNullOrWhiteSpace(a.OriginalRef) && a.OriginalRef != "0")
            {
                TrySet(trace, "OriginalReferenceNumber", a.OriginalRef);
                TrySet(trace, "OriginalEcrReferenceNumber", a.OriginalRef);
            }

            Logger.Info("ApplyTrace: ref=" + a.RefNum + " origRef=" + a.OriginalRef);
        }

        private static void ApplyEbtType(object req, ArgsModel a)
        {
            var acct = PoslinkReflection.GetOrCreateProperty(req, "AccountInformation");
            if (acct == null)
            {
                Logger.Info("ApplyEbtType: AccountInformation not found");
                return;
            }

            DumpChildProperties("AccountInformation", acct);
            DumpEnumValues(acct, "EbtType");
            DumpEnumValues(acct, "CardType");

            string cardType = (a.CardType ?? "").Trim().ToUpperInvariant();
            bool okString = false;
            bool okEnum = false;

            switch (cardType)
            {
                case "EBT_FOOD":
                case "EBT_FOODSTAMP":
                    okString = TrySet(acct, "EbtType", "FOODSTAMP");
                    okEnum = PoslinkReflection.SetEnumProperty(acct, "EbtType",
                        "FOODSTAMP", "FoodStamp", "Food", "F");
                    Logger.Info($"ApplyEbtType: FOOD string={okString} enum={okEnum}");
                    break;

                case "EBT_CASH":
                case "EBT_CASHBENEFIT":
                    okString = TrySet(acct, "EbtType", "CashBenefits");
                    okEnum = PoslinkReflection.SetEnumProperty(acct, "EbtType",
                        "CashBenefits", "CASHBENEFITS");
                    Logger.Info($"ApplyEbtType: CASH string={okString} enum={okEnum}");
                    break;

                default:
                    throw new Exception("Unsupported EBT card type: " + a.CardType);
            }
        }

        private static void ApplyAmounts(object req, ArgsModel a)
        {
            string txnType = (a.TxnType ?? "").Trim().ToUpperInvariant();
            string cardType = (a.CardType ?? "").Trim().ToUpperInvariant();

            var amt = PoslinkReflection.GetOrCreateProperty(req, "AmountInformation");
            if (amt == null)
            {
                Logger.Info("ApplyAmounts: AmountInformation not found");
                return;
            }

            DumpChildProperties("AmountInformation", amt);

            if (txnType == "BALANCE" || txnType == "BALANCEINQUIRY" || txnType == "BALANCE_INQUIRY")
            {
                Logger.Info("ApplyAmounts: skipping amount for balance inquiry");
                return;
            }

            decimal saleAmount = ParseAmount(a.Amount);
            decimal cashBack = ParseAmount(a.Surcharge);

            TrySetMoney(amt, "TransactionAmount", saleAmount);

            if (txnType == "RETURN")
            {
                TrySetMoney(amt, "OriginalAmount", saleAmount);

                if (cardType == "EBT_CASH" || cardType == "EBT_CASHBENEFIT")
                    TrySetMoney(amt, "CashBackAmount", 0m);
            }
            else
            {
                if ((cardType == "EBT_CASH" || cardType == "EBT_CASHBENEFIT") && cashBack > 0)
                    TrySetMoney(amt, "CashBackAmount", cashBack);
            }

            Logger.Info($"ApplyAmounts: sale={saleAmount:0.00} cashback={cashBack:0.00} cardType={cardType} txnType={txnType}");
        }

        private static bool TrySet(object obj, string propName, object value)
        {
            bool ok = PoslinkReflection.SetProperty(obj, propName, value);
            Logger.Info($"TrySet: {(ok ? "SET" : "MISS")} {obj.GetType().Name}.{propName} = {value}");
            return ok;
        }

        private static void TrySetMoney(object obj, string propName, decimal amount)
        {
            if (TrySet(obj, propName, amount)) return;
            if (TrySet(obj, propName, amount.ToString("0.00", CultureInfo.InvariantCulture))) return;

            int cents = (int)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
            if (TrySet(obj, propName, cents)) return;
            if (TrySet(obj, propName, cents.ToString(CultureInfo.InvariantCulture))) return;

            Logger.Info($"TrySetMoney: unable to set {obj.GetType().Name}.{propName} with amount={amount}");
        }

        private static decimal ParseAmount(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0m;

            value = value.Trim().Replace("$", "").Replace(",", "");

            decimal result;
            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
                return result;

            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out result))
                return result;

            return 0m;
        }

        private static void DumpEnumValues(object obj, string propName)
        {
            try
            {
                var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null)
                {
                    Logger.Info($"DumpEnumValues: property {propName} not found");
                    return;
                }

                var t = pi.PropertyType;
                if (!t.IsEnum)
                {
                    Logger.Info($"DumpEnumValues: property {propName} is not enum");
                    return;
                }

                Logger.Info($"--- ENUM VALUES FOR {obj.GetType().Name}.{propName} ---");
                foreach (var name in Enum.GetNames(t))
                    Logger.Info("  " + name);
            }
            catch (Exception ex)
            {
                Logger.Info("DumpEnumValues failed: " + ex.Message);
            }
        }

        private static void DumpRequestProperties(object req)
        {
            try
            {
                Logger.Info("--- DoEbt Request PROPERTIES ---");
                foreach (var pi in req.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    Logger.Info($"  {pi.Name} ({pi.PropertyType.Name})");
            }
            catch (Exception ex)
            {
                Logger.Info("DumpRequestProperties failed: " + ex.Message);
            }
        }

        private static void DumpChildProperties(string label, object obj)
        {
            try
            {
                Logger.Info($"--- {label} PROPERTIES ---");
                foreach (var pi in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    Logger.Info($"  {label}.{pi.Name} ({pi.PropertyType.Name})");
            }
            catch (Exception ex)
            {
                Logger.Info($"DumpChildProperties failed for {label}: " + ex.Message);
            }
        }
    }
}