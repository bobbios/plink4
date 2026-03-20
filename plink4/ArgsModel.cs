namespace plink4
{
    internal sealed class ArgsModel
    {
        public string RefNum;
        public string Amount;

        public string CardType;      // CREDIT / DEBIT / EBT_CASHBENEFIT / EBT_FOODSTAMP / BATCHCLOSE
        public string TxnType;       // SALE / RETURN / INQUIRY / ADJUST / BATCHCLOSE / LASTTRANSACTION
        public string Command;       // optional custom command from args[2]

        public string Ip;
        public string TcpFlag;
        public string ArgPort;
        public string OriginalRef;
        public string Surcharge;

        public string PreTipFlag;
        public string ApprovalCode { get; set; }
        public string TransactionId { get; set; }
    }
}