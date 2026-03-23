using System;
using System.Collections.Generic;
using System.Linq;

namespace plink4
{
    internal static class cEwicBasketSplitter
    {
        internal sealed class AplItem
        {
            public string Upc { get; set; }
            public string Description { get; set; }
            public string Category { get; set; }
            public decimal SizeValue { get; set; }
            public string SizeUnit { get; set; }
            public bool IsWicApproved { get; set; }
        }

        internal sealed class ScannedItem
        {
            public string Upc { get; set; }
            public string Description { get; set; }
            public int Qty { get; set; }
            public decimal Price { get; set; }
        }

        internal sealed class WicBenefit
        {
            public string Category { get; set; }
            public decimal RemainingQty { get; set; }
            public string Unit { get; set; }
        }

        internal sealed class SplitItem
        {
            public string Upc { get; set; }
            public string Description { get; set; }
            public string Category { get; set; }
            public int RequestedQty { get; set; }
            public int ApprovedQty { get; set; }
            public int RemainingQty { get; set; }
            public decimal UnitPrice { get; set; }
            public string Result { get; set; }
            public string Reason { get; set; }
        }

        internal sealed class SplitResult
        {
            public List<SplitItem> WicApprovedItems { get; set; } = new List<SplitItem>();
            public List<SplitItem> PartialItems { get; set; } = new List<SplitItem>();
            public List<SplitItem> NonWicItems { get; set; } = new List<SplitItem>();
        }

