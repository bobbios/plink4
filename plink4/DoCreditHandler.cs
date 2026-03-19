using System;

namespace plink4
{
    internal static class DoCreditHandler
    {
        public static int Run(object semi, ArgsModel a, out object rsp)
        {
            rsp = null;

            Logger.Info("DoCreditHandler.Run ENTER");
            Logger.Info($"DoCreditHandler.Run ref={a?.RefNum} amt={a?.Amount} txnType={a?.TxnType}");

            if (semi == null) throw new Exception("semi is null.");
            if (a == null) throw new Exception("ArgsModel is null.");

            // Transaction lives directly on the semi object
            var tx = PoslinkReflection.RequireProperty(semi, "Transaction", "POSLinkSemiIntegration.Transaction is null.");

            var req = PoslinkReflection.CreateRequest("DoCredit");
            rsp = PoslinkReflection.CreateResponse("DoCredit");

            Logger.Info("DoCreditHandler: req=" + req.GetType().FullName);

            PoslinkRequestBuilder.ApplyTrace(req, a.RefNum);
            PoslinkRequestBuilder.ApplyCreditTransactionType(req, a.TxnType);
            PoslinkRequestBuilder.ApplyCreditAmounts(req, a.Amount, a.Surcharge, a.TxnType);

            Logger.Info("DoCreditHandler: invoking DoCredit");

            int rc = PoslinkReflection.InvokeTxMethod(tx, "DoCredit", req, ref rsp);

            Logger.Info("DoCreditHandler: done rc=" + rc);
            return rc;
        }
    }
}