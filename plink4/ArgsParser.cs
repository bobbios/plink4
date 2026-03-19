using System;

namespace plink4
{
    internal static class ArgsParser
    {
        private static string GetArg(string[] args, int i)
            => (args != null && i >= 0 && i < args.Length && args[i] != null) ? args[i].Trim() : "";

        public static bool TryParse(string[] args, out ArgsModel m, out string err)
        {
            m = null;
            err = null;

            if (args == null || args.Length == 0)
            {
                err = "No arguments supplied.";
                return false;
            }

            if (TryParseSixArgBatchClose(args, out m, out err))
                return true;

            if (TryParseSixArgLastTransaction(args, out m, out err))
                return true;

            if (args.Length < 7)
            {
                err = "Missing required arguments. Expected: ref amount cardType txnType ip tcpFlag port [surcharge] [origRef] [preTipFlag] [approvalCode] [transactionId]";
                return false;
            }

            string refNum = GetArg(args, 0);
            string amount = GetArg(args, 1);
            string arg2 = GetArg(args, 2).ToUpperInvariant();
            string arg3 = GetArg(args, 3).ToUpperInvariant();
            string ip = GetArg(args, 4);
            string tcpFlag = GetArg(args, 5);
            string argPort = GetArg(args, 6);

            string surcharge = GetArg(args, 7);
            string origRef = GetArg(args, 8);
            string preTip = (GetArg(args, 9) == "1") ? "1" : "0";
            string appr = GetArg(args, 10);
            string transId = GetArg(args, 11);

            // Old style batch close
            if (arg2 == "BATCHCLOSE")
            {
                m = BuildBatchClose(refNum, amount, ip, tcpFlag, argPort);
                return ValidateIpOnly(m, out err);
            }

            // Custom command style:
            // ref amount lasttransaction SALE ip tcpFlag port
            // 7-arg style if ever sent this way:
            // ref amount LASTTRANSACTION SALE ip tcpFlag port
            if (arg2 == "LASTTRANSACTION")
            {
                m = BuildLastTransaction(refNum, amount, arg3, ip, tcpFlag, argPort);
                return ValidateBasic(m, out err, requireAmount: false);
            }

            if (arg3 == "ADJUST")
            {
                preTip = "0";
                appr = GetArg(args, 8);
                transId = GetArg(args, 9);
            }

            m = new ArgsModel
            {
                RefNum = refNum,
                Amount = amount,
                CardType = arg2,
                Command = arg2,
                TxnType = arg3,
                Ip = ip,
                TcpFlag = tcpFlag,
                ArgPort = argPort,
                Surcharge = surcharge,
                OriginalRef = origRef,
                PreTipFlag = preTip,
                ApprovalCode = appr,
                TransactionId = transId
            };

            return ValidateBasic(m, out err, requireAmount: true);
        }


        private static bool TryParseSixArgLastTransaction(string[] args, out ArgsModel m, out string err)
        {
            m = null;
            err = null;

            if (args.Length == 6 && GetArg(args, 1).ToUpperInvariant() == "LASTTRANSACTION")
            {
                string refNum = GetArg(args, 0);
                string txnType = GetArg(args, 2);
                string ip = GetArg(args, 3);
                string tcpFlag = GetArg(args, 4);
                string argPort = GetArg(args, 5);

                m = new ArgsModel
                {
                    RefNum = string.IsNullOrEmpty(refNum) ? "NA" : refNum,
                    Amount = "0",
                    CardType = "LASTTRANSACTION",
                    Command = "LASTTRANSACTION",
                    TxnType = string.IsNullOrEmpty(txnType) ? "LASTTRANSACTION" : txnType.ToUpperInvariant(),
                    Ip = ip,
                    TcpFlag = tcpFlag,
                    ArgPort = argPort,
                    Surcharge = "",
                    OriginalRef = "",
                    PreTipFlag = "0",
                    ApprovalCode = "",
                    TransactionId = ""
                };

                return ValidateIpOnly(m, out err);
            }

            return false;
        }


        private static bool TryParseSixArgBatchClose(string[] args, out ArgsModel m, out string err)
        {
            m = null;
            err = null;

            if (args.Length == 6 && GetArg(args, 1).ToUpperInvariant() == "BATCHCLOSE")
            {
                string refNum = GetArg(args, 0);
                string ip = GetArg(args, 3);
                string tcpFlag = GetArg(args, 4);
                string argPort = GetArg(args, 5);

                m = BuildBatchClose(refNum, "0", ip, tcpFlag, argPort);
                return ValidateIpOnly(m, out err);
            }

            return false;
        }

        private static ArgsModel BuildBatchClose(string refNum, string amount, string ip, string tcpFlag, string argPort)
        {
            return new ArgsModel
            {
                RefNum = string.IsNullOrEmpty(refNum) ? "NA" : refNum,
                Amount = string.IsNullOrEmpty(amount) ? "0" : amount,
                CardType = "BATCHCLOSE",
                Command = "BATCHCLOSE",
                TxnType = "BATCHCLOSE",
                Ip = ip,
                TcpFlag = tcpFlag,
                ArgPort = argPort,
                Surcharge = "",
                OriginalRef = "",
                PreTipFlag = "0",
                ApprovalCode = "",
                TransactionId = ""
            };
        }

        private static ArgsModel BuildLastTransaction(string refNum, string amount, string txnType, string ip, string tcpFlag, string argPort)
        {
            return new ArgsModel
            {
                RefNum = string.IsNullOrEmpty(refNum) ? "NA" : refNum,
                Amount = string.IsNullOrEmpty(amount) ? "0" : amount,
                CardType = "LASTTRANSACTION",
                Command = "LASTTRANSACTION",
                TxnType = string.IsNullOrEmpty(txnType) ? "LASTTRANSACTION" : txnType,
                Ip = ip,
                TcpFlag = tcpFlag,
                ArgPort = argPort,
                Surcharge = "",
                OriginalRef = "",
                PreTipFlag = "0",
                ApprovalCode = "",
                TransactionId = ""
            };
        }

        private static bool ValidateIpOnly(ArgsModel m, out string err)
        {
            err = null;
            if (string.IsNullOrEmpty(m.Ip))
            {
                err = "IP missing";
                return false;
            }
            return true;
        }

        private static bool ValidateBasic(ArgsModel m, out string err, bool requireAmount)
        {
            err = null;

            if (string.IsNullOrEmpty(m.RefNum)) { err = "RefNum missing"; return false; }
            if (requireAmount && string.IsNullOrEmpty(m.Amount)) { err = "Amount missing"; return false; }
            if (string.IsNullOrEmpty(m.CardType)) { err = "CardType missing"; return false; }
            if (string.IsNullOrEmpty(m.TxnType)) { err = "TxnType missing"; return false; }
            if (string.IsNullOrEmpty(m.Ip)) { err = "IP missing"; return false; }

            return true;
        }
    }
}