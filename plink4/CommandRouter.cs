using System;
using System.Linq;
using System.Reflection;

namespace plink4
{
    internal static class CommandRouter
    {
        public static int Execute(ArgsModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            // normalize once
            string cardTypeUpper = (model.CardType ?? "").Trim().ToUpperInvariant();
            string txnTypeUpper = (model.TxnType ?? "").Trim().ToUpperInvariant();

            // special commands
            if (cardTypeUpper == "BATCHCLOSE")
                return DoBatchCloseHandler.Run(model);

            if (cardTypeUpper == "LASTTRANSACTION")
                return LastTransactionHandler.Run(model);

            if (txnTypeUpper == "ADJUST")
                return DoCreditAdjustHandler.Run(model);


            // local eWIC test route
            // local eWIC test route
            if (cardTypeUpper == "EWIC")
            {
                Logger.Debug("EWIC route entered");

                try
                {
                    Logger.Debug("EWIC step: connecting to terminal");

                    object ewicTerminal = ConnectTerminal(model);
                    if (ewicTerminal == null)
                        throw new Exception("Terminal connection failed.");

                    Logger.Debug("EWIC terminal connected OK");
                    Logger.Debug("EWIC terminal type = " + ewicTerminal.GetType().FullName);

                    object txnObj = ewicTerminal.GetType().GetProperty("Transaction")?.GetValue(ewicTerminal, null);
                    if (txnObj == null)
                        throw new Exception("terminal.Transaction is null.");

                    Assembly asm = txnObj.GetType().Assembly;

                    Type doEbtReqType = asm.GetType("POSLinkSemiIntegration.Transaction.DoEbtRequest", false);
                    Type doEbtRspType = asm.GetType("POSLinkSemiIntegration.Transaction.DoEbtResponse", false);
                    Type ewicInfoReqType = asm.GetType("POSLinkSemiIntegration.Util.EwicInformationRequest", false);
                    Type ewicDataType = asm.GetType("POSLinkSemiIntegration.Util.EwicData", false);

                    if (doEbtReqType != null && doEbtRspType != null && ewicInfoReqType != null && ewicDataType != null)
                    {
                        object doEbtReq = Activator.CreateInstance(doEbtReqType);
                        object doEbtRsp = Activator.CreateInstance(doEbtRspType);

                        object ewicInfo = Activator.CreateInstance(ewicInfoReqType);
                        object ewicItem = Activator.CreateInstance(ewicDataType);

                        // ---- basket item ----
                        int price = 200;
                        int qty = 1;
                        string basketTotal = (price * qty).ToString();

                        ewicDataType.GetProperty("UpcPluInd")?.SetValue(ewicItem, "0");
                        ewicDataType.GetProperty("UpcPluData")?.SetValue(ewicItem, "7760015721");
                        ewicDataType.GetProperty("UpcPrice")?.SetValue(ewicItem, price.ToString());
                        ewicDataType.GetProperty("UpcQty")?.SetValue(ewicItem, qty.ToString());

                        Array ewicArray = Array.CreateInstance(ewicDataType, 1);
                        ewicArray.SetValue(ewicItem, 0);

                        ewicInfoReqType.GetProperty("EwicData")?.SetValue(ewicInfo, ewicArray);

                        var discountProp = ewicInfoReqType.GetProperty("EwicDiscountAmount");
                        if (discountProp != null)
                            discountProp.SetValue(ewicInfo, "0");

                        doEbtReqType.GetProperty("EwicInformation")?.SetValue(doEbtReq, ewicInfo);

                        // ---- transaction type ----
                        SetEnumIfExists(doEbtReq, "TransactionType", "Sale");

                        // ---- account info (chip read) ----
                        PropertyInfo acctProp = doEbtReqType.GetProperty("AccountInformation");
                        if (acctProp != null && acctProp.PropertyType != null)
                        {
                            object acctObj = Activator.CreateInstance(acctProp.PropertyType);

                            // let terminal show FoodStamp / CashBenefits / eWIC
                            SetEnumIfExists(acctObj, "EbtType", "Ewic");

                            // do NOT set PAN / Exp / CVV so chip reader is used
                            acctProp.SetValue(doEbtReq, acctObj);

                            Logger.Debug("Attached AccountInformation with EbtType=NotSet (chip read)");
                        }

                        // ---- trace / reference ----
                        PropertyInfo traceProp = doEbtReqType.GetProperty("TraceInformation");
                        if (traceProp != null && traceProp.PropertyType != null)
                        {
                            object traceObj = Activator.CreateInstance(traceProp.PropertyType);

                            var refProp =
                                traceProp.PropertyType.GetProperty("ReferenceNumber") ??
                                traceProp.PropertyType.GetProperty("RefNo") ??
                                traceProp.PropertyType.GetProperty("InvoiceNumber") ??
                                traceProp.PropertyType.GetProperty("TransactionNumber");

                            if (refProp != null)
                            {
                                refProp.SetValue(traceObj, model.RefNum);
                                Logger.Debug("TraceInformation " + refProp.Name + " = " + model.RefNum);
                            }

                            traceProp.SetValue(doEbtReq, traceObj);
                        }

                        // ---- amount (basket total) ----
                        PropertyInfo amtProp = doEbtReqType.GetProperty("AmountInformation");
                        if (amtProp != null && amtProp.PropertyType != null)
                        {
                            object amtObj = Activator.CreateInstance(amtProp.PropertyType);

                            var p1 = amtProp.PropertyType.GetProperty("Amount");
                            var p2 = amtProp.PropertyType.GetProperty("TransactionAmount");
                            var p3 = amtProp.PropertyType.GetProperty("PurchaseAmount");

                            if (p1 != null)
                            {
                                p1.SetValue(amtObj, basketTotal);
                                Logger.Debug("AmountInformation.Amount = " + basketTotal);
                            }

                            if (p2 != null)
                            {
                                p2.SetValue(amtObj, basketTotal);
                                Logger.Debug("AmountInformation.TransactionAmount = " + basketTotal);
                            }

                            if (p3 != null)
                            {
                                p3.SetValue(amtObj, basketTotal);
                                Logger.Debug("AmountInformation.PurchaseAmount = " + basketTotal);
                            }

                            amtProp.SetValue(doEbtReq, amtObj);
                        }

                        Logger.Debug("Invoking Transaction.DoEbt");

                        int rc = PoslinkReflection.InvokeTxMethod(
                            txnObj,
                            "DoEbt",
                            doEbtReq,
                            ref doEbtRsp
                        );

                        Logger.Debug("DoEbt returned rc=" + rc);

                        if (doEbtRsp != null)
                            DumpObjectGraph(doEbtRsp, "doEbtRsp", 0, 4);
                    }

                    return 0;
                }
                catch (Exception ex)
                {
                    Logger.Debug("EWIC route error: " + ex);
                    return 1;
                }
            }

            // -------------------------------------------------
            // EBT BALANCE ROUTING
            // -------------------------------------------------
            if ((cardTypeUpper == "EBT_FOOD" || cardTypeUpper == "EBT_FOODSTAMP") &&
                txnTypeUpper == "BALANCE")
            {
                return DoEbtBalanceHandler.Run(model, "F");
            }

            if ((cardTypeUpper == "EBT_CASH" || cardTypeUpper == "EBT_CASHBENEFIT") &&
                txnTypeUpper == "BALANCE")
            {
                return DoEbtBalanceHandler.Run(model, "C");
            }

            // normal terminal-based flows
            object terminal = ConnectTerminal(model);

            object response = null;
            int returnCode = -1;

            // EBT RETURN
            if ((cardTypeUpper == "EBT_CASH" ||
                 cardTypeUpper == "EBT_CASHBENEFIT" ||
                 cardTypeUpper == "EBT_FOOD" ||
                 cardTypeUpper == "EBT_FOODSTAMP") &&
                txnTypeUpper == "RETURN")
            {
                returnCode = DoEbtReturnHandler.Run(terminal, model, out response);

                if (response == null)
                    return returnCode;

                switch (cardTypeUpper)
                {
                    case "CREDIT":
                        returnCode = DoCreditHandler.Run(terminal, model, out response);
                        break;

                    case "DEBIT":
                        returnCode = DoDebitHandler.Run(terminal, model, out response);
                        break;

                    case "EBT_CASH":
                    case "EBT_CASHBENEFIT":
                    case "EBT_FOOD":
                    case "EBT_FOODSTAMP":
                        returnCode = DoEbtHandler.Run(terminal, model, out response);
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported CardType: {model.CardType}");
                }
            }

            LegacyResponseWriter.WriteDump(response);
            LegacyResponseWriter.WriteFromRsp(model.CardType, model.TxnType, returnCode == 0, response);

            return returnCode;
        }

