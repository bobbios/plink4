using System;
using System.IO;
using System.Reflection;

namespace plink4
{
    internal static class DoEbtBalanceHandler
    {
        public static int Run(ArgsModel model, string ebtType)
        {
            try
            {
                if (model == null)
                    throw new ArgumentNullException(nameof(model));

                object terminal = CommandRouter.ConnectTerminal(model);
                if (terminal == null)
                    throw new Exception("Terminal connection failed.");

                object txnObj = terminal.GetType()
                    .GetProperty("Transaction", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(terminal, null);

                if (txnObj == null)
                    throw new Exception("Terminal.Transaction is null.");

                Type reqType = Type.GetType(
                    "POSLinkSemiIntegration.Transaction.DoEbtRequest, POSLinkSemiIntegration",
                    throwOnError: true
                );

                Type rspType = Type.GetType(
                    "POSLinkSemiIntegration.Transaction.DoEbtResponse, POSLinkSemiIntegration",
                    throwOnError: true
                );

                object req = Activator.CreateInstance(reqType);

                SetIfExists(req, "TransType", "BALANCE_INQUIRY");
                SetIfExists(req, "EbtType", ebtType);

                MethodInfo doEbtMethod = txnObj.GetType().GetMethod(
                    "DoEbt",
                    new[] { reqType, rspType.MakeByRefType() }
                );

                if (doEbtMethod == null)
                    throw new Exception("DoEbt method not found.");

                object[] parms = new object[] { req, null };
                int rc = (int)doEbtMethod.Invoke(txnObj, parms);

                object rsp = parms[1];

                WriteEbtBalanceResponse(rc, rsp, ebtType);
                return rc;
            }
            catch (Exception ex)
            {
                WriteEbtBalanceErrorResponse(ex, ebtType);
                return 1;
            }
        }

        private static void WriteEbtBalanceResponse(int rc, object rsp, string ebtType)
        {
            string responseCode = GetString(rsp, "ResponseCode");
            string responseMessage = GetString(rsp, "ResponseMessage");

            bool ok = rc == 0 &&
                      (string.IsNullOrWhiteSpace(responseCode) ||
                       responseCode == "000000" ||
                       responseCode == "0");

            string resultCode = ok ? "0" : FirstNonEmpty(responseCode, rc.ToString());
            string resultTxt = ok ? "OK" : FirstNonEmpty(responseMessage, "ERROR");

            string foodBalance = GetString(rsp, "FoodstampBalance");
            string cashBalance = GetString(rsp, "CashBalance");
            string remainingBalance = GetString(rsp, "RemainingBalance");

            string timeStamp = FirstNonEmpty(
                GetString(rsp, "LocalDateTime"),
                GetString(rsp, "TimeStamp")
            );

            string tid = FirstNonEmpty(
                GetString(rsp, "TerminalId"),
                GetString(rsp, "Tid")
            );

            string mid = GetString(rsp, "Mid");

            string selectedBalance = !string.IsNullOrWhiteSpace(remainingBalance)
                ? remainingBalance
                : (string.Equals(ebtType, "F", StringComparison.OrdinalIgnoreCase)
                    ? foodBalance
                    : cashBalance);

            string text =
                "ResultCode: " + resultCode + "\r\n" +
                "ResultTxt: " + resultTxt + "\r\n" +
                "ResponseCode: " + responseCode + "\r\n" +
                "ResponseMessage: " + responseMessage + "\r\n" +
                "FoodstampBalance: " + foodBalance + "\r\n" +
                "CashBalance: " + cashBalance + "\r\n" +
                "RemainingBalance: " + selectedBalance + "\r\n" +
                "TimeStamp: " + timeStamp + "\r\n" +
                "Tid: " + tid + "\r\n" +
                "Mid: " + mid + "\r\n";

            WriteResponseFile(text);
        }

        private static void WriteEbtBalanceErrorResponse(Exception ex, string ebtType)
        {
            string msg = ex.InnerException?.Message ?? ex.Message;

            string text =
                "ResultCode: 1\r\n" +
                "ResultTxt: ERROR\r\n" +
                "ResponseCode: 1\r\n" +
                "ResponseMessage: " + msg + "\r\n" +
                "FoodstampBalance: \r\n" +
                "CashBalance: \r\n" +
                "RemainingBalance: \r\n" +
                "EbtType: " + ebtType + "\r\n";

            WriteResponseFile(text);
        }

        private static void WriteResponseFile(string text)
        {
            var dir = Path.GetDirectoryName(AppConfig.OutResponse);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(AppConfig.OutResponse, text ?? "");
        }

        private static object GetProp(object obj, string name)
        {
            if (obj == null) return null;

            var p = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return p?.GetValue(obj, null);
        }

        private static string GetString(object obj, string name)
        {
            var val = GetProp(obj, name);
            return val == null ? "" : Convert.ToString(val);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return "";

            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }

            return "";
        }

        private static void SetIfExists(object obj, string propName, object value)
        {
            if (obj == null) return;

            PropertyInfo p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite) return;

            object converted = value;
            Type targetType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

            if (value != null && !targetType.IsAssignableFrom(value.GetType()))
                converted = Convert.ChangeType(value, targetType);

            p.SetValue(obj, converted, null);
        }
    }
}