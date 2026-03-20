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
            Logger.Info("Starting DoCreditAdjustHandler");

            try
            {
                if (model == null)
                    throw new Exception("ArgsModel is null.");
                if (string.IsNullOrWhiteSpace(model.Ip))
                    throw new Exception("IP is required.");
                if (string.IsNullOrWhiteSpace(model.RefNum))
                    throw new Exception("RefNum is required.");
                if (string.IsNullOrWhiteSpace(model.OriginalRef))
                    throw new Exception("OriginalRef is required for ADJUST.");

                decimal amt;
                if (!decimal.TryParse(model.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out amt))
                    throw new Exception("Invalid amount: " + model.Amount);

                string finalAmount = (model.Amount ?? "").Trim().Replace("$", "").Replace(",", "");

                Logger.Info(
                    $"ADJUST request: RefNum={model.RefNum}, OrigRef={model.OriginalRef}, ApprovalCode={model.ApprovalCode}, TransactionId={model.TransactionId}, FinalAmount={finalAmount}, Ip={model.Ip}"
                );

                object terminal = CommandRouter.ConnectTerminal(model);
                if (terminal == null)
                    throw new Exception("Terminal connection failed.");

                object txnObj = Get(terminal, "Transaction");
                if (txnObj == null)
                    throw new Exception("terminal.Transaction is null.");

                Assembly asm = txnObj.GetType().Assembly;

                object req = CreateDoCreditRequest(model, asm);

                ApplyAdjustTraceFields(req, model);

                Logger.Info("Calling terminal.Transaction.DoCredit for ADJUST...");

                object rsp;
                int ret = InvokeMethodWithOutInt(txnObj, "DoCredit", req, out rsp);

                Logger.Info("DoCredit(ADJUST) returned ret=" + ret);

                if (rsp == null)
                    throw new Exception("DoCredit returned null response.");

                string responseCode = SafeGet(rsp, "ResponseCode");
                string responseMessage = SafeGet(rsp, "ResponseMessage");

                string resultCode = SafeGet(rsp, "ResultCode");
                string resultTxt = SafeGet(rsp, "ResultTxt");

                string authCode = SafeGet(rsp, "AuthCode");
                string hostCode = SafeGet(rsp, "HostCode");
                string refNumRsp = SafeGet(rsp, "RefNum");
                string cardNum = SafeGet(rsp, "CardNum");
                string approvedAmount = SafeGetNested(rsp, "AmountInformation", "ApprovedAmount");

                Logger.Info(
                    $"ADJUST response: ret={ret}, ResponseCode={responseCode}, ResponseMessage={responseMessage}, ResultCode={resultCode}, ResultTxt={resultTxt}, AuthCode={authCode}, HostCode={hostCode}, RefNum={refNumRsp}, CardNum={cardNum}, ApprovedAmount={approvedAmount}"
                );

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
                    !string.IsNullOrWhiteSpace(responseCode) ? ("ResponseCode=" + responseCode) :
                    !string.IsNullOrWhiteSpace(resultCode) ? ("ResultCode=" + resultCode) :
                    "No response text";

                LegacyResponseWriter.WriteLegacy(
                    model.CardType,
                    model.TxnType,
                    ok,
                    finalMessage,
                    !string.IsNullOrWhiteSpace(responseCode) ? responseCode : resultCode,
                    string.IsNullOrWhiteSpace(authCode) ? (model.OriginalRef ?? "") : authCode,
                    rsp
                );

                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Logger.Error("DoCreditAdjustHandler failed: " + ex);

                LegacyResponseWriter.WriteLegacy(
                    model?.CardType ?? "",
                    model?.TxnType ?? "",
                    false,
                    ex.Message,
                    "",
                    model?.OriginalRef ?? ""
                );

                return 1;
            }
        }

        private static object CreateDoCreditRequest(ArgsModel model, Assembly asm)
        {
            Type reqType = asm.GetType("POSLinkSemiIntegration.Transaction.DoCreditRequest", true);
            object req = Activator.CreateInstance(reqType);
            if (req == null)
                throw new Exception("Could not create DoCreditRequest.");

            Logger.Info("Created request type: " + reqType.FullName);

            // This is an ADJUST against an original SALE
            SetEnumPropertyByName(req, "TransactionType", "Adjust");
            SetEnumPropertyByName(req, "OriginalTransactionType", "Sale");

            string finalAmount = (model.Amount ?? "").Trim().Replace("$", "").Replace(",", "");

            // -------------------------------------------------
            // AmountInformation
            // amount being adjusted
            // -------------------------------------------------
            PropertyInfo piAmount = req.GetType().GetProperty("AmountInformation", BindingFlags.Public | BindingFlags.Instance);
            if (piAmount == null)
                throw new Exception("AmountInformation property not found on DoCreditRequest.");

            object amountInfo = Activator.CreateInstance(piAmount.PropertyType);
            if (amountInfo == null)
                throw new Exception("Could not create AmountInformation object.");

            DumpProperties("AmountInformation", amountInfo);

            bool amountSet = false;
            if (SetIfExists(amountInfo, "TransactionAmount", finalAmount)) amountSet = true;

            if (!amountSet)
                throw new Exception("No usable amount field found on " + piAmount.PropertyType.FullName);

            piAmount.SetValue(req, amountInfo, null);

            // -------------------------------------------------
            // TraceInformation
            // current ref + original ref / original transaction #
            // -------------------------------------------------
            // TraceInformation
            // TraceInformation
            PropertyInfo piTrace = req.GetType().GetProperty("TraceInformation", BindingFlags.Public | BindingFlags.Instance);
            if (piTrace != null)
            {
                object traceInfo = Activator.CreateInstance(piTrace.PropertyType);

                DumpProperties("TraceInformation", traceInfo);

                SetIfExists(traceInfo, "EcrReferenceNumber", model.RefNum);
                SetIfExists(traceInfo, "InvoiceNumber", model.RefNum);

                // original refs
                SetIfExists(traceInfo, "OriginalReferenceNumber", model.RefNum);       // 123456
                SetIfExists(traceInfo, "OriginalEcrReferenceNumber", model.TransactionId); // 002

                SetIfExists(traceInfo, "TimeStamp", DateTime.Now.ToString("yyyyMMddHHmmss"));

                piTrace.SetValue(req, traceInfo, null);
            }

            // -------------------------------------------------
            // Original
            // only mark original txn type for now
            // -------------------------------------------------
            PropertyInfo piOriginal = req.GetType().GetProperty("Original", BindingFlags.Public | BindingFlags.Instance);
            if (piOriginal != null)
            {
                object originalInfo = Activator.CreateInstance(piOriginal.PropertyType);
                DumpProperties("Original", originalInfo);

                SetEnumPropertyByName(originalInfo, "TransactionType", "Sale");

                // leave other Original fields blank until you have real values
                // TransactionDate
                // TransactionTime
                // Pan
                // ExpiryDate
                // Amount

                piOriginal.SetValue(req, originalInfo, null);
            }

            // -------------------------------------------------
            // HostTraceInformation
            // this is the important original transaction identity block
            // -------------------------------------------------
            // HostTraceInformation
            PropertyInfo piHostTrace = req.GetType().GetProperty("HostTraceInformation", BindingFlags.Public | BindingFlags.Instance);
            if (piHostTrace != null)
            {
                object hostTraceInfo = Activator.CreateInstance(piHostTrace.PropertyType);

                DumpProperties("HostTraceInformation", hostTraceInfo);

                SetIfExists(hostTraceInfo, "EcrTransactionId", model.TransactionId);   // 002
                SetIfExists(hostTraceInfo, "OriginalTraceNumber", model.TransactionId); // 002
                SetIfExists(hostTraceInfo, "HostReferenceNumber", model.TransactionId); // 002

                piHostTrace.SetValue(req, hostTraceInfo, null);
            }

            // -------------------------------------------------
            // HostInformation
            // only auth code exists here
            // -------------------------------------------------
            PropertyInfo piHostInfo = req.GetType().GetProperty("HostInformation", BindingFlags.Public | BindingFlags.Instance);
            if (piHostInfo != null)
            {
                object hostInfo = Activator.CreateInstance(piHostInfo.PropertyType);
                DumpProperties("HostInformation", hostInfo);

                if (!string.IsNullOrWhiteSpace(model.ApprovalCode))
                    SetIfExists(hostInfo, "AuthorizationCode", model.ApprovalCode);

                piHostInfo.SetValue(req, hostInfo, null);
            }

            return req;
        }

        private static void DumpProperties(string label, object obj)
        {
            if (obj == null)
            {
                Logger.Info(label + " = null");
                return;
            }

            Type t = obj.GetType();
            Logger.Info("--- " + label + " PROPERTIES: " + t.FullName + " ---");

            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = null;
                string valueText = "";

                try
                {
                    v = p.GetValue(obj, null);
                    valueText = v == null ? "null" : v.ToString();
                }
                catch (Exception ex)
                {
                    valueText = "ERR: " + ex.Message;
                }

                Logger.Info("  " + p.Name + " (" + p.PropertyType.FullName + ") = " + valueText);
            }
        }

        private static void ApplyAdjustTraceFields(object req, ArgsModel model)
        {
            object traceParent = GetIfExists(req, "TraceInformation");
            if (traceParent == null)
            {
                Logger.Info("ApplyAdjustTraceFields: TraceInformation not found");
                return;
            }

            bool anySet = false;

            if (SetIfExists(traceParent, "EcrReferenceNumber", model.RefNum)) anySet = true;
            if (SetIfExists(traceParent, "InvoiceNumber", model.RefNum)) anySet = true;
            if (SetIfExists(traceParent, "OriginalReferenceNumber", model.RefNum)) anySet = true;          // 123456
            if (SetIfExists(traceParent, "OriginalEcrReferenceNumber", model.TransactionId)) anySet = true; // 002
            if (SetIfExists(traceParent, "TimeStamp", DateTime.Now.ToString("yyyyMMddHHmmss"))) anySet = true;

            Logger.Info(anySet
                ? "ApplyAdjustTraceFields: trace fields set"
                : "ApplyAdjustTraceFields: no matching trace fields found");
        }

        private static void SetEnumPropertyByName(object obj, string propName, string enumName)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null)
                throw new Exception($"Property '{propName}' not found on {obj.GetType().FullName}");

            Type propType = pi.PropertyType;
            Logger.Info($"SetEnumPropertyByName: {obj.GetType().FullName}.{propName} type={propType.FullName}");

            object valueToSet;
            if (propType.IsEnum)
                valueToSet = Enum.Parse(propType, enumName, true);
            else if (propType == typeof(string))
                valueToSet = enumName;
            else
                throw new Exception($"Property '{propName}' is not enum/string. Actual type: {propType.FullName}");

            pi.SetValue(obj, valueToSet, null);
            Logger.Info($"Set {obj.GetType().Name}.{propName} = {enumName}");
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

        private static string SafeGetNested(object obj, string parentProp, string childProp)
        {
            try
            {
                object parent = Get(obj, parentProp);
                if (parent == null) return "";
                object child = Get(parent, childProp);
                return child == null ? "" : Convert.ToString(child);
            }
            catch
            {
                return "";
            }
        }

        private static object Get(object obj, string propName)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null)
                throw new Exception($"Property '{propName}' not found on {obj.GetType().FullName}");

            return pi.GetValue(obj, null);
        }

        private static object GetIfExists(object obj, string propName)
        {
            if (obj == null) return null;

            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null)
                return null;

            return pi.GetValue(obj, null);
        }

        private static bool SetIfExists(object obj, string propName, object value)
        {
            if (obj == null) return false;

            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null)
            {
                Logger.Info($"Property '{propName}' not found on {obj.GetType().FullName}, skipping.");
                return false;
            }

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
            Logger.Info($"Set {obj.GetType().Name}.{propName} = {converted}");
            return true;
        }

        private static int InvokeMethodWithOutInt(object target, string methodName, object arg1, out object outArg)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

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

                    DumpProperties("ExecutionResult", ret);
                    DumpProperties("DoCreditResponse", outArg);

                    if (ret == null)
                        return -1;

                    if (ret is int i)
                        return i;

                    Type retType = ret.GetType();
                    Logger.Info("InvokeMethodWithOutInt: return type = " + retType.FullName);

                    PropertyInfo pi;

                    pi = retType.GetProperty("Code");
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

                    pi = retType.GetProperty("Value");
                    if (pi != null)
                    {
                        object v = pi.GetValue(ret, null);
                        if (v != null) return Convert.ToInt32(v);
                    }

                    FieldInfo fi;

                    fi = retType.GetField("Code");
                    if (fi != null)
                    {
                        object v = fi.GetValue(ret);
                        if (v != null) return Convert.ToInt32(v);
                    }

                    fi = retType.GetField("ResultCode");
                    if (fi != null)
                    {
                        object v = fi.GetValue(ret);
                        if (v != null) return Convert.ToInt32(v);
                    }

                    if (retType.IsEnum)
                        return (int)ret;

                    Logger.Info("InvokeMethodWithOutInt: could not convert return object");

                    pi = retType.GetProperty("Success");
                    if (pi != null)
                    {
                        object v = pi.GetValue(ret, null);
                        if (v is bool b)
                            return b ? 0 : 1;
                    }

                    return 1;
                }
            }

            throw new Exception($"Method '{methodName}(arg, out rsp)' not found on {target.GetType().FullName}");
        }
    }
}