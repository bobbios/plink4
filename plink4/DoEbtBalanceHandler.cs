using POSLinkSemiIntegration.Transaction;
using System;
using System.IO;
using System.Reflection;

namespace plink4
{
    /// <summary>
    /// EBT balance inquiry.
    ///
    /// From the live terminal dump we can see the terminal object exposes:
    ///   _transaction  = POSLinkSemiIntegration.Transaction.Transaction
    ///   _communication = POSLinkCommon.Communication
    ///
    /// This handler uses _transaction.DoEbt() — the same call the old handler
    /// attempted, but now we grab _transaction via the private field directly
    /// instead of through a non-existent .Transaction property.
    ///
    /// DoProcess signature (from blob decode) is:
    ///   ExecutionResult DoProcess(List&lt;byte[]&gt;, ref List&lt;byte[]&gt;)
    /// — NOT DoProcess(string) — which is why that approach failed.
    /// </summary>
    internal static class DoEbtBalanceHandler
    {
        public static int Run(ArgsModel model, string ebtType)  // you can even remove string ebtType param if always "F"
        {
            Logger.Info("Entered DoEbtBalanceHandler.Run ebtType=" + ebtType);
            try
            {
                if (model == null)
                    throw new ArgumentNullException(nameof(model));

                int rc = TransactionUiRunner.RunWithDialog(model, "Checking EBT balance...\nFollow prompts on the terminal.",
                    (object terminal, out object result) =>
                    {
                        Logger.Info("Terminal type: " + terminal.GetType().FullName);

                        // 2. Get _transaction
                        object txn = GetPrivateField(terminal, "_transaction");
                        if (txn == null)
                            throw new Exception("_transaction field is null on " + terminal.GetType().FullName);
                        Logger.Info("Transaction type: " + txn.GetType().FullName);

                        // 3. Resolve types
                        Type txnType = txn.GetType();
                        Assembly semiAsm = txnType.Assembly;
                        Type reqType = semiAsm.GetType("POSLinkSemiIntegration.Transaction.DoEbtRequest", true);
                        Type rspType = semiAsm.GetType("POSLinkSemiIntegration.Transaction.DoEbtResponse", true);
                        //      Logger.Debug("ReqType: " + reqType.FullName);
                        //     Logger.Debug("RspType: " + rspType.FullName);

                        // 4. Build the request
                        dynamic req = Activator.CreateInstance(reqType);
                        req.TransactionType = Enum.Parse(req.TransactionType.GetType(), "Inquiry", true);

                        if (req.AccountInformation == null)
                        {
                            req.AccountInformation = Activator.CreateInstance(
                                req.GetType().GetProperty("AccountInformation").PropertyType);
                        }
                        string ebtEnumName =
                            string.Equals(ebtType, "C", StringComparison.OrdinalIgnoreCase)
                                ? "CashBenefits"
                                : "FoodStamp";

                        req.AccountInformation.EbtType = Enum.Parse(
                            req.AccountInformation.EbtType.GetType(), ebtEnumName, true);

                        // Optional: force CardType too (uncomment if still needed)
                        // req.AccountInformation.CardType = Enum.Parse(req.AccountInformation.CardType.GetType(), "EbtFoodStamp", true);

                        // TraceInformation – populate required trace fields safely
                        var traceObj = req.TraceInformation;
                        if (traceObj == null)
                        {
                            var traceProp = req.GetType().GetProperty("TraceInformation", BindingFlags.Public | BindingFlags.Instance);
                            if (traceProp != null && traceProp.CanWrite)
                            {
                                var traceType = traceProp.PropertyType;
                                traceObj = Activator.CreateInstance(traceType);
                                traceProp.SetValue(req, traceObj);
                                Logger.Info("Created TraceInformation instance");
                            }
                        }

                        if (traceObj != null)
                        {
                            string ecrRef = DateTime.Now.ToString("HHmmssfff");  // unique enough

                            // Try common trace property names – add more if needed based on future dumps
                            var possibleTraceProps = new[]
                            {
                                "TraceNumber", "TraceNum", "TraceNo",
                                "TerminalTraceNumber", "TerminalTrace",
                                "ReferenceNumber", "RefNumber",
                                "EcrReferenceNumber", "ECRRefNo",
                                "InvoiceNumber", "InvoiceNo"
                            };

                            bool setAny = false;
                            foreach (var propName in possibleTraceProps)
                            {
                                var p = traceObj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                                if (p != null && p.CanWrite)
                                {
                                    try
                                    {
                                        p.SetValue(traceObj, ecrRef);
                                        Logger.Info($"Set TraceInformation.{propName} = {ecrRef}");
                                        setAny = true;
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Info($"Failed setting {propName}: {ex.Message}");
                                    }
                                }
                            }

                            if (!setAny)
                            {
                                Logger.Info("WARNING: No recognizable trace/reference properties found on TraceRequest");
                            }
                        }
                        else
                        {
                            Logger.Info("TraceInformation could not be created or accessed");
                        }

                        // 5. Call DoEbt
                        MethodInfo doEbt = txnType.GetMethod("DoEbt", new[] { reqType, rspType.MakeByRefType() });
                        if (doEbt == null)
                            throw new Exception("DoEbt method not found on " + txnType.FullName);

                        Logger.Debug("Calling DoEbt...");
                        object[] args = { req, null };
                        doEbt.Invoke(txn, args);
                        dynamic rsp = args[1];

                        DumpObject(rsp.AmountInformation, "AmountInformation");

                        // 6. Evaluate & write
                        int rcInner = IsApproved(rsp) ? 0 : 1;
                        result = rsp;
                        return rcInner;
                    },
                    out object rspObj,
                    out string errorMessage);

                if (rc == TransactionUiRunner.CancelledReturnCode)
                {
                    Logger.Info("EbtBalance cancelled by operator.");
                    WriteFile(
                        "ResultCode: 1\r\n" +
                        "ResultTxt: CANCELLED\r\n" +
                        "ResponseCode: \r\n" +
                        "ResponseMessage: Cancelled by operator.\r\n" +
                        "Balance1: 0.00\r\n" +
                        "Balance2: 0.00\r\n");
                    return rc;
                }

                if (rc == TransactionUiRunner.ConnectionErrorReturnCode || rc == TransactionUiRunner.TimeoutReturnCode)
                {
                    Logger.Error("EbtBalance terminal connection error: " + errorMessage);
                    WriteError(new Exception(errorMessage ?? "Terminal connection error."), ebtType);
                    return rc;
                }

                if (rc != 0)
                {
                    Logger.Error("EbtBalance declined: ResponseCode=" + Str(rspObj, "ResponseCode") +
                        " HostResponseCode=" + Str(rspObj, "HostResponseCode") +
                        " ResponseMessage=" + FirstOf(Str(rspObj, "HostDetailedMessage"), Str(rspObj, "HostResponseMessage"), Str(rspObj, "ResponseMessage")));
                }

                WriteResponse(rc, rspObj, ebtType);
                return rc;
            }
            catch (Exception ex)
            {
                Logger.Debug("DoEbtBalanceHandler ERROR: " + ex.ToString());
                WriteError(ex, ebtType);
                return 1;
            }
        }

