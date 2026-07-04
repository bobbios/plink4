using System;

namespace plink4
{
    internal static class DoCreditHandler
    {
        public static int Run(object semi, ArgsModel model, out object response)
        {
            response = null;

            // Replace ThrowIfNull with classic null checks
            if (semi == null)
                throw new ArgumentNullException(nameof(semi));

            if (model == null)
                throw new ArgumentNullException(nameof(model));

            try
            {
                object transaction = PoslinkReflection.RequireProperty(
                    semi,
                    "Transaction",
                    "Transaction property is null on semi object."
                );

                object request = PoslinkReflection.CreateRequest("DoCredit");
                // Note: CreateResponse is often unnecessary here – many POSLink implementations
                //       return the response via the out parameter without needing a pre-created object.
                //       You can try removing this line and see if InvokeTxMethod still works.
                object responseObj = PoslinkReflection.CreateResponse("DoCredit");

                PoslinkRequestBuilder.ApplyTrace(request, model.RefNum);
                PoslinkRequestBuilder.ApplyCreditTransactionType(request, model.TxnType);
                PoslinkRequestBuilder.ApplyCreditAmounts(request, model.Amount, model.Surcharge, model.TxnType);

                int returnCode = PoslinkReflection.InvokeTxMethod(
                    transaction,
                    "DoCredit",
                    request,
                    ref responseObj
                );

                response = responseObj;
                return returnCode;
            }
            catch (Exception ex)
            {
                Logger.Error($"DoCredit failed: {ex}");
                throw;
            }
        }
    }
}