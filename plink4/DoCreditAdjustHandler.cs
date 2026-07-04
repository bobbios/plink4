using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace plink4
{
    internal static class DoCreditAdjustHandler
    {
        public static int Run(ArgsModel model)
        {
            try
            {
                if (model == null) throw new Exception("ArgsModel is null.");
                if (string.IsNullOrWhiteSpace(model.Ip)) throw new Exception("IP is required.");
                if (string.IsNullOrWhiteSpace(model.RefNum)) throw new Exception("RefNum is required.");
                if (string.IsNullOrWhiteSpace(model.TransactionId)) throw new Exception("TransactionId is required for ADJUST.");
                if (string.IsNullOrWhiteSpace(model.ApprovalCode)) throw new Exception("ApprovalCode is required for ADJUST.");

                if (!decimal.TryParse(model.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                    throw new Exception("Invalid amount: " + model.Amount);

                int ret = TransactionUiRunner.RunWithDialog(model, "Adjusting transaction...\nFollow prompts on the terminal.",
                    (object terminal, out object result) =>
                    {
                        object txnObj = Get(terminal, "Transaction");
                        if (txnObj == null) throw new Exception("terminal.Transaction is null.");

                        Assembly asm = txnObj.GetType().Assembly;
                        object req = CreateDoCreditRequest(model, asm);

                        object rspInner;
                        int retInner = InvokeMethodWithOutInt(txnObj, "DoCredit", req, out rspInner);

                        if (rspInner == null) throw new Exception("DoCredit returned null response.");

                        result = rspInner;
                        return retInner;
                    },
                    out object rspObj,
                    out string errorMessage);

                if (ret == TransactionUiRunner.CancelledReturnCode)
                {
                    Logger.Info("Adjust cancelled by operator.");
                    LegacyResponseWriter.WriteLegacy(model.CardType, model.TxnType, false, "CANCELLED", "", model.ApprovalCode ?? "");
                    return ret;
                }

                if (ret == TransactionUiRunner.ConnectionErrorReturnCode || ret == TransactionUiRunner.TimeoutReturnCode)
                {
                    Logger.Error("Adjust terminal connection error: " + errorMessage);
                    LegacyResponseWriter.WriteLegacy(model.CardType, model.TxnType, false, errorMessage ?? "Terminal connection error.", "", model.ApprovalCode ?? "");
                    return ret;
                }

                object rsp = rspObj;

                string responseCode = SafeGet(rsp, "ResponseCode");
                string responseMessage = SafeGet(rsp, "ResponseMessage");
                string resultCode = SafeGet(rsp, "ResultCode");
                string resultTxt = SafeGet(rsp, "ResultTxt");
                string authCode = SafeGet(rsp, "AuthCode");

                bool ok = false;
                if (ret == 0)
                {
                    if (string.Equals(responseCode, "000000", StringComparison.OrdinalIgnoreCase))
                        ok = true;
                    else if (string.Equals(resultCode, "000000", StringComparison.OrdinalIgnoreCase))
                        ok = true;
                    else if (!string.IsNullOrWhiteSpace(responseMessage) &&
                             responseMessage.IndexOf("APPROV", StringComparison.OrdinalIgnoreCase) >= 0)
                        ok = true;
                    else if (!string.IsNullOrWhiteSpace(resultTxt) &&
                             resultTxt.IndexOf("APPROV", StringComparison.OrdinalIgnoreCase) >= 0)
                        ok = true;
                }

                string finalMessage =
                    !string.IsNullOrWhiteSpace(responseMessage) ? responseMessage :
                    !string.IsNullOrWhiteSpace(resultTxt) ? resultTxt :
                    !string.IsNullOrWhiteSpace(responseCode) ? "ResponseCode=" + responseCode :
                    !string.IsNullOrWhiteSpace(resultCode) ? "ResultCode=" + resultCode :
                    "No response text";

                LegacyResponseWriter.WriteLegacy(
                    model.CardType,
                    model.TxnType,
                    ok,
                    finalMessage,
                    !string.IsNullOrWhiteSpace(responseCode) ? responseCode : resultCode,
                    string.IsNullOrWhiteSpace(authCode) ? model.ApprovalCode : authCode,
                    rsp
                );

                if (!ok)
                    Logger.Error($"Adjust declined: ResponseCode={responseCode} ResultCode={resultCode} Message={finalMessage}");

                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Logger.Error($"DoCreditAdjustHandler failed: {ex}");

                LegacyResponseWriter.WriteLegacy(
                    model?.CardType ?? "",
                    model?.TxnType ?? "",
                    false,
                    ex.Message,
                    "",
                    model?.ApprovalCode ?? ""
                );

                return 1;
            }
        }

        private static object CreateDoCreditRequest(ArgsModel model, Assembly asm)
        {
            Type reqType = asm.GetType("POSLinkSemiIntegration.Transaction.DoCreditRequest", true);
            object req = Activator.CreateInstance(reqType);
            if (req == null) throw new Exception("Could not create DoCreditRequest.");

            SetEnumPropertyByName(req, "TransactionType", "Adjust");

            PropertyInfo piAmount = req.GetType().GetProperty("AmountInformation", BindingFlags.Public | BindingFlags.Instance);
            if (piAmount == null) throw new Exception("AmountInformation property not found.");

            object amountInfo = Activator.CreateInstance(piAmount.PropertyType);
            if (amountInfo == null) throw new Exception("Could not create AmountInformation object.");

            if (!SetIfExists(amountInfo, "TransactionAmount", model.Amount))
                throw new Exception("TransactionAmount field not found.");

            piAmount.SetValue(req, amountInfo, null);

            PropertyInfo piTrace = req.GetType().GetProperty("TraceInformation", BindingFlags.Public | BindingFlags.Instance);
            if (piTrace != null)
            {
                object traceInfo = Activator.CreateInstance(piTrace.PropertyType);
                SetIfExists(traceInfo, "OriginalReferenceNumber", model.TransactionId);
                piTrace.SetValue(req, traceInfo, null);
            }

            PropertyInfo piHostInfo = req.GetType().GetProperty("HostInformation", BindingFlags.Public | BindingFlags.Instance);
            if (piHostInfo != null)
            {
                object hostInfo = Activator.CreateInstance(piHostInfo.PropertyType);
                SetIfExists(hostInfo, "AuthorizationCode", model.ApprovalCode);
                piHostInfo.SetValue(req, hostInfo, null);
            }

            return req;
        }

        private static void SetEnumPropertyByName(object obj, string propName, string enumName)
        {
            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null)
                throw new Exception($"Property '{propName}' not found on {obj.GetType().FullName}");

            if (pi.PropertyType.IsEnum)
                pi.SetValue(obj, Enum.Parse(pi.PropertyType, enumName, true), null);
            else if (pi.PropertyType == typeof(string))
                pi.SetValue(obj, enumName, null);
            else
                throw new Exception($"Property '{propName}' is not enum/string.");
        }

        private static string SafeGet(object obj, string propName)
        {
            try
            {
                object v = Get(obj, propName);
                return v == null ? "" : Convert.ToString(v);
            }
            catch
            {
                return "";
            }
        }

        private static object Get(object obj, string propName)
        {
            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null)
                throw new Exception($"Property '{propName}' not found on {obj.GetType().FullName}");

            return pi.GetValue(obj, null);
        }

        private static bool SetIfExists(object obj, string propName, object value)
        {
            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null) return false;

            object converted = value;

            if (value != null)
            {
                Type targetType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;

                if (!targetType.IsInstanceOfType(value))
                {
                    if (targetType.IsEnum && value is string s)
                        converted = Enum.Parse(targetType, s, true);
                    else if (targetType == typeof(string))
                        converted = Convert.ToString(value, CultureInfo.InvariantCulture);
                    else
                        converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                }
            }

            pi.SetValue(obj, converted, null);
            return true;
        }

        private static int InvokeMethodWithOutInt(object target, string methodName, object arg1, out object outArg)
        {
            var methods = target.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == methodName)
                .ToArray();

            foreach (var m in methods)
            {
                var ps = m.GetParameters();
                if (ps.Length == 2 && ps[1].IsOut)
                {
                    object[] args = new object[] { arg1, null };
                    object ret = m.Invoke(target, args);
                    outArg = args[1];

                    if (ret == null) return -1;
                    if (ret is int i) return i;

                    Type retType = ret.GetType();

                    var pi = retType.GetProperty("Code");
                    if (pi != null)
                    {
                        object v = pi.GetValue(ret, null);
                        if (v != null) return Convert.ToInt32(v);
                    }

                    pi = retType.GetProperty("ResultCode");
                    if (pi != null)
                    {
                        object v = pi.GetValue(ret, null);
                        if (v != null) return Convert.ToInt32(v);
                    }

                    pi = retType.GetProperty("Success");
                    if (pi != null)
                    {
                        object v = pi.GetValue(ret, null);
                        if (v is bool b) return b ? 0 : 1;
                    }

                    return 1;
                }
            }

            throw new Exception($"Method '{methodName}(arg, out rsp)' not found on {target.GetType().FullName}");
        }
    }
}