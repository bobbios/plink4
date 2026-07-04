using System;
using System.Threading;

namespace plink4
{
    internal static class TransactionUiRunner
    {
        public const int CancelledReturnCode = 2;
        public const int ConnectionErrorReturnCode = 3;
        public const int TimeoutReturnCode = 4;

        public static int RunPaymentFlow(ArgsModel model, string cardTypeUpper, out object response, out string errorMessage)
        {
            object localResponse = null;
            string localErrorMessage = null;
            int localReturnCode = 1;
            Exception error = null;
            object terminalRef = null;

            string connectingMessage = $"Connecting to terminal {model.Ip}:{model.ArgPort}...";
            var form = new TransactionProgressForm(connectingMessage, AppConfig.TimeoutMs);

            form.CancelClicked += (s, e) =>
            {
                if (terminalRef == null)
                {
                    Logger.Info("Operator cancelled before terminal connected; nothing to send.");
                    return;
                }

                Logger.Info("Operator cancelled transaction; sending Cancel to terminal.");
                bool sent = PoslinkReflection.TryCancelTerminal(terminalRef);
                Logger.Info(sent ? "Cancel sent to terminal." : "Cancel could not be sent to terminal (method not found or threw).");
            };

            var worker = new Thread(() =>
            {
                try
                {
                    int port;
                    if (!int.TryParse(model.ArgPort, out port))
                        port = AppConfig.PortAlways;

                    if (!TerminalConnectivity.IsReachable(model.Ip, port, AppConfig.ConnectCheckTimeoutMs))
                    {
                        localErrorMessage = $"Cannot reach terminal at {model.Ip}:{port}.\n\nCheck that the terminal is powered on,\nconnected to the network, and that the\nIP address is correct.";
                        Logger.Error(localErrorMessage);
                        form.ShowError(localErrorMessage);
                        return;
                    }

                    object terminal;
                    try
                    {
                        terminal = CommandRouter.ConnectTerminal(model);
                    }
                    catch (Exception ex)
                    {
                        localErrorMessage = $"Cannot reach terminal at {model.Ip}:{model.ArgPort}.\n\n{ex.Message}";
                        Logger.Error("ConnectTerminal failed: " + ex);
                        form.ShowError(localErrorMessage);
                        return;
                    }

                    terminalRef = terminal;
                    form.UpdateStatus($"Processing {cardTypeUpper} {model.TxnType}...\nFollow prompts on the terminal.");

                    localReturnCode = DispatchTransaction(cardTypeUpper, terminal, model, out localResponse);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    form.CompleteAndClose();
                }
            });
            worker.IsBackground = true;
            worker.Name = "PlinkTransactionWorker";

            // Don't start the worker until the form's handle actually exists —
            // otherwise a fast-completing/failing action can close the dialog
            // before ShowDialog() has shown anything, and it never appears.
            form.Shown += (s, e) => worker.Start();

            var dialogResult = form.ShowDialog();

            if (form.CancelRequested)
            {
                // Give the in-flight SDK call a bounded grace period to unwind after
                // Cancel() was sent, so the terminal session closes cleanly instead of
                // being killed mid-flight when this process exits.
                if (!worker.Join(AppConfig.CancelGraceMs))
                    Logger.Info($"Worker did not finish within {AppConfig.CancelGraceMs}ms of cancel; exiting anyway.");

                response = null;
                errorMessage = null;
                return CancelledReturnCode;
            }

            if (form.TimedOut)
            {
                Logger.Info($"Transaction dialog timed out after {AppConfig.TimeoutMs}ms; sending Cancel to terminal.");
                PoslinkReflection.TryCancelTerminal(terminalRef);
                response = null;
                errorMessage = localErrorMessage ?? "Transaction timed out with no response from the terminal.";
                return TimeoutReturnCode;
            }

            if (dialogResult == System.Windows.Forms.DialogResult.Abort)
            {
                response = null;
                errorMessage = localErrorMessage ?? "Cannot reach terminal.";
                return ConnectionErrorReturnCode;
            }

            if (error != null)
                throw error;

            response = localResponse;
            errorMessage = null;
            return localReturnCode;
        }

        private static int DispatchTransaction(string cardTypeUpper, object terminal, ArgsModel model, out object response)
        {
            switch (cardTypeUpper)
            {
                case "CREDIT":
                    return DoCreditHandler.Run(terminal, model, out response);

                case "DEBIT":
                    return DoDebitHandler.Run(terminal, model, out response);

                case "EBT_CASHBENEFIT":
                case "EBT_CASH":
                case "EBT_FOODSTAMP":
                case "EBT_FOOD":
                    return DoEbtHandler.Run(terminal, model, out response);

                default:
                    throw new NotSupportedException($"Unsupported CardType: {cardTypeUpper}");
            }
        }
    }
}
