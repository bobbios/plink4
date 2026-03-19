using System;
using System.Reflection;

namespace plink4
{
    internal static class CommandRouter
    {
        public static int Execute(ArgsModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            Logger.Info($"ref={model.RefNum} amt={model.Amount} surcharge={model.Surcharge} cardType={model.CardType} type={model.TxnType} ip={model.Ip} tcpFlag={model.TcpFlag} argPort={model.ArgPort} origRef={model.OriginalRef} preTip={model.PreTipFlag}");

            if (model.CardType == "BATCHCLOSE")
            {
                Logger.Info("ROUTE CHECK: calling DoBatchCloseHandler");
                return DoBatchCloseHandler.Run(model);
            }

            if (model.CardType == "LASTTRANSACTION")
            {
                Logger.Info("ROUTE CHECK: calling LastTransactionHandler");
                return LastTransactionHandler.Run(model);
            }

            if (string.Equals(model.TxnType, "ADJUST", StringComparison.OrdinalIgnoreCase))
            {
                LegacyResponseWriter.WriteLegacy(model.CardType, model.TxnType, ok: false,
                    responseMessage: "ADJUST not implemented yet", responseCode: "NA", authCode: "");
                return 9;
            }

            var term = ConnectTerminal(model);

            object rspObj;
            int rc;

            switch (model.CardType)
            {
                case "CREDIT":
                    Logger.Info("ROUTE CHECK: calling DoCreditHandler");
                    rc = DoCreditHandler.Run(term, model, out rspObj);
                    break;
                case "DEBIT":
                    Logger.Info("ROUTE CHECK: calling DoDebitHandler");
                    rc = DoDebitHandler.Run(term, model, out rspObj);
                    break;
                case "EBT_CASHBENEFIT":
                case "EBT_CASH":
                    Logger.Info("ROUTE CHECK: calling DoEbtHandler");
                    rc = DoEbtHandler.Run(term, model, out rspObj);
                    break;

                case "EBT_FOODSTAMP":
                case "EBT_FOOD":
                    Logger.Info("ROUTE CHECK: calling DoEbtHandler");
                    rc = DoEbtHandler.Run(term, model, out rspObj);
                    break;
                default:
                    throw new Exception("Unsupported CardType: " + model.CardType);
            }

            LegacyResponseWriter.WriteDump(rspObj);
            LegacyResponseWriter.WriteFromRsp(model.CardType, model.TxnType, rc == 0, rspObj);

            Logger.Info("Done. rc=" + rc);
            return rc;
        }

        /// <summary>
        /// Builds a CommunicationSetting, passes it to POSLinkSemi.GetTerminal(setting),
        /// and returns the Terminal object which has Transaction / Report / Batch on it.
        /// </summary>
        internal static object ConnectTerminal(ArgsModel model)
        {
            Logger.Info($"ConnectTerminal: {model.Ip}:{model.ArgPort}");

            // 1. Find POSLinkSemi exact type
            var semiType = Type.GetType(
                "POSLinkSemiIntegration.POSLinkSemi, POSLinkSemiIntegration",
                throwOnError: false);

            if (semiType == null)
                throw new Exception("POSLinkSemi type not found.");

            // 2. Get singleton / instance
            object semi =
                GetStaticFieldValue(semiType, "_poslinkSemi") ??
                TryStaticFactory(semiType, "GetPOSLinkSemi") ??
                Activator.CreateInstance(semiType);

            if (semi == null)
                throw new Exception("Could not create/get POSLinkSemi instance.");

            Logger.Info("ConnectTerminal: semi=" + semi.GetType().FullName);

            // 3. Force TCP setting exact type
            var commType = Type.GetType(
                "POSLinkCore.CommunicationSetting.TcpSetting, POSLinkCore",
                throwOnError: false);

            if (commType == null)
                throw new Exception("TcpSetting type not found.");

            Logger.Info("ConnectTerminal: commType=" + commType.FullName);

            var comm = Activator.CreateInstance(commType);
            if (comm == null)
                throw new Exception("Could not create TcpSetting.");

            Logger.Info("--- TcpSetting PROPERTIES ---");
            foreach (var pi in commType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Logger.Info("  " + pi.Name + " (" + pi.PropertyType.Name + ")");

            // 4. Set required TCP fields
            SetRequiredProperty(comm, "Ip", model.Ip);
            SetRequiredProperty(comm, "Port", model.ArgPort);
            SetRequiredProperty(comm, "Timeout", AppConfig.TimeoutMs);

            Logger.Info($"ConnectTerminal: comm set ip={model.Ip} port={model.ArgPort} timeout={AppConfig.TimeoutMs}");

            // 5. Find GetTerminal method
            var getTerminal = semiType.GetMethod(
                "GetTerminal",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { commType },
                null);

            if (getTerminal == null)
            {
                foreach (var m in semiType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "GetTerminal") continue;

                    var ps = m.GetParameters();
                    if (ps.Length == 1)
                    {
                        getTerminal = m;
                        Logger.Info("ConnectTerminal: fallback GetTerminal overload = " + ps[0].ParameterType.FullName);
                        break;
                    }
                }
            }

            if (getTerminal == null)
                throw new Exception("GetTerminal(TcpSetting) method not found on " + semiType.FullName);

            Logger.Info("ConnectTerminal: calling GetTerminal(TcpSetting)");
            var term = getTerminal.Invoke(semi, new[] { comm });

            if (term == null)
                throw new Exception("GetTerminal returned null.");

            Logger.Info("ConnectTerminal: terminal type=" + term.GetType().FullName);

            Logger.Info("--- Terminal PROPERTIES ---");
            foreach (var pi in term.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    var v = pi.GetValue(term);
                    Logger.Info("  term." + pi.Name + " = " + (v == null ? "(null)" : v.GetType().FullName));
                }
                catch
                {
                    Logger.Info("  term." + pi.Name + " = (error)");
                }
            }

            return term;
        }

        private static void SetRequiredProperty(object obj, string propName, object value)
        {
            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null)
                throw new Exception($"Property '{propName}' not found on {obj.GetType().FullName}");

            object finalValue = value;

            if (value != null)
            {
                var targetType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;

                if (!targetType.IsAssignableFrom(value.GetType()))
                    finalValue = Convert.ChangeType(value, targetType);
            }

            pi.SetValue(obj, finalValue, null);
            Logger.Info($"SetRequiredProperty: {obj.GetType().Name}.{propName} = {finalValue}");
        }



        private static object GetStaticFieldValue(Type type, string fieldName)
        {
            try
            {
                var fi = type.GetField(fieldName,
                    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                return fi?.GetValue(null);
            }
            catch { return null; }
        }

        private static object TryStaticFactory(Type type, string methodName)
        {
            try
            {
                var m = type.GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                return m?.Invoke(null, null);
            }
            catch { return null; }
        }
    }
}