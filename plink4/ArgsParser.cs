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

            // defaults
            m.Surcharge = "0";
            m.PreTipFlag = "N";
            m.ApprovalCode = "";
            m.TransactionId = "";

            if (m.CardType == "CREDIT")
            {
                if (m.TxnType == "SALE")
                {
                    // plink4.exe ref amt CREDIT SALE ip tcpFlag port surcharge pretip
                    m.Surcharge = GetArg(args, 7);
                    m.PreTipFlag = GetArg(args, 8).ToUpperInvariant();
                }
                else if (m.TxnType == "RETURN")
                {
                    // plink4.exe ref amt CREDIT RETURN ip tcpFlag port approvalCode transactionId
                    m.ApprovalCode = GetArg(args, 7);
                    m.TransactionId = GetArg(args, 8);
                }
                else if (m.TxnType == "ADJUST")
                {
                    // plink4.exe ref amt CREDIT ADJUST ip tcpFlag port approvalCode transactionId
                    m.ApprovalCode = GetArg(args, 7);
                    m.TransactionId = GetArg(args, 8);
                }
                else
                {
                    // fallback
                    m.ApprovalCode = GetArg(args, 7);
                    m.TransactionId = GetArg(args, 8);
                }
            }
            else
            {
                // existing generic behavior for other types
                m.ApprovalCode = GetArg(args, 7);
                m.TransactionId = GetArg(args, 8);
            }

            return m;
        }
    }
}