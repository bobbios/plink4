using System;
using System.Reflection;

namespace plink4
{
    internal static class DoDebitHandler
    {
        public static int Run(object term, ArgsModel a, out object rsp)
        {
            rsp = null;

            Logger.Info("DoDebitHandler.Run ENTER");
            Logger.Info($"DoDebitHandler.Run ref={a?.RefNum} amt={a?.Amount} txnType={a?.TxnType}");

            if (term == null) throw new Exception("term is null.");
            if (a == null) throw new Exception("ArgsModel is null.");

            var tx = PoslinkReflection.RequireProperty(
                term,
                "Transaction",
                "POSLinkSemiIntegration.Transaction is null.");

            var req = PoslinkReflection.CreateRequest("DoDebit");
            rsp = PoslinkReflection.CreateResponse("DoDebit");

            Logger.Info("DoDebitHandler: req=" + req.GetType().FullName);

            PoslinkRequestBuilder.ApplyTrace(req, a.RefNum);
            PoslinkRequestBuilder.ApplySaleReturnTransactionType(req, a.TxnType);
            PoslinkRequestBuilder.ApplySimpleAmount(req, a.Amount);

            Logger.Info("--- DoDebit REQUEST PROPERTIES ---");
            foreach (var pi in req.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    var val = pi.GetValue(req);
                    Logger.Info("  req." + pi.Name + " = " + (val == null ? "(null)" : val.ToString()));
                }
                catch (Exception ex)
                {
                    Logger.Info("  req." + pi.Name + " = (error: " + ex.Message + ")");
                }
            }

            // Dump important nested request objects
            DumpObject("req.AmountInformation", GetProp(req, "AmountInformation"));
            DumpObject("req.AccountInformation", GetProp(req, "AccountInformation"));
            DumpObject("req.TraceInformation", GetProp(req, "TraceInformation"));
            DumpObject("req.TransactionBehavior", GetProp(req, "TransactionBehavior"));
            DumpObject("req.HostInformation", GetProp(req, "HostInformation"));

            Logger.Info("DoDebitHandler: invoking DoDebit");

            int rc = PoslinkReflection.InvokeTxMethod(tx, "DoDebit", req, ref rsp);

            Logger.Info("DoDebitHandler: done rc=" + rc);
            Logger.Info("DoDebit rsp type=" + (rsp == null ? "(null)" : rsp.GetType().FullName));

            if (rsp != null)
            {
                Logger.Info("--- DoDebit RESPONSE PROPERTIES ---");
                foreach (var pi in rsp.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        var val = pi.GetValue(rsp);
                        Logger.Info("  rsp." + pi.Name + " = " + (val == null ? "(null)" : val.ToString()));
                    }
                    catch (Exception ex)
                    {
                        Logger.Info("  rsp." + pi.Name + " = (error: " + ex.Message + ")");
                    }
                }

                // Dump important nested response objects
                DumpObject("rsp.HostInformation", GetProp(rsp, "HostInformation"));
                DumpObject("rsp.AmountInformation", GetProp(rsp, "AmountInformation"));
                DumpObject("rsp.AccountInformation", GetProp(rsp, "AccountInformation"));
                DumpObject("rsp.TraceInformation", GetProp(rsp, "TraceInformation"));
                DumpObject("rsp.PaymentTransactionInformation", GetProp(rsp, "PaymentTransactionInformation"));
                DumpObject("rsp.CardInformation", GetProp(rsp, "CardInformation"));
                DumpObject("rsp.TransactionBehavior", GetProp(rsp, "TransactionBehavior"));
                DumpObject("rsp.HostTraceInformation", GetProp(rsp, "HostTraceInformation"));
                DumpObject("rsp.TorInformation", GetProp(rsp, "TorInformation"));
            }

            return rc;
        }

        private static object GetProp(object obj, string prop)
        {
            if (obj == null) return null;

            var pi = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
            return pi?.GetValue(obj);
        }

        private static void DumpObject(string label, object obj)
        {
            if (obj == null)
            {
                Logger.Info(label + " = (null)");
                return;
            }

            Logger.Info(label + " type=" + obj.GetType().FullName);

            foreach (var pi in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    var val = pi.GetValue(obj);
                    Logger.Info("  " + label + "." + pi.Name + " = " + (val == null ? "(null)" : val.ToString()));
                }
                catch (Exception ex)
                {
                    Logger.Info("  " + label + "." + pi.Name + " = (error: " + ex.Message + ")");
                }
            }
        }
    }
}