        public static SplitResult SplitBasket(
            List<ScannedItem> basket,
            List<AplItem> aplList,
            List<WicBenefit> benefits)
        {
            var result = new SplitResult();

            if (basket == null) throw new ArgumentNullException(nameof(basket));
            if (aplList == null) throw new ArgumentNullException(nameof(aplList));
            if (benefits == null) throw new ArgumentNullException(nameof(benefits));

            Logger.Debug("cEwicBasketSplitter.SplitBasket start");
            Logger.Debug("Basket count=" + basket.Count + " APL count=" + aplList.Count + " Benefits count=" + benefits.Count);

            foreach (var scanned in basket)
            {
                Logger.Debug("SplitBasket item start UPC=" + scanned.Upc +
                             " Desc=" + scanned.Description +
                             " Qty=" + scanned.Qty +
                             " Price=" + scanned.Price);

                var apl = aplList.FirstOrDefault(x =>
                    string.Equals(x.Upc, scanned.Upc, StringComparison.OrdinalIgnoreCase));

                if (apl == null)
                {
                    Logger.Debug("SplitBasket result: NON-WIC reason=UPC not found in APL");

                    result.NonWicItems.Add(new SplitItem
                    {
                        Upc = scanned.Upc,
                        Description = scanned.Description,
                        RequestedQty = scanned.Qty,
                        ApprovedQty = 0,
                        RemainingQty = scanned.Qty,
                        UnitPrice = scanned.Price,
                        Result = "NON-WIC",
                        Reason = "UPC not found in APL"
                    });
                    continue;
                }

                if (!apl.IsWicApproved)
                {
                    Logger.Debug("SplitBasket result: NON-WIC reason=UPC found but not WIC approved");

                    result.NonWicItems.Add(new SplitItem
                    {
                        Upc = scanned.Upc,
                        Description = scanned.Description,
                        Category = apl.Category,
                        RequestedQty = scanned.Qty,
                        ApprovedQty = 0,
                        RemainingQty = scanned.Qty,
                        UnitPrice = scanned.Price,
                        Result = "NON-WIC",
                        Reason = "UPC found but not WIC approved"
                    });
                    continue;
                }

                var benefit = benefits.FirstOrDefault(x =>
                    string.Equals(x.Category, apl.Category, StringComparison.OrdinalIgnoreCase));

                if (benefit == null)
                {
                    Logger.Debug("SplitBasket result: NON-WIC reason=No matching benefit category");

                    result.NonWicItems.Add(new SplitItem
                    {
                        Upc = scanned.Upc,
                        Description = scanned.Description,
                        Category = apl.Category,
                        RequestedQty = scanned.Qty,
                        ApprovedQty = 0,
                        RemainingQty = scanned.Qty,
                        UnitPrice = scanned.Price,
                        Result = "NON-WIC",
                        Reason = "No matching benefit category"
                    });
                    continue;
                }

                if (!string.Equals(apl.SizeUnit, benefit.Unit, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Debug("SplitBasket result: NON-WIC reason=Unit mismatch aplUnit=" + apl.SizeUnit + " benefitUnit=" + benefit.Unit);

                    result.NonWicItems.Add(new SplitItem
                    {
                        Upc = scanned.Upc,
                        Description = scanned.Description,
                        Category = apl.Category,
                        RequestedQty = scanned.Qty,
                        ApprovedQty = 0,
                        RemainingQty = scanned.Qty,
                        UnitPrice = scanned.Price,
                        Result = "NON-WIC",
                        Reason = "Unit mismatch"
                    });
                    continue;
                }

                decimal requiredPerItem = apl.SizeValue;
                decimal availableQty = benefit.RemainingQty;

                Logger.Debug("SplitBasket match found Category=" + apl.Category +
                             " requiredPerItem=" + requiredPerItem +
                             " availableQty=" + availableQty);

                if (requiredPerItem <= 0)
                {
                    Logger.Debug("SplitBasket result: NON-WIC reason=Invalid APL size value");

                    result.NonWicItems.Add(new SplitItem
                    {
                        Upc = scanned.Upc,
                        Description = scanned.Description,
                        Category = apl.Category,
                        RequestedQty = scanned.Qty,
                        ApprovedQty = 0,
                        RemainingQty = scanned.Qty,
                        UnitPrice = scanned.Price,
                        Result = "NON-WIC",
                        Reason = "Invalid APL size value"
                    });
                    continue;
                }

                int maxApprovableQty = (int)Math.Floor(availableQty / requiredPerItem);
                if (maxApprovableQty < 0) maxApprovableQty = 0;

                Logger.Debug("SplitBasket computed maxApprovableQty=" + maxApprovableQty);

                if (maxApprovableQty >= scanned.Qty)
                {
                    benefit.RemainingQty -= requiredPerItem * scanned.Qty;

                    Logger.Debug("SplitBasket result: APPROVED approvedQty=" + scanned.Qty +
                                 " newRemainingBenefitQty=" + benefit.RemainingQty);

                    result.WicApprovedItems.Add(new SplitItem
                    {
                        Upc = scanned.Upc,
                        Description = scanned.Description,
                        Category = apl.Category,
                        RequestedQty = scanned.Qty,
                        ApprovedQty = scanned.Qty,
                        RemainingQty = 0,
                        UnitPrice = scanned.Price,
                        Result = "APPROVED",
                        Reason = "Fully covered by WIC"
                    });
                }
                else if (maxApprovableQty > 0)
                {
                    benefit.RemainingQty -= requiredPerItem * maxApprovableQty;

                    Logger.Debug("SplitBasket result: PARTIAL approvedQty=" + maxApprovableQty +
                                 " remainingQty=" + (scanned.Qty - maxApprovableQty) +
                                 " newRemainingBenefitQty=" + benefit.RemainingQty);

                    result.PartialItems.Add(new SplitItem
                    {
                        Upc = scanned.Upc,
                        Description = scanned.Description,
                        Category = apl.Category,
                        RequestedQty = scanned.Qty,
                        ApprovedQty = maxApprovableQty,
                        RemainingQty = scanned.Qty - maxApprovableQty,
                        UnitPrice = scanned.Price,
                        Result = "PARTIAL",
                        Reason = "Only part of requested quantity covered by WIC"
                    });
                }
                else
                {
                    Logger.Debug("SplitBasket result: NON-WIC reason=No remaining WIC quantity");

                    result.NonWicItems.Add(new SplitItem
                    {
                        Upc = scanned.Upc,
                        Description = scanned.Description,
                        Category = apl.Category,
                        RequestedQty = scanned.Qty,
                        ApprovedQty = 0,
                        RemainingQty = scanned.Qty,
                        UnitPrice = scanned.Price,
                        Result = "NON-WIC",
                        Reason = "No remaining WIC quantity"
                    });
                }
            }

            Logger.Debug("cEwicBasketSplitter.SplitBasket end Approved=" + result.WicApprovedItems.Count +
                         " Partial=" + result.PartialItems.Count +
                         " NonWic=" + result.NonWicItems.Count);

            return result;
        }

        public static void PrintResult(SplitResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            Logger.Debug("========== WIC APPROVED ==========");
            foreach (var x in result.WicApprovedItems)
            {
                Logger.Debug(
                    x.Description + " | UPC=" + x.Upc +
                    " | Req=" + x.RequestedQty +
                    " | Appr=" + x.ApprovedQty +
                    " | Rem=" + x.RemainingQty +
                    " | " + x.Reason);
            }

            Logger.Debug("========== PARTIAL ==========");
            foreach (var x in result.PartialItems)
            {
                Logger.Debug(
                    x.Description + " | UPC=" + x.Upc +
                    " | Req=" + x.RequestedQty +
                    " | Appr=" + x.ApprovedQty +
                    " | Rem=" + x.RemainingQty +
                    " | " + x.Reason);
            }

            Logger.Debug("========== NON-WIC ==========");
            foreach (var x in result.NonWicItems)
            {
                Logger.Debug(
                    x.Description + " | UPC=" + x.Upc +
                    " | Req=" + x.RequestedQty +
                    " | Appr=" + x.ApprovedQty +
                    " | Rem=" + x.RemainingQty +
                    " | " + x.Reason);
            }
        }

        public static void RunTest()
        {
            Logger.Debug("cEwicBasketSplitter.RunTest start");

            var aplList = new List<AplItem>
            {
                new AplItem { Upc = "012345678901", Description = "Whole Milk Gallon", Category = "MILK",   SizeValue = 1m,  SizeUnit = "GAL",   IsWicApproved = true  },
                new AplItem { Upc = "012345678902", Description = "Eggs Dozen",        Category = "EGGS",   SizeValue = 1m,  SizeUnit = "DOZEN", IsWicApproved = true  },
                new AplItem { Upc = "012345678903", Description = "Soda 2 Liter",      Category = "DRINK",  SizeValue = 2m,  SizeUnit = "LTR",   IsWicApproved = false },
                new AplItem { Upc = "012345678904", Description = "Cereal 18oz",       Category = "CEREAL", SizeValue = 18m, SizeUnit = "OZ",    IsWicApproved = true  }
            };

            var benefits = new List<WicBenefit>
            {
                new WicBenefit { Category = "MILK",   RemainingQty = 2m,  Unit = "GAL" },
                new WicBenefit { Category = "EGGS",   RemainingQty = 1m,  Unit = "DOZEN" },
                new WicBenefit { Category = "CEREAL", RemainingQty = 18m, Unit = "OZ" }
            };

            var basket = new List<ScannedItem>
            {
                new ScannedItem { Upc = "012345678901", Description = "Whole Milk Gallon", Qty = 1, Price = 3.99m },
                new ScannedItem { Upc = "012345678902", Description = "Eggs Dozen",        Qty = 2, Price = 2.49m },
                new ScannedItem { Upc = "012345678903", Description = "Soda 2 Liter",      Qty = 1, Price = 1.99m },
                new ScannedItem { Upc = "012345678904", Description = "Cereal 18oz",       Qty = 2, Price = 4.29m },
                new ScannedItem { Upc = "999999999999", Description = "Unknown Item",      Qty = 1, Price = 5.00m }
            };

            var split = SplitBasket(basket, aplList, benefits);
            PrintResult(split);

            Logger.Debug("WIC Approved Count = " + split.WicApprovedItems.Count);
            Logger.Debug("Partial Count = " + split.PartialItems.Count);
            Logger.Debug("Non-WIC Count = " + split.NonWicItems.Count);

            Logger.Debug("cEwicBasketSplitter.RunTest end");
        }
    }
}