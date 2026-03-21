using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.IO;



namespace plink4
{
    internal static class DoEbtReturnHandler
    {
        public static int Run(object terminal, ArgsModel model, out object response)
        {
            response = null;

            Logger.Debug("DoEbtReturnHandler.Run start");

            if (terminal == null)
                throw new ArgumentNullException(nameof(terminal));

            if (model == null)
                throw new ArgumentNullException(nameof(model));

            Logger.Debug("CardType=" + Safe(model.CardType) +
                         " TxnType=" + Safe(model.TxnType) +
                         " RefNum=" + Safe(model.RefNum) +
                         " Amount=" + Safe(model.Amount));

            string cardType = (model.CardType ?? "").Trim().ToUpperInvariant();
            string txnType = (model.TxnType ?? "").Trim().ToUpperInvariant();
            if (cardType == "EBT_CASH" && txnType == "RETURN")
            {
                Logger.Debug("EBT_CASH RETURN blocked");

                string responseFile = AppConfig.OutResponse;
                string msg = "EBT CASH RETURN NOT ALLOWED. RETURN TO FOODSTAMP OR GIVE CASH.";

                Directory.CreateDirectory(Path.GetDirectoryName(responseFile));

                File.WriteAllText(responseFile,
                    "ResultTxt: " + msg + "\r\n");

                Logger.Debug("Custom response written: " + responseFile);

                response = null;
                return -1;
            }



            object transaction = PoslinkReflection.RequireProperty(
                terminal,
                "Transaction",
                "Transaction property is null on terminal object."
            );

            Logger.Debug("Transaction object acquired: " + transaction.GetType().FullName);

            object request = PoslinkReflection.CreateRequest("DoEbt");
            object responseObj = PoslinkReflection.CreateResponse("DoEbt");

            Logger.Debug("DoEbt request type: " + request.GetType().FullName);
            Logger.Debug("DoEbt response type: " + responseObj.GetType().FullName);

            ApplyTransactionType(request);
            ApplyTrace(request, model);
            ApplyEbtType(request, model);
            ApplyAmounts(request, model);

            Logger.Debug("Invoking Transaction.DoEbt...");
            int returnCode = PoslinkReflection.InvokeTxMethod(
                transaction,
                "DoEbt",
                request,
                ref responseObj
            );

            Logger.Debug("Transaction.DoEbt returnCode=" + returnCode);

            response = responseObj;
            return returnCode;
        }

        private static void ApplyTransactionType(object req)
        {
            Logger.Debug("ApplyTransactionType: trying Return/Refund");
            PoslinkReflection.SetEnumProperty(req, "TransactionType", "Return", "Refund");
        }

        private static void ApplyTrace(object req, ArgsModel model)
        {
            object trace = PoslinkReflection.GetOrCreateProperty(req, "TraceInformation");
            if (trace == null)
            {
                Logger.Debug("ApplyTrace: TraceInformation is null");
                return;
            }

            Logger.Debug("ApplyTrace: TraceInformation type=" + trace.GetType().FullName);

            if (!string.IsNullOrWhiteSpace(model.RefNum))
            {
                Logger.Debug("ApplyTrace: setting EcrReferenceNumber=" + model.RefNum);
                PoslinkReflection.SetProperty(trace, "EcrReferenceNumber", model.RefNum);

                Logger.Debug("ApplyTrace: setting InvoiceNumber=" + model.RefNum);
                PoslinkReflection.SetProperty(trace, "InvoiceNumber", model.RefNum);
            }
        }

