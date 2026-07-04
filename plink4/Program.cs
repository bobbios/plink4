using System;
using System.Linq;
using System.Windows.Forms;

namespace plink4
{
    internal class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Without this, WinForms controls (including the marquee ProgressBar)
            // render with classic/unthemed GDI styles and the marquee never animates —
            // this app started as a console app so it never got the usual WinForms
            // startup boilerplate.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Log startup banner + all raw arguments in one line
            Logger.LogStartup(args);

            // Optional: show all arguments in a single compact line
            if (args.Length > 0)
            {
                string argsLine = string.Join(" ", args.Select(a => $"'{a}'"));
            }
            else
            {
                Logger.Info("No command-line arguments provided.");
            }

            var model = ArgsParser.Parse(args);

            // One-line summary of parsed values (most useful single line for quick review)
            Logger.Info(
                $"Parsed: ref={model.RefNum ?? "-"} amt={model.Amount ?? "-"} " +
                $"card={model.CardType ?? "-"} type={model.TxnType ?? "-"} " +
                $"ip={model.Ip ?? "-"} flag={model.TcpFlag ?? "-"} port={model.ArgPort ?? "-"} " +
                $"approval={model.ApprovalCode ?? "-"} transId={model.TransactionId ?? "-"}"
            );

            return CommandRouter.Execute(model);
        }
    }
}