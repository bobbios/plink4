using System;

namespace plink4
{
    internal static class DoDebitHandler
    {
        public static int Run(object term, ArgsModel model, out object response)
        {
            response = null;

            if (term == null)
                throw new ArgumentNullException(nameof(term));

            if (model == null)
                throw new ArgumentNullException(nameof(model));

            try
            {
                object transaction = PoslinkReflection.RequireProperty(
                    term,
                    "Transaction",
                    "Transaction property is null on terminal object."
                );

                object request = PoslinkReflection.CreateRequest("DoDebit");

                // Note: CreateResponse is often unnecessary — many implementations populate the ref/out param directly.
                // If InvokeTxMethod works without it, you can remove this line.
                object responseObj = PoslinkReflection.CreateResponse("DoDebit");

                PoslinkRequestBuilder.ApplyTrace(request, model.RefNum);
                PoslinkRequestBuilder.ApplySaleReturnTransactionType(request, model.TxnType);
                PoslinkRequestBuilder.ApplySimpleAmount(request, model.Amount);

                int returnCode = PoslinkReflection.InvokeTxMethod(
                    transaction,
                    "DoDebit",
                    request,
                    ref responseObj
                );

                response = responseObj;

                return returnCode;
            }
            catch (Exception ex)
            {
                Logger.Error($"DoDebit failed: {ex}");
                throw;
            }
        }
    }
}