        private static void ApplyEbtType(object req, ArgsModel model)
        {
            object account = PoslinkReflection.GetOrCreateProperty(req, "AccountInformation");
            if (account == null)
                throw new Exception("AccountInformation is null on DoEbt request.");

            Logger.Debug("ApplyEbtType: AccountInformation type=" + account.GetType().FullName);

            string cardType = Safe(model.CardType).ToUpperInvariant();
            Logger.Debug("ApplyEbtType: cardTypeUpper=" + cardType);

            PropertyInfo prop = account.GetType().GetProperty("EbtType", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
                throw new Exception("EbtType property not found on " + account.GetType().FullName);

            Type propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            Logger.Debug("ApplyEbtType: EbtType property type=" + propType.FullName);

            if (propType.IsEnum)
            {
                string[] names = Enum.GetNames(propType);
                Logger.Debug("ApplyEbtType: enum names = " + string.Join(", ", names));

                string[] candidates;

                switch (cardType)
                {
                    case "EBT_FOOD":
                    case "EBT_FOODSTAMP":
                        candidates = new[] { "FoodStamp", "Food", "F" };
                        break;

                    case "EBT_CASH":
                    case "EBT_CASHBENEFIT":
                        candidates = new[] { "CashBenefit", "Cash", "C" };
                        break;

                    default:
                        throw new NotSupportedException("Unsupported EBT return CardType: " + model.CardType);
                }

                foreach (string candidate in candidates)
                {
                    string match = names.FirstOrDefault(n =>
                        string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase));

                    Logger.Debug("ApplyEbtType: enum candidate=" + candidate + " match=" + Safe(match));

                    if (!string.IsNullOrWhiteSpace(match))
                    {
                        object enumValue = Enum.Parse(propType, match, ignoreCase: true);
                        prop.SetValue(account, enumValue, null);

                        object readBack = prop.GetValue(account, null);
                        Logger.Debug("ApplyEbtType: enum set success, readback=" +
                                     (readBack == null ? "(null)" : readBack.ToString()));
                        return;
                    }
                }

                throw new Exception("No matching EbtType enum value found for CardType=" + model.CardType);
            }

            // Fallback only if EbtType is not enum
            switch (cardType)
            {
                case "EBT_FOOD":
                case "EBT_FOODSTAMP":
                    Logger.Debug("ApplyEbtType: non-enum fallback FOOD");
                    if (!PoslinkReflection.SetProperty(account, "EbtType", "FoodStamp"))
                        PoslinkReflection.SetProperty(account, "EbtType", "F");
                    break;

                case "EBT_CASH":
                case "EBT_CASHBENEFIT":
                    Logger.Debug("ApplyEbtType: non-enum fallback CASH");
                    if (!PoslinkReflection.SetProperty(account, "EbtType", "CashBenefit"))
                        PoslinkReflection.SetProperty(account, "EbtType", "C");
                    break;

                default:
                    throw new NotSupportedException("Unsupported EBT return CardType: " + model.CardType);
            }

            object fallbackReadBack = prop.GetValue(account, null);
            Logger.Debug("ApplyEbtType: fallback readback=" +
                         (fallbackReadBack == null ? "(null)" : fallbackReadBack.ToString()));
        }

        private static void ApplyAmounts(object req, ArgsModel model)
        {
            object amountInfo = PoslinkReflection.GetOrCreateProperty(req, "AmountInformation");
            if (amountInfo == null)
                throw new Exception("AmountInformation is null on DoEbt request.");

            Logger.Debug("ApplyAmounts: AmountInformation type=" + amountInfo.GetType().FullName);

            decimal amount = ParseAmount(model.Amount);
            Logger.Debug("ApplyAmounts: parsed amount=" + amount.ToString("0.00", CultureInfo.InvariantCulture));

            if (amount <= 0)
                throw new Exception("EBT RETURN amount must be greater than zero.");

            Logger.Debug("ApplyAmounts: setting TransactionAmount");
            TrySetMoney(amountInfo, "TransactionAmount", amount);

            Logger.Debug("ApplyAmounts: setting CashBackAmount=0");
            TrySetMoney(amountInfo, "CashBackAmount", 0m);
        }

        private static void TrySetMoney(object target, string propertyName, decimal amount)
        {
            Logger.Debug("TrySetMoney: " + propertyName + " decimal=" + amount.ToString("0.00", CultureInfo.InvariantCulture));
            if (PoslinkReflection.SetProperty(target, propertyName, amount))
            {
                Logger.Debug("TrySetMoney: success via decimal");
                return;
            }

            string amountText = amount.ToString("0.00", CultureInfo.InvariantCulture);
            Logger.Debug("TrySetMoney: " + propertyName + " string=" + amountText);
            if (PoslinkReflection.SetProperty(target, propertyName, amountText))
            {
                Logger.Debug("TrySetMoney: success via string");
                return;
            }

            int cents = (int)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
            Logger.Debug("TrySetMoney: " + propertyName + " cents int=" + cents);
            if (PoslinkReflection.SetProperty(target, propertyName, cents))
            {
                Logger.Debug("TrySetMoney: success via int cents");
                return;
            }

            Logger.Debug("TrySetMoney: " + propertyName + " cents string=" + cents.ToString(CultureInfo.InvariantCulture));
            if (PoslinkReflection.SetProperty(target, propertyName, cents.ToString(CultureInfo.InvariantCulture)))
            {
                Logger.Debug("TrySetMoney: success via string cents");
                return;
            }

            Logger.Debug("TrySetMoney: all set attempts failed for " + propertyName);
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

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }
    }
}