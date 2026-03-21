using System;
using System.Reflection;

namespace plink4
{
    internal static class DoCreditHandler
    {
        private static void DumpAll(object obj, string prefix)
        {
            if (obj == null)
            {
                Logger.Debug(prefix + " = null");
                return;
            }

            var t = obj.GetType();

            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object val = null;

                try
                {
                    if (p.GetIndexParameters().Length == 0)
                        val = p.GetValue(obj, null);
                }
                catch { }

                string typeName = p.PropertyType != null ? p.PropertyType.Name : "(null)";
                string valText = val == null ? "(null)" : val.ToString();

                Logger.Debug(prefix + "." + p.Name + " Type: " + typeName + " Value: " + valText);
            }
        }
        public static int Run(object semi, ArgsModel model, out object response)
        {
            response = null;

            if (semi == null)
                throw new ArgumentNullException(nameof(semi));

            if (model == null)
                throw new ArgumentNullException(nameof(model));

            try
            {
                object transaction = PoslinkReflection.RequireProperty(
                    semi,
                    "Transaction",
                    "Transaction property is null on semi object."
                );

                object request = PoslinkReflection.CreateRequest("DoCredit");
                object responseObj = PoslinkReflection.CreateResponse("DoCredit");

                PoslinkRequestBuilder.ApplyTrace(request, model.RefNum);
                PoslinkRequestBuilder.ApplyCreditTransactionType(request, model.TxnType);
                PoslinkRequestBuilder.ApplyCreditAmounts(request, model.Amount, model.Surcharge, model.TxnType);


                Logger.Debug("model.Surcharge=[" + model.Surcharge + "]");
                Logger.Debug("model.PreTipFlag=[" + model.PreTipFlag + "]");
                Logger.Debug("model.ApprovalCode=[" + model.ApprovalCode + "]");


                // -----------------------------------
                // TIP ON TERMINAL (FROM PRETIP FLAG)
                // -----------------------------------

                var tbProp = request.GetType().GetProperty("TransactionBehavior");
                object tb = null;

                if (tbProp != null)
                {
                    tb = tbProp.GetValue(request, null);

                    if (tb == null)
                    {
                        tb = Activator.CreateInstance(tbProp.PropertyType);
                        tbProp.SetValue(request, tb, null);
                        Logger.Debug("TransactionBehavior object created");
                    }

                    var tipProp = tb.GetType().GetProperty("TipRequestFlag");

                    if (tipProp != null && tipProp.PropertyType.IsEnum)
                    {
                        var enumType = tipProp.PropertyType;

                        string tipEnumName =
                            string.Equals(model.PreTipFlag, "Y", StringComparison.OrdinalIgnoreCase)
                                ? "NeedEnterTipOnTerminal"
                                : "NotNeedEnterTipOnTerminal";

                        object enumValue = Enum.Parse(enumType, tipEnumName, true);

                        tipProp.SetValue(tb, enumValue, null);

                        Logger.Debug("Set TransactionBehavior.TipRequestFlag = " + tipEnumName);
                    }
                    else
                    {
                        Logger.Debug("TipRequestFlag property not found on TransactionBehavior");
                    }
                }
                else
                {
                    Logger.Debug("TransactionBehavior property not found on request");
                }

                // Optional debug dump
                DumpAll(tb, "DoCredit.TransactionBehavior");

                int returnCode = PoslinkReflection.InvokeTxMethod(
                    transaction,
                    "DoCredit",
                    request,
                    ref responseObj
                );

                response = responseObj;
                return returnCode;
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }
}