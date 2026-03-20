using System;
using System.Globalization;
using System.Reflection;

namespace plink4
{
    internal static class DoCreditAdjustmentHandler
    {
        public static int Run(ArgsModel model)
        {
            try
            {
                if (model == null)
                    throw new Exception("ArgsModel is null.");

                Logger.Info("Starting DoCreditAdjustmentHandler");

                object terminal = CommandRouter.ConnectTerminal(model);
                if (terminal == null)
                    throw new Exception("Terminal connection failed.");

                Type termType = terminal.GetType();

                object transaction = termType.GetProperty("Transaction")?.GetValue(terminal, null);
                if (transaction == null)
                    throw new Exception("terminal.Transaction is null.");

                Type txnType = transaction.GetType();

                Type reqType = txnType.Assembly.GetType("POSLink2.Transaction.DoCreditReq");
                if (reqType == null)
                    throw new Exception("Could not load POSLink2.Transaction.DoCreditReq");

                object req = Activator.CreateInstance(reqType);
                if (req == null)
                    throw new Exception("Could not create DoCreditReq");

                SetProp(req, "EdcType", GetEnumValue(txnType.Assembly, "POSLink2.Const.EdcType", "Credit"));
                SetProp(req, "TransType", GetEnumValue(txnType.Assembly, "POSLink2.Const.TransType", "Adjust"));

                SetProp(req, "RefNum", model.RefNum);
                SetPropIfExists(req, "InvoiceNo", model.RefNum);
                SetPropIfExists(req, "OrigRefNum", model.OriginalRef);
                SetPropIfExists(req, "OriginalRefNum", model.OriginalRef);
                SetPropIfExists(req, "ApprovalCode", model.ApprovalCode);
                SetPropIfExists(req, "AuthCode", model.ApprovalCode);
                SetPropIfExists(req, "TransactionId", model.TransactionId);

                Type amountType =
                    txnType.Assembly.GetType("POSLink2.Transaction.AmountInformation") ??
                    txnType.Assembly.GetType("POSLink2.Transaction.AmountInfo");

                if (amountType == null)
                    throw new Exception("Could not load AmountInformation type.");

                object amountInfo = Activator.CreateInstance(amountType);
                if (amountInfo == null)
                    throw new Exception("Could not create AmountInformation.");

                decimal amt;
                if (!decimal.TryParse(model.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out amt))
                    throw new Exception("Invalid amount: " + model.Amount);

                string finalAmount = amt.ToString("0.00", CultureInfo.InvariantCulture);

                bool amountSet = false;
                if (SetPropIfExists(amountInfo, "TransactionAmount", finalAmount)) amountSet = true;
                if (SetPropIfExists(amountInfo, "Amount", finalAmount)) amountSet = true;
                if (SetPropIfExists(amountInfo, "TotalAmount", finalAmount)) amountSet = true;

                if (!amountSet)
                    throw new Exception("No usable amount property found on AmountInformation.");

                SetProp(req, "AmountInformation", amountInfo);

                Logger.Info("ADJUST Ref=" + model.RefNum +
                            " Amount=" + finalAmount +
                            " OrigRef=" + model.OriginalRef +
                            " TxnId=" + model.TransactionId);

                MethodInfo mi = null;
                foreach (MethodInfo m in txnType.GetMethods())
                {
                    if (m.Name != "DoCredit")
                        continue;

                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 2 && ps[1].IsOut)
                    {
                        mi = m;
                        break;
                    }
                }

                if (mi == null)
                    throw new Exception("DoCredit(req, out rsp) method not found.");

                object[] callArgs = new object[] { req, null };
                object result = mi.Invoke(transaction, callArgs);
                object rsp = callArgs[1];

                int rc = Convert.ToInt32(result ?? -1);
                Logger.Info("DoCredit returned rc=" + rc);

                string resultCode = "";
                string resultTxt = "";
                string authCode = "";
                string refNumRsp = "";

                if (rsp != null)
                {
                    PropertyInfo pi;

                    pi = rsp.GetType().GetProperty("ResultCode");
                    if (pi != null)
                    {
                        object v = pi.GetValue(rsp, null);
                        resultCode = v == null ? "" : Convert.ToString(v);
                    }

                    pi = rsp.GetType().GetProperty("ResultTxt");
                    if (pi != null)
                    {
                        object v = pi.GetValue(rsp, null);
                        resultTxt = v == null ? "" : Convert.ToString(v);
                    }

                    pi = rsp.GetType().GetProperty("AuthCode");
                    if (pi != null)
                    {
                        object v = pi.GetValue(rsp, null);
                        authCode = v == null ? "" : Convert.ToString(v);
                    }

                    pi = rsp.GetType().GetProperty("RefNum");
                    if (pi != null)
                    {
                        object v = pi.GetValue(rsp, null);
                        refNumRsp = v == null ? "" : Convert.ToString(v);
                    }
                }

                bool ok = false;
                if (rc == 0)
                {
                    if (string.Equals(resultCode, "000000", StringComparison.OrdinalIgnoreCase))
                        ok = true;
                    else if (!string.IsNullOrWhiteSpace(resultTxt) &&
                             resultTxt.IndexOf("APPROV", StringComparison.OrdinalIgnoreCase) >= 0)
                        ok = true;
                }

                LegacyResponseWriter.WriteLegacy(
                    model.CardType,
                    model.TxnType,
                    ok,
                    string.IsNullOrWhiteSpace(resultTxt) ? ("ResultCode=" + resultCode) : resultTxt,
                    authCode,
                    string.IsNullOrWhiteSpace(refNumRsp) ? model.RefNum : refNumRsp
                );

                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Logger.Error("DoCreditAdjustmentHandler failed: " + ex);

                LegacyResponseWriter.WriteLegacy(
                    model.CardType,
                    model.TxnType,
                    false,
                    ex.Message,
                    "",
                    model?.RefNum ?? ""
                );

                return 1;
            }
        }

