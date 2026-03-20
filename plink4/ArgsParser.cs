using System;

namespace plink4
{
    internal static class ArgsParser
    {
        private static string GetArg(string[] args, int i)
        {
            if (args == null) return "";
            if (i < 0 || i >= args.Length) return "";
            return (args[i] ?? "").Trim();
        }

        public static ArgsModel Parse(string[] args)
        {
            var m = new ArgsModel();

            m.RefNum = GetArg(args, 0);
            m.Amount = GetArg(args, 1);
            m.CardType = GetArg(args, 2).ToUpperInvariant();
            m.TxnType = GetArg(args, 3).ToUpperInvariant();
            m.Ip = GetArg(args, 4);
            m.TcpFlag = GetArg(args, 5).ToUpperInvariant();
            m.ArgPort = GetArg(args, 6);

            m.ApprovalCode = GetArg(args, 7);
            m.TransactionId = GetArg(args, 8);

            return m;
        }
    }
}