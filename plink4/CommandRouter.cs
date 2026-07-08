using System;
using System.Reflection;

namespace plink4
{
    internal static class CommandRouter
    {
        public static int Execute(ArgsModel model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            try
            {
                // Special / non-credit/debit flows
                if (model.CardType == "BATCHCLOSE")
                    return DoBatchCloseHandler.Run(model);

                if (model.CardType == "LASTTRANSACTION")
                    return LastTransactionHandler.Run(model);

                if (string.Equals(model.TxnType, "ADJUST", StringComparison.OrdinalIgnoreCase))
                    return DoCreditAdjustHandler.Run(model);


                if (model.CardType == "EBT_FOOD" && model.TxnType == "BALANCE")
                {
                    return DoEbtBalanceHandler.Run(model, "F");
                }

                if (model.CardType == "EBT_CASH" && model.TxnType == "BALANCE")
                {
                    return DoEbtBalanceHandler.Run(model, "C");
                }

                if (model.CardType == "LOYALTY" && model.TxnType == "PHONE")
                {
                    return GetPhoneNumberHandler.Run(model);
                }

                // Standard payment flows — the dialog owns connecting to the terminal too,
                // so it appears immediately rather than after a silent, unbounded connect.
                string cardTypeUpper = (model.CardType ?? "").Trim().ToUpperInvariant();

                int returnCode = TransactionUiRunner.RunPaymentFlow(
                    model, cardTypeUpper, out object response, out string errorMessage);

                if (returnCode == TransactionUiRunner.CancelledReturnCode)
                {
                    Logger.Info("Transaction cancelled by operator.");
                    LegacyResponseWriter.WriteLegacy(model.CardType, model.TxnType, false, "CANCELLED", "", "");
                    return returnCode;
                }

                if (returnCode == TransactionUiRunner.ConnectionErrorReturnCode)
                {
                    Logger.Error("Terminal connection error: " + errorMessage);
                    LegacyResponseWriter.WriteLegacy(model.CardType, model.TxnType, false, errorMessage, "", "");
                    return returnCode;
                }

                if (returnCode == TransactionUiRunner.TimeoutReturnCode)
                {
                    Logger.Error("Terminal transaction timed out: " + errorMessage);
                    LegacyResponseWriter.WriteLegacy(model.CardType, model.TxnType, false, errorMessage, "", "");
                    return returnCode;
                }

                LegacyResponseWriter.WriteDump(response);
                LegacyResponseWriter.WriteFromRsp(model.CardType, model.TxnType, returnCode == 0, response);

                if (returnCode != 0)
                {
                    string respCode = PoslinkReflection.GetProperty(response, "ResponseCode")?.ToString() ?? "";
                    string respMsg = PoslinkReflection.GetProperty(response, "ResponseMessage")?.ToString() ?? "";
                    Logger.Error($"Transaction declined: CardType={model.CardType} TxnType={model.TxnType} ResponseCode={respCode} ResponseMessage={respMsg}");
                }

                return returnCode;
            }
            catch (Exception ex)
            {
                Logger.Error($"CommandRouter.Execute failed: {ex}");
                throw;
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

            DisablePosLinkFileLogging(semi, semiType);

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

        // The SDK has its own built-in debug logger (POSLog{date}.txt, raw TCP frame
        // dumps) that defaults to writing wherever the process's current working
        // directory happens to be at launch - inconsistent and not something we need
        // alongside plink4's own Logger. Disable it via the SDK's own LogSetting API.
        private static void DisablePosLinkFileLogging(object semi, Type semiType)
        {
            try
            {
                Type logSettingType = Type.GetType("POSLinkCore.LogSetting, POSLinkCore", throwOnError: false);
                if (logSettingType == null) return;

                object logSetting = Activator.CreateInstance(logSettingType);

                PropertyInfo enabledProp = logSettingType.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Instance);
                enabledProp?.SetValue(logSetting, false);

                MethodInfo setMethod = semiType.GetMethod("SetLogSetting", new[] { logSettingType });
                setMethod?.Invoke(semi, new[] { logSetting });
            }
            catch
            {
                // Best-effort - failing to disable POSLog shouldn't block the transaction.
            }
        }

        // Very minimal singleton/field fallback
        private static object GetStaticFieldOrSingleton(Type type)
        {
            // Try common singleton patterns
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
            {
                converted = Convert.ChangeType(value, targetType);
            }

            prop.SetValue(target, converted);
        }
    }
}