        private static object CreateAmountInformation(Assembly asm, string amountText)
        {
            Type amtType = asm.GetType("POSLink2.Transaction.AmountInformation")
                        ?? asm.GetType("POSLink2.Transaction.AmountInfo");

            if (amtType == null)
                throw new Exception("AmountInformation type not found.");

            object amtObj = Activator.CreateInstance(amtType);
            if (amtObj == null)
                throw new Exception("Could not create AmountInformation object.");

            string normalized = NormalizeAmount(amountText);

            // Try common property names
            if (!SetPropIfExists(amtObj, "Amount", normalized) &&
                !SetPropIfExists(amtObj, "TotalAmount", normalized) &&
                !SetPropIfExists(amtObj, "TransactionAmount", normalized))
            {
                throw new Exception("No usable amount property found on AmountInformation.");
            }

            return amtObj;
        }

        private static string NormalizeAmount(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "0.00";

            input = input.Trim().Replace("$", "").Replace(",", "");

            if (!decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d))
                throw new Exception("Invalid amount: " + input);

            return d.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static void SetProp(object obj, string propName, object value)
        {
            PropertyInfo p = obj.GetType().GetProperty(propName);
            if (p == null)
                throw new Exception($"Property '{propName}' not found on {obj.GetType().FullName}");

            object converted = ConvertValue(value, p.PropertyType);
            p.SetValue(obj, converted, null);
        }

        private static bool SetPropIfExists(object obj, string propName, object value)
        {
            PropertyInfo p = obj.GetType().GetProperty(propName);
            if (p == null)
                return false;

            object converted = ConvertValue(value, p.PropertyType);
            p.SetValue(obj, converted, null);
            return true;
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null)
                return null;

            Type t = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (t.IsEnum)
            {
                if (value.GetType().IsEnum)
                    return value;

                return Enum.Parse(t, value.ToString(), true);
            }

            if (t == typeof(string))
                return value.ToString();

            if (t == typeof(int))
                return Convert.ToInt32(value);

            if (t == typeof(decimal))
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture);

            if (t == typeof(bool))
                return Convert.ToBoolean(value);

            return value;
        }

        private static object GetEnumValue(Assembly asm, string enumTypeName, string enumName)
        {
            Type t = asm.GetType(enumTypeName);
            if (t == null)
                throw new Exception("Enum type not found: " + enumTypeName);

            return Enum.Parse(t, enumName, true);
        }
    }
}