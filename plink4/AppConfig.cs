using System;

namespace plink4
{
    internal static class AppConfig
    {
        public const int PortAlways = 10009;
        public const int TimeoutMs = 120000;
        public const int ConnectCheckTimeoutMs = 5000;

        // After Cancel() is sent to the terminal, how long to let the in-flight
        // DoCredit/DoDebit/DoEbt call unwind before this process exits. Without this,
        // the worker is a background thread that gets killed the instant Main()
        // returns, tearing the TCP session down abruptly instead of letting the SDK
        // close it cleanly — which is why the terminal can be left still showing its
        // "apply card" prompt even though Cancel() was called.
        public const int CancelGraceMs = 4000;

        public const string LogPath = @"C:\newretail\card\plink4.txt";
        public const string OutResponse = @"C:\newretail\card\response.txt";
        public const string OutResponse2 = @"C:\newretail\card\response2.txt";
        public const string BatchResponse = @"C:\newretail\card\batchresponse.txt";
        public const string LastTransactionResponse = @"C:\newretail\card\lasttransactionresponse.txt";
        public const string Last10Transactions = @"C:\newretail\card\last10Transactions.txt";
        public const string BalanceResponse = @"C:\newretail\card\ebt_balance.txt";
        public const string LoyaltyResponse = @"C:\newretail\card\loyalty_response.txt";
    }
}