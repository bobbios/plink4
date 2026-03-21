using System;
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
            int returnCode;

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

            LegacyResponseWriter.WriteDump(response);
            LegacyResponseWriter.WriteFromRsp(model.CardType, model.TxnType, returnCode == 0, response);

            return returnCode;
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