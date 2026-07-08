using System;
using System.IO;
using System.Reflection;

namespace plink4
{
    // Captures a customer phone number via the terminal's own keypad, using the
    // Form.InputText Administration command (POSLinkAdmin.Form.Form.InputText).
    // This is a standalone prompt, not a payment transaction, so it does NOT
    // require Loyalty (or any EDC type) to be enabled on the merchant account —
    // unlike DoLoyalty, which came back "100005 UNSUPPORT EDC" on a terminal
    // whose processor boarding doesn't have Loyalty turned on.
    internal static class GetPhoneNumberHandler
    {
        public static int Run(ArgsModel model)
        {
            Logger.Info("Entered GetPhoneNumberHandler.Run");
            try
            {
                if (model == null)
                    throw new ArgumentNullException(nameof(model));

                int rc = TransactionUiRunner.RunWithDialog(model, "Enter phone number on terminal...\nFollow prompts on the terminal.",
                    (object terminal, out object result) =>
                    {
                        Logger.Info("Terminal type: " + terminal.GetType().FullName);

                        object form = PoslinkReflection.RequireProperty(terminal, "Form", "Form property is null on " + terminal.GetType().FullName);
                        Assembly adminAsm = form.GetType().Assembly;

                        Type reqType = adminAsm.GetType("POSLinkAdmin.Form.InputTextRequest", true);
                        Type rspType = adminAsm.GetType("POSLinkAdmin.Form.InputTextResponse", true);

                        object req = Activator.CreateInstance(reqType);
                        PoslinkReflection.SetProperty(req, "Title", "EnterPhoneNumber");
                        PoslinkReflection.SetProperty(req, "InputType", "PhoneNumber");
                        PoslinkReflection.SetProperty(req, "MaxLength", "10");
                        PoslinkReflection.SetProperty(req, "Timeout", "60");

                        MethodInfo inputText = form.GetType().GetMethod("InputText", new[] { reqType, rspType.MakeByRefType() });
                        if (inputText == null)
                            throw new Exception("InputText method not found on " + form.GetType().FullName);

                        Logger.Debug("Calling Form.InputText...");
                        object[] args = { req, null };
                        inputText.Invoke(form, args);
                        object rsp = args[1];

                        DumpObject(rsp, "InputTextResponse");

                        string respCode = Str(rsp, "ResponseCode");
                        bool ok = string.IsNullOrWhiteSpace(respCode) || respCode == "000000" || respCode == "0" || respCode == "00";

                        result = rsp;
                        return ok ? 0 : 1;
                    },
                    out object rspObj,
                    out string errorMessage);

                if (rc == TransactionUiRunner.CancelledReturnCode)
                {
                    Logger.Info("Phone number entry cancelled by operator.");
                    WriteFile("ResultCode|1\r\nResultTxt|CANCELLED\r\nPhoneNumber|\r\n");
                    return rc;
                }

                if (rc == TransactionUiRunner.ConnectionErrorReturnCode || rc == TransactionUiRunner.TimeoutReturnCode)
                {
                    Logger.Error("Phone number entry terminal connection error: " + errorMessage);
                    WriteError(errorMessage ?? "Terminal connection error.");
                    return rc;
                }

                WriteResponse(rc, rspObj);
                return rc;
            }
            catch (Exception ex)
            {
                Logger.Debug("GetPhoneNumberHandler ERROR: " + ex);
                WriteError(ex.InnerException?.Message ?? ex.Message);
                return 1;
            }
        }

        private static void WriteResponse(int rc, object rsp)
        {
            string phoneNumber = Str(rsp, "Text");
            string responseCode = Str(rsp, "ResponseCode");
            string responseMsg = Str(rsp, "ResponseMessage");

            WriteFile(
                "DateTime|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                "ResultCode|" + (rc == 0 ? "0" : "1") + "\r\n" +
                "ResultTxt|" + (rc == 0 ? "OK" : "ERROR") + "\r\n" +
                "ResponseCode|" + responseCode + "\r\n" +
                "ResponseMessage|" + responseMsg + "\r\n" +
                "PhoneNumber|" + phoneNumber + "\r\n"
            );
        }

        private static void WriteError(string msg)
        {
            WriteFile(
                "ResultCode|1\r\n" +
                "ResultTxt|ERROR\r\n" +
                "ResponseMessage|" + msg + "\r\n" +
                "PhoneNumber|\r\n"
            );
        }

        private static void WriteFile(string text)
        {
            string dir = Path.GetDirectoryName(AppConfig.LoyaltyResponse);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(AppConfig.LoyaltyResponse, text ?? "");
        }

        private static object GetProp(object obj, string name)
        {
            if (obj == null) return null;
            return obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj);
        }

        private static string Str(object obj, string name)
        {
            object v = GetProp(obj, name);
            return v == null ? "" : Convert.ToString(v);
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
