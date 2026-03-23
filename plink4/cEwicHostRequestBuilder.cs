using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace plink4
{
    internal static class cEwicHostRequestBuilder
    {
        internal sealed class HostItem
        {
            public string Upc { get; set; }
            public string Description { get; set; }
            public string Category { get; set; }
            public int Qty { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal ExtendedPrice
            {
                get { return UnitPrice * Qty; }
            }
        }

        internal sealed class HostRequest
        {
            public string StoreId { get; set; }
            public string TerminalId { get; set; }
            public string LaneId { get; set; }
            public string TransactionId { get; set; }
            public string CashierId { get; set; }
            public string Timestamp { get; set; }
            public string TenderType { get; set; }
            public int ItemCount { get; set; }
            public decimal TotalAmount { get; set; }
            public List<HostItem> Items { get; set; } = new List<HostItem>();
        }

        public static HostRequest BuildRequest(
            string storeId,
            string terminalId,
            string laneId,
            string transactionId,
            string cashierId,
            List<cEwicBasketSplitter.SplitItem> approvedItems)
        {
            if (string.IsNullOrWhiteSpace(storeId))
                throw new ArgumentException("storeId is required.", nameof(storeId));

            if (string.IsNullOrWhiteSpace(terminalId))
                throw new ArgumentException("terminalId is required.", nameof(terminalId));

            if (string.IsNullOrWhiteSpace(transactionId))
                throw new ArgumentException("transactionId is required.", nameof(transactionId));

            if (approvedItems == null)
                throw new ArgumentNullException(nameof(approvedItems));

            var request = new HostRequest
            {
                StoreId = storeId.Trim(),
                TerminalId = terminalId.Trim(),
                LaneId = (laneId ?? "").Trim(),
                TransactionId = transactionId.Trim(),
                CashierId = (cashierId ?? "").Trim(),
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                TenderType = "EWIC"
            };

            foreach (var item in approvedItems)
            {
                if (item == null) continue;
                if (item.ApprovedQty <= 0) continue;

                request.Items.Add(new HostItem
                {
                    Upc = Safe(item.Upc),
                    Description = Safe(item.Description),
                    Category = Safe(item.Category),
                    Qty = item.ApprovedQty,
                    UnitPrice = item.UnitPrice
                });
            }

            request.ItemCount = request.Items.Count;
            request.TotalAmount = request.Items.Sum(x => x.ExtendedPrice);

            return request;
        }

        public static string ToPlainText(HostRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var sb = new StringBuilder();

            sb.AppendLine("EWIC HOST REQUEST");
            sb.AppendLine("StoreId: " + request.StoreId);
            sb.AppendLine("TerminalId: " + request.TerminalId);
            sb.AppendLine("LaneId: " + request.LaneId);
            sb.AppendLine("TransactionId: " + request.TransactionId);
            sb.AppendLine("CashierId: " + request.CashierId);
            sb.AppendLine("Timestamp: " + request.Timestamp);
            sb.AppendLine("TenderType: " + request.TenderType);
            sb.AppendLine("ItemCount: " + request.ItemCount);
            sb.AppendLine("TotalAmount: " + request.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine("Items:");

            for (int i = 0; i < request.Items.Count; i++)
            {
                var item = request.Items[i];
                sb.AppendLine(
                    (i + 1).ToString(CultureInfo.InvariantCulture) +
                    ". UPC=" + item.Upc +
                    " | Desc=" + item.Description +
                    " | Cat=" + item.Category +
                    " | Qty=" + item.Qty.ToString(CultureInfo.InvariantCulture) +
                    " | Price=" + item.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture) +
                    " | Ext=" + item.ExtendedPrice.ToString("0.00", CultureInfo.InvariantCulture)
                );
            }

            return sb.ToString();
        }

        public static string ToJsonLikeText(HostRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var sb = new StringBuilder();

            sb.AppendLine("{");
            sb.AppendLine("  \"storeId\": \"" + Esc(request.StoreId) + "\",");
            sb.AppendLine("  \"terminalId\": \"" + Esc(request.TerminalId) + "\",");
            sb.AppendLine("  \"laneId\": \"" + Esc(request.LaneId) + "\",");
            sb.AppendLine("  \"transactionId\": \"" + Esc(request.TransactionId) + "\",");
            sb.AppendLine("  \"cashierId\": \"" + Esc(request.CashierId) + "\",");
            sb.AppendLine("  \"timestamp\": \"" + Esc(request.Timestamp) + "\",");
            sb.AppendLine("  \"tenderType\": \"" + Esc(request.TenderType) + "\",");
            sb.AppendLine("  \"itemCount\": " + request.ItemCount.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"totalAmount\": " + request.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"items\": [");

            for (int i = 0; i < request.Items.Count; i++)
            {
                var item = request.Items[i];

                sb.AppendLine("    {");
                sb.AppendLine("      \"upc\": \"" + Esc(item.Upc) + "\",");
                sb.AppendLine("      \"description\": \"" + Esc(item.Description) + "\",");
                sb.AppendLine("      \"category\": \"" + Esc(item.Category) + "\",");
                sb.AppendLine("      \"qty\": " + item.Qty.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"unitPrice\": " + item.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"extendedPrice\": " + item.ExtendedPrice.ToString("0.00", CultureInfo.InvariantCulture));
                sb.Append("    }");

                if (i < request.Items.Count - 1)
                    sb.Append(",");

                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            return sb.ToString();
        }

        public static void RunTest()
        {
            var approvedItems = new List<cEwicBasketSplitter.SplitItem>
            {
                new cEwicBasketSplitter.SplitItem
                {
                    Upc = "012345678901",
                    Description = "Whole Milk Gallon",
                    Category = "MILK",
                    RequestedQty = 1,
                    ApprovedQty = 1,
                    RemainingQty = 0,
                    UnitPrice = 3.99m,
                    Result = "APPROVED",
                    Reason = "Fully covered by WIC"
                },
                new cEwicBasketSplitter.SplitItem
                {
                    Upc = "012345678902",
                    Description = "Eggs Dozen",
                    Category = "EGGS",
                    RequestedQty = 2,
                    ApprovedQty = 1,
                    RemainingQty = 1,
                    UnitPrice = 2.49m,
                    Result = "PARTIAL",
                    Reason = "Only part covered"
                },
                new cEwicBasketSplitter.SplitItem
                {
                    Upc = "012345678903",
                    Description = "Soda 2 Liter",
                    Category = "DRINK",
                    RequestedQty = 1,
                    ApprovedQty = 0,
                    RemainingQty = 1,
                    UnitPrice = 1.99m,
                    Result = "NON-WIC",
                    Reason = "Not WIC approved"
                }
            };

            var req = BuildRequest(
                storeId: "1001",
                terminalId: "02",
                laneId: "LANE01",
                transactionId: "TXN123456",
                cashierId: "CASH01",
                approvedItems: approvedItems
            );

            Console.WriteLine(ToPlainText(req));
            Console.WriteLine();
            Console.WriteLine(ToJsonLikeText(req));
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }

        private static string Esc(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}