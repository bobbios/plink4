using System;

namespace plink4
{
    internal static class AppConfig
    {
        public const int PortAlways = 10009;
        public const int TimeoutMs = 120000;

        public const string LogPath = @"C:\newretail\card\plink4.txt";
        public const string OutResponse = @"C:\newretail\card\response.txt";
        public const string BalanceResponse = @"C:\newretail\card\ebt_balance.txt";
        public const string OutResponse2 = @"C:\newretail\card\response2.txt";
        public const string BatchResponse = @"C:\newretail\card\batchresponse.txt";
        public const string LastTransactionResponse = @"C:\newretail\card\last10Transactions.txt";
        public const string Last10Transactions = @"C:\newretail\card\last10Transactions.txt";
    }
}