namespace plink4
{
    internal sealed class ArgsModel
    {
        public string RefNum { get; set; }        // 123456
        public string Amount { get; set; }        // 125
        public string CardType { get; set; }      // CREDIT
        public string TxnType { get; set; }       // ADJUST

        public string Command { get; set; }       // optional custom command from args[2]

        public string Ip { get; set; }            // 192.168.3.165
        public string TcpFlag { get; set; }       // Y
        public string ArgPort { get; set; }       // 10009

        public string Surcharge { get; set; }
        public string PreTipFlag { get; set; }

        public string ApprovalCode { get; set; }  // 000000
        public string TransactionId { get; set; } // 0002
    }
}