        private static void DumpObjectProperties(object obj, string prefix = "")
        {
            if (obj == null)
            {
                Logger.Debug(prefix + " = null");
                return;
            }

            Type t = obj.GetType();

            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    object val = p.GetValue(obj, null);

                    if (val == null)
                    {
                        Logger.Debug($"{prefix}{p.Name} = null");
                        continue;
                    }

                    // primitive / simple types
                    if (p.PropertyType.IsPrimitive ||
                        p.PropertyType == typeof(string) ||
                        p.PropertyType == typeof(decimal))
                    {
                        Logger.Debug($"{prefix}{p.Name} = {val}");
                    }
                    else
                    {
                        Logger.Debug($"{prefix}{p.Name} -> {p.PropertyType.Name}");
                        DumpObjectProperties(val, prefix + p.Name + ".");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"{prefix}{p.Name} ERROR: {ex.Message}");
                }
            }
        }

        // ---------------------------------------------------------------
        // Response evaluation
        // ---------------------------------------------------------------
        private static bool IsApproved(object rsp)
        {
            if (rsp == null) return false;

            // Try IsSuccessful bool
            object isSucc = GetProp(rsp, "IsSuccessful");
            if (isSucc is bool b) return b;

            // Try response codes
            string rc = Str(rsp, "ResponseCode");
            string hrc = Str(rsp, "HostResponseCode");
            return IsOk(rc) || IsOk(hrc);
        }

        private static bool IsOk(string code)
            => !string.IsNullOrWhiteSpace(code)
            && (code == "000000" || code == "00" || code == "0" || code == "000");

        // ---------------------------------------------------------------
        // Write response.txt
        // ---------------------------------------------------------------
        private static void WriteResponse(int rc, object rsp, string ebtType)
        {
            string responseCode = FirstOf(Str(rsp, "HostResponseCode"), Str(rsp, "ResponseCode"));
            string responseMsg = FirstOf(
                Str(rsp, "HostDetailedMessage"),
                Str(rsp, "HostResponseMessage"),
                Str(rsp, "ResponseMessage")
            );

            object amtInfo = GetProp(rsp, "AmountInformation");

            decimal Balance1 = 0m;
            decimal Balance2 = 0m;

            decimal.TryParse(Str(amtInfo, "Balance1"), out Balance1);
            decimal.TryParse(Str(amtInfo, "Balance2"), out Balance2);

            Balance1 *= 0.01m;
            Balance2 *= 0.01m;

            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            WriteFile(
                "DateTime: " + now + "\r\n" +
                "ResultCode: " + (rc == 0 ? "0" : "1") + "\r\n" +
                "ResultTxt: " + (rc == 0 ? "OK" : "ERROR") + "\r\n" +
                "ResponseCode: " + responseCode + "\r\n" +
                "ResponseMessage: " + responseMsg + "\r\n" +
                "Balance1: " + Balance1.ToString("0.00") + "\r\n" +
                "Balance2: " + Balance2.ToString("0.00") + "\r\n"
            );
        }

        private static void WriteError(Exception ex, string ebtType)
        {
            string msg = ex.InnerException?.Message ?? ex.Message;
            WriteFile(
                "ResultCode: 1\r\n" +
                "ResultTxt: ERROR\r\n" +
                "ResponseCode: 1\r\n" +
                "ResponseMessage: " + msg + "\r\n" +
                "FoodstampBalance: \r\n" +
                "CashBalance: \r\n" +
                "RemainingBalance: \r\n" +
                "EbtType: " + ebtType + "\r\n");
        }

        private static void WriteFile(string text)
        {
            string dir = Path.GetDirectoryName(AppConfig.BalanceResponse);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(AppConfig.BalanceResponse, text ?? "");
        }

        // ---------------------------------------------------------------
        // Reflection helpers
        // ---------------------------------------------------------------

        /// <summary>Gets a private field value walking up the hierarchy.</summary>
        private static object GetPrivateField(object obj, string fieldName)
        {
            for (Type t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo f = t.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(obj);
            }
            return null;
        }

        /// <summary>Ensures a child object property exists and is instantiated.</summary>
        private static object EnsureChild(object parent, string propName)
        {
            PropertyInfo p = parent.GetType().GetProperty(propName,
                BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanRead || !p.CanWrite) return null;

            object val = p.GetValue(parent);
            if (val != null) return val;

            Type childType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            val = Activator.CreateInstance(childType);
            p.SetValue(parent, val);
            return val;
        }

        /// <summary>Tries to set an enum property by value name; logs all valid values on failure.</summary>
        private static void TrySetEnum(object obj, string propName, params string[] valueNames)
        {
            PropertyInfo p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite) { Logger.Debug($"TrySetEnum: {propName} not found"); return; }
            Type et = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (!et.IsEnum)
            {
                Logger.Debug($"TrySetEnum: {propName} is not an enum ({et.Name})");
                return;
            }
            foreach (string valueName in valueNames)
            {
                try
                {
                    object val = Enum.Parse(et, valueName, true);
                    p.SetValue(obj, val);
                    Logger.Debug($"TrySetEnum: {propName} = {valueName}");
                    return;
                }
                catch { }
            }
            // Log all valid enum values to help diagnose
            Logger.Debug($"TrySetEnum: {propName} failed all candidates. Valid values: "
                + string.Join(", ", Enum.GetNames(et)));
        }

        /// <summary>Sets an enum property by name (case-insensitive).</summary>
        private static void SetEnum(object obj, string propName, string valueName)
        {
            PropertyInfo p = obj.GetType().GetProperty(propName,
                BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Property '{propName}' not found on {obj.GetType().Name}");
            Type et = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (!et.IsEnum)
                throw new InvalidOperationException($"{propName} is not an enum (type={et.Name})");
            p.SetValue(obj, Enum.Parse(et, valueName, true));
            Logger.Debug($"Set {propName} = {valueName}");
        }

        /// <summary>Tries to set a string or convertible property; silent on failure.</summary>
        private static void TrySet(object obj, string propName, object value)
        {
            try
            {
                PropertyInfo p = obj.GetType().GetProperty(propName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite) return;
                Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                object v = (value != null && !t.IsAssignableFrom(value.GetType()))
                    ? Convert.ChangeType(value, t) : value;
                p.SetValue(obj, v);
            }
            catch (Exception ex) { Logger.Debug($"TrySet {propName}: {ex.Message}"); }
        }

        private static object GetProp(object obj, string name)
        {
            if (obj == null) return null;
            return obj.GetType()
                .GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(obj);
        }

        private static string Str(object obj, string name)
        {
            object v = GetProp(obj, name);
            return v == null ? "" : Convert.ToString(v);
        }

        private static string FirstOf(params string[] vals)
        {
            foreach (string v in vals)
                if (!string.IsNullOrWhiteSpace(v)) return v;
            return "";
        }


        private static decimal ParseCents(object obj, string propName)
        {
            string raw = Str(obj, propName);

            if (string.IsNullOrWhiteSpace(raw))
                return 0m;

            decimal val;
            if (decimal.TryParse(raw, out val))
                return val * 0.01m;

            Logger.Info($"ParseCents failed for {propName}. Raw value = [{raw}]");
            return 0m;



        }
        private static void DumpObject(object obj, string label)
        {
            if (obj == null) { Logger.Debug(label + " = null"); return; }
            Logger.Debug(label + " type=" + obj.GetType().FullName);
            foreach (PropertyInfo p in obj.GetType().GetProperties())
            {
                try { Logger.Debug($"  {label}.{p.Name} = {p.GetValue(obj) ?? "(null)"}"); }
                catch (Exception ex) { Logger.Debug($"  {label}.{p.Name} err={ex.Message}"); }
            }
        }
    }
}