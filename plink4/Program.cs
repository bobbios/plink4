using System;
using System.Linq;

namespace plink4
{
    internal class Program
    {
        static int Main(string[] args)
        {
            Logger.Debug("Program.Main start");

            // Log startup banner
            Logger.LogStartup(args);

            Logger.Debug("After LogStartup");

            // Optional: show all arguments in a single compact line
            if (args.Length > 0)
            {
                string argsLine = string.Join(" ", args.Select(a => $"'{a}'"));
                Logger.Debug("Raw args: " + argsLine);
            }
            else
            {
                Logger.Info("No command-line arguments provided.");
            }

            Logger.Debug("Before ArgsParser.Parse");

            var model = ArgsParser.Parse(args);

            Logger.Debug("After ArgsParser.Parse");

            // One-line summary of parsed values
            Logger.Info(
                $"Parsed: ref={model.RefNum ?? "-"} amt={model.Amount ?? "-"} " +
                $"card={model.CardType ?? "-"} type={model.TxnType ?? "-"} " +
                $"ip={model.Ip ?? "-"} flag={model.TcpFlag ?? "-"} port={model.ArgPort ?? "-"} " +
                $"approval={model.ApprovalCode ?? "-"} transId={model.TransactionId ?? "-"}"
            );

            Logger.Debug("Before CommandRouter.Execute");

            int rc = CommandRouter.Execute(model);

            Logger.Debug("After CommandRouter.Execute rc=" + rc);

            return rc;
        }
    }
}