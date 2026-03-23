using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace plink4
{
    internal static class cEwicHostResponseParser
    {
        internal sealed class HostApprovedItem
        {
            public string Upc { get; set; }
            public int ApprovedQty { get; set; }
            public decimal ApprovedAmount { get; set; }
            public string Status { get; set; }
            public string Reason { get; set; }
        }

        internal sealed class HostBalance
        {
            public string Category { get; set; }
            public decimal RemainingQty { get; set; }
            public string Unit { get; set; }
        }

        internal sealed class HostResponse
        {
            public string ResponseCode { get; set; }
            public string ResponseMessage { get; set; }
            public string TransactionId { get; set; }
            public string ApprovalCode { get; set; }
            public bool IsApproved { get; set; }
            public List<HostApprovedItem> Items { get; set; } = new List<HostApprovedItem>();
            public List<HostBalance> Balances { get; set; } = new List<HostBalance>();
        }

        internal sealed class ParsedBasketResult
        {
            public List<cEwicBasketSplitter.SplitItem> ApprovedItems { get; set; } = new List<cEwicBasketSplitter.SplitItem>();
            public List<cEwicBasketSplitter.SplitItem> PartialItems { get; set; } = new List<cEwicBasketSplitter.SplitItem>();
            public List<cEwicBasketSplitter.SplitItem> RejectedItems { get; set; } = new List<cEwicBasketSplitter.SplitItem>();
        }

        public static HostResponse ParseSimpleResponse(string text)
        {
            var response = new HostResponse();

            if (string.IsNullOrWhiteSpace(text))
            {
                response.ResponseCode = "999";
                response.ResponseMessage = "Empty host response";
                response.IsApproved = false;
                return response;
            }

            var lines = text
                .Replace("\r", "")
                .Split('\n')
                .Select(x => x == null ? "" : x.Trim())
                .Where(x => x.Length > 0)
                .ToList();

            foreach (var line in lines)
            {
                if (StartsWithKey(line, "ResponseCode"))
                {
                    response.ResponseCode = GetValue(line);
                    continue;
                }

                if (StartsWithKey(line, "ResponseMessage"))
                {
                    response.ResponseMessage = GetValue(line);
                    continue;
                }

                if (StartsWithKey(line, "TransactionId"))
                {
                    response.TransactionId = GetValue(line);
                    continue;
                }

                if (StartsWithKey(line, "ApprovalCode"))
                {
                    response.ApprovalCode = GetValue(line);
                    continue;
                }

                if (StartsWithKey(line, "Approved"))
                {
                    response.IsApproved = ParseBool(GetValue(line));
                    continue;
                }

                if (line.StartsWith("ITEM|", StringComparison.OrdinalIgnoreCase))
                {
                    response.Items.Add(ParseItemLine(line));
                    continue;
                }

                if (line.StartsWith("BALANCE|", StringComparison.OrdinalIgnoreCase))
                {
                    response.Balances.Add(ParseBalanceLine(line));
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(response.ResponseCode))
                response.ResponseCode = response.IsApproved ? "000" : "999";

            if (string.IsNullOrWhiteSpace(response.ResponseMessage))
                response.ResponseMessage = response.IsApproved ? "APPROVED" : "DECLINED";

            return response;
        }

        public static ParsedBasketResult ApplyHostResponse(
            List<cEwicBasketSplitter.SplitItem> requestedItems,
            HostResponse response)
        {
            if (requestedItems == null) throw new ArgumentNullException(nameof(requestedItems));
            if (response == null) throw new ArgumentNullException(nameof(response));

            var result = new ParsedBasketResult();

            foreach (var req in requestedItems)
            {
                if (req == null) continue;

                var hostItem = response.Items.FirstOrDefault(x =>
                    string.Equals(Safe(x.Upc), Safe(req.Upc), StringComparison.OrdinalIgnoreCase));

                if (hostItem == null)
                {
                    result.RejectedItems.Add(new cEwicBasketSplitter.SplitItem
                    {
                        Upc = req.Upc,
                        Description = req.Description,
                        Category = req.Category,
                        RequestedQty = req.RequestedQty,
                        ApprovedQty = 0,
                        RemainingQty = req.RequestedQty,
                        UnitPrice = req.UnitPrice,
                        Result = "REJECTED",
                        Reason = "Host returned no item decision"
                    });
                    continue;
                }

                int approvedQty = hostItem.ApprovedQty;
                if (approvedQty < 0) approvedQty = 0;
                if (approvedQty > req.RequestedQty) approvedQty = req.RequestedQty;

                int remainingQty = req.RequestedQty - approvedQty;

                if (approvedQty == req.RequestedQty && approvedQty > 0)
                {
                    result.ApprovedItems.Add(new cEwicBasketSplitter.SplitItem
                    {
                        Upc = req.Upc,
                        Description = req.Description,
                        Category = req.Category,
                        RequestedQty = req.RequestedQty,
                        ApprovedQty = approvedQty,
                        RemainingQty = 0,
                        UnitPrice = req.UnitPrice,
                        Result = "APPROVED",
                        Reason = Safe(hostItem.Reason, "Fully approved by host")
                    });
                }
                else if (approvedQty > 0)
                {
                    result.PartialItems.Add(new cEwicBasketSplitter.SplitItem
                    {
                        Upc = req.Upc,
                        Description = req.Description,
                        Category = req.Category,
                        RequestedQty = req.RequestedQty,
                        ApprovedQty = approvedQty,
                        RemainingQty = remainingQty,
                        UnitPrice = req.UnitPrice,
                        Result = "PARTIAL",
                        Reason = Safe(hostItem.Reason, "Partially approved by host")
                    });
                }
                else
                {
                    result.RejectedItems.Add(new cEwicBasketSplitter.SplitItem
                    {
                        Upc = req.Upc,
                        Description = req.Description,
                        Category = req.Category,
                        RequestedQty = req.RequestedQty,
                        ApprovedQty = 0,
                        RemainingQty = req.RequestedQty,
                        UnitPrice = req.UnitPrice,
                        Result = "REJECTED",
                        Reason = Safe(hostItem.Reason, "Rejected by host")
                    });
                }
            }

            return result;
        }

        public static void PrintResponse(HostResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            Console.WriteLine("========== HOST RESPONSE ==========");
            Console.WriteLine("ResponseCode   : " + Safe(response.ResponseCode));
            Console.WriteLine("ResponseMessage: " + Safe(response.ResponseMessage));
            Console.WriteLine("TransactionId  : " + Safe(response.TransactionId));
            Console.WriteLine("ApprovalCode   : " + Safe(response.ApprovalCode));
            Console.WriteLine("IsApproved     : " + response.IsApproved);

            Console.WriteLine("========== HOST ITEMS ==========");
            foreach (var item in response.Items)
            {
                Console.WriteLine(
                    "UPC=" + Safe(item.Upc) +
                    " | ApprovedQty=" + item.ApprovedQty.ToString(CultureInfo.InvariantCulture) +
                    " | ApprovedAmount=" + item.ApprovedAmount.ToString("0.00", CultureInfo.InvariantCulture) +
                    " | Status=" + Safe(item.Status) +
                    " | Reason=" + Safe(item.Reason));
            }

            Console.WriteLine("========== HOST BALANCES ==========");
            foreach (var bal in response.Balances)
            {
                Console.WriteLine(
                    "Category=" + Safe(bal.Category) +
                    " | RemainingQty=" + bal.RemainingQty.ToString("0.##", CultureInfo.InvariantCulture) +
                    " | Unit=" + Safe(bal.Unit));
            }
        }

        public static void PrintParsedBasketResult(ParsedBasketResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            Console.WriteLine("========== APPROVED ==========");
            foreach (var x in result.ApprovedItems)
            {
                Console.WriteLine(
                    x.Description + " | UPC=" + x.Upc +
                    " | Req=" + x.RequestedQty +
                    " | Appr=" + x.ApprovedQty +
                    " | Rem=" + x.RemainingQty +
                    " | " + x.Reason);
            }

            Console.WriteLine("========== PARTIAL ==========");
            foreach (var x in result.PartialItems)
            {
                Console.WriteLine(
                    x.Description + " | UPC=" + x.Upc +
                    " | Req=" + x.RequestedQty +
                    " | Appr=" + x.ApprovedQty +
                    " | Rem=" + x.RemainingQty +
                    " | " + x.Reason);
            }

            Console.WriteLine("========== REJECTED ==========");
            foreach (var x in result.RejectedItems)
            {
                Console.WriteLine(
                    x.Description + " | UPC=" + x.Upc +
                    " | Req=" + x.RequestedQty +
                    " | Appr=" + x.ApprovedQty +
                    " | Rem=" + x.RemainingQty +
                    " | " + x.Reason);
            }
        }

        public static void RunTest()
        {
            var requestedItems = new List<cEwicBasketSplitter.SplitItem>
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
                    Reason = "Pre-check passed"
                },
                new cEwicBasketSplitter.SplitItem
                {
                    Upc = "012345678902",
                    Description = "Eggs Dozen",
                    Category = "EGGS",
                    RequestedQty = 2,
                    ApprovedQty = 2,
                    RemainingQty = 0,
                    UnitPrice = 2.49m,
                    Result = "APPROVED",
                    Reason = "Pre-check passed"
                },
                new cEwicBasketSplitter.SplitItem
                {
                    Upc = "012345678904",
                    Description = "Cereal 18oz",
                    Category = "CEREAL",
                    RequestedQty = 2,
                    ApprovedQty = 2,
                    RemainingQty = 0,
                    UnitPrice = 4.29m,
                    Result = "APPROVED",
                    Reason = "Pre-check passed"
                }
            };

            string rawHostResponse =
                "ResponseCode: 000\n" +
                "ResponseMessage: APPROVED\n" +
                "TransactionId: EWIC123456\n" +
                "ApprovalCode: A12345\n" +
                "Approved: true\n" +
                "ITEM|UPC=012345678901|ApprovedQty=1|ApprovedAmount=3.99|Status=APPROVED|Reason=Milk approved\n" +
                "ITEM|UPC=012345678902|ApprovedQty=1|ApprovedAmount=2.49|Status=PARTIAL|Reason=Only one dozen eggs remaining\n" +
                "ITEM|UPC=012345678904|ApprovedQty=0|ApprovedAmount=0.00|Status=REJECTED|Reason=No cereal benefits left\n" +
                "BALANCE|Category=MILK|RemainingQty=0|Unit=GAL\n" +
                "BALANCE|Category=EGGS|RemainingQty=0|Unit=DOZEN\n" +
                "BALANCE|Category=CEREAL|RemainingQty=0|Unit=OZ\n";

            var response = ParseSimpleResponse(rawHostResponse);
            PrintResponse(response);

            Console.WriteLine();

            var parsed = ApplyHostResponse(requestedItems, response);
            PrintParsedBasketResult(parsed);
        }

        private static HostApprovedItem ParseItemLine(string line)
        {
            var item = new HostApprovedItem();

            var parts = line.Split('|');
            foreach (var part in parts)
            {
                if (part.StartsWith("ITEM", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (StartsWithKey(part, "UPC"))
                {
                    item.Upc = GetValue(part);
                    continue;
                }

                if (StartsWithKey(part, "ApprovedQty"))
                {
                    item.ApprovedQty = ParseInt(GetValue(part));
                    continue;
                }

                if (StartsWithKey(part, "ApprovedAmount"))
                {
                    item.ApprovedAmount = ParseDecimal(GetValue(part));
                    continue;
                }

                if (StartsWithKey(part, "Status"))
                {
                    item.Status = GetValue(part);
                    continue;
                }

                if (StartsWithKey(part, "Reason"))
                {
                    item.Reason = GetValue(part);
                    continue;
                }
            }

            return item;
        }

        private static HostBalance ParseBalanceLine(string line)
        {
            var bal = new HostBalance();

            var parts = line.Split('|');
            foreach (var part in parts)
            {
                if (part.StartsWith("BALANCE", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (StartsWithKey(part, "Category"))
                {
                    bal.Category = GetValue(part);
                    continue;
                }

                if (StartsWithKey(part, "RemainingQty"))
                {
                    bal.RemainingQty = ParseDecimal(GetValue(part));
                    continue;
                }

                if (StartsWithKey(part, "Unit"))
                {
                    bal.Unit = GetValue(part);
                    continue;
                }
            }

            return bal;
        }

        private static bool StartsWithKey(string text, string key)
        {
            if (text == null) return false;
            return text.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetValue(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            int p = text.IndexOf(':');
            int q = text.IndexOf('=');

            int idx;
            if (p < 0) idx = q;
            else if (q < 0) idx = p;
            else idx = Math.Min(p, q);

            if (idx < 0) return "";
            if (idx + 1 >= text.Length) return "";

            return text.Substring(idx + 1).Trim();
        }

        private static bool ParseBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            value = value.Trim();

            return value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("y", StringComparison.OrdinalIgnoreCase)
                || value.Equals("approved", StringComparison.OrdinalIgnoreCase);
        }

        private static int ParseInt(string value)
        {
            int n;
            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out n))
                return n;

            if (int.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out n))
                return n;

            return 0;
        }

        private static decimal ParseDecimal(string value)
        {
            decimal d;
            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return d;

            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out d))
                return d;

            return 0m;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }

        private static string Safe(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}