        private static bool SetEnumIfExists(object target, string propName, string enumName)
        {
            if (target == null) return false;

            var p = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite) return false;

            try
            {
                Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                if (!t.IsEnum) return false;

                object enumValue = Enum.Parse(t, enumName, true);
                p.SetValue(target, enumValue, null);
                Logger.Debug("Set " + target.GetType().Name + "." + propName + " = " + enumValue);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Debug("SetEnumIfExists failed for " + propName + ": " + ex.Message);
                return false;
            }
        }



        private static bool SetIfExists(object target, string propName, object value)
        {
            if (target == null) return false;

            var p = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite) return false;

            try
            {
                Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                object converted = value;

                if (value != null && !t.IsAssignableFrom(value.GetType()))
                    converted = Convert.ChangeType(value, t);

                p.SetValue(target, converted, null);
                Logger.Debug("Set " + target.GetType().Name + "." + propName + " = " + converted);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Debug("SetIfExists failed for " + propName + ": " + ex.Message);
                return false;
            }
        }


        private static void DumpTypeMembers(Type t, string label)
        {
            if (t == null)
            {
                Logger.Debug(label + ": <null type>");
                return;
            }

            Logger.Debug("===== TYPE DUMP: " + label + " =====");
            Logger.Debug("FullName = " + t.FullName);

            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Logger.Debug("PROP  " + p.PropertyType.FullName + "  " + p.Name);
            }

            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Logger.Debug("FIELD " + f.FieldType.FullName + "  " + f.Name);
            }

            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var pars = m.GetParameters();
                string sig = "";
                for (int i = 0; i < pars.Length; i++)
                {
                    if (i > 0) sig += ", ";
                    sig += pars[i].ParameterType.Name + " " + pars[i].Name;
                }

                Logger.Debug("METHOD " + m.ReturnType.Name + " " + m.Name + "(" + sig + ")");
            }

            Logger.Debug("===== END TYPE DUMP: " + label + " =====");
        }

        private static void DumpInterestingProps(Type t, string label)
        {
            if (t == null)
            {
                Logger.Debug(label + ": <null type>");
                return;
            }

            Logger.Debug("===== INTERESTING PROPS: " + label + " =====");

            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                string n = p.Name.ToUpperInvariant();
                if (n.Contains("EWIC") || n.Contains("UPC") || n.Contains("PLU") || n.Contains("EBT") || n.Contains("VOUCHER"))
                {
                    Logger.Debug("PROP  " + p.PropertyType.FullName + "  " + p.Name);
                }
            }

            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string n = f.Name.ToUpperInvariant();
                if (n.Contains("EWIC") || n.Contains("UPC") || n.Contains("PLU") || n.Contains("EBT") || n.Contains("VOUCHER"))
                {
                    Logger.Debug("FIELD " + f.FieldType.FullName + "  " + f.Name);
                }
            }

            Logger.Debug("===== END INTERESTING PROPS: " + label + " =====");
        }

        private static void DumpObjectGraph(object obj, string label, int depth = 0, int maxDepth = 2)
        {
            if (obj == null)
            {
                Logger.Debug(label + " = <null>");
                return;
            }

            if (depth > maxDepth)
            {
                Logger.Debug(label + " = <max depth reached>");
                return;
            }

            Type t = obj.GetType();
            Logger.Debug(label + " TYPE = " + t.FullName);

            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0)
                    {
                        Logger.Debug(label + "." + p.Name + " = <not readable>");
                        continue;
                    }

                    object val = p.GetValue(obj, null);

                    if (val == null)
                    {
                        Logger.Debug(label + "." + p.Name + " = null");
                    }
                    else
                    {
                        Type pt = val.GetType();

                        if (pt.IsPrimitive || val is string || val is decimal || val is DateTime || pt.IsEnum)
                        {
                            Logger.Debug(label + "." + p.Name + " = " + val);
                        }
                        else
                        {
                            Logger.Debug(label + "." + p.Name + " -> " + pt.FullName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug(label + "." + p.Name + " = <ERR: " + ex.Message + ">");
                }
            }
        }



    private static void DumpPublicProperties(object obj, string prefix)
        {
            if (obj == null)
            {
                Logger.Debug(prefix + " = null");
                return;
            }

            var props = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var p in props)
            {
                try
                {
                    object val = p.GetValue(obj, null);

                    if (val == null)
                    {
                        Logger.Debug(prefix + p.Name + " = null");
                        continue;
                    }

                    Type t = p.PropertyType;
                    if (t.IsPrimitive || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime))
                    {
                        Logger.Debug(prefix + p.Name + " = " + val);
                    }
                    else
                    {
                        Logger.Debug(prefix + p.Name + " -> " + t.FullName);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug(prefix + p.Name + " ERROR: " + ex.Message);
                }
            }
        }

        internal static object ConnectTerminal(ArgsModel model)
        {
            const string SemiFullName = "POSLinkSemiIntegration.POSLinkSemi, POSLinkSemiIntegration";
            const string TcpSettingType = "POSLinkCore.CommunicationSetting.TcpSetting, POSLinkCore";

            Type semiType = Type.GetType(SemiFullName, throwOnError: false)
                ?? throw new InvalidOperationException("POSLinkSemi type not found.");

            object semi = GetStaticFieldOrSingleton(semiType)
                ?? Activator.CreateInstance(semiType)
                ?? throw new InvalidOperationException("Cannot obtain/create POSLinkSemi instance.");

            Type tcpType = Type.GetType(TcpSettingType, throwOnError: false)
                ?? throw new InvalidOperationException("TcpSetting type not found.");

            object tcp = Activator.CreateInstance(tcpType)
                ?? throw new InvalidOperationException("Cannot create TcpSetting instance.");

            SetPropertyValue(tcp, "Ip", model.Ip);
            SetPropertyValue(tcp, "Port", model.ArgPort);
            SetPropertyValue(tcp, "Timeout", AppConfig.TimeoutMs);

            MethodInfo getTerminalMethod = semiType.GetMethod("GetTerminal", new[] { tcpType })
                ?? throw new InvalidOperationException("GetTerminal(TcpSetting) method not found.");

            object terminal = getTerminalMethod.Invoke(semi, new[] { tcp })
                ?? throw new InvalidOperationException("GetTerminal returned null.");

            return terminal;
        }

        private static object GetStaticFieldOrSingleton(Type type)
        {
            return type.GetField("_poslinkSemi", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                ?? type.GetMethod("GetPOSLinkSemi", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        }

        private static void SetPropertyValue(object target, string name, object value)
        {
            if (value == null) return;

            PropertyInfo prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Property '{name}' not found on {target.GetType().Name}.");

            object converted = value;
            Type targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            if (!targetType.IsAssignableFrom(value.GetType()))
                converted = Convert.ChangeType(value, targetType);

            prop.SetValue(target, converted);
        }
    }
}