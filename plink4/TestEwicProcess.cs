using System;
using System.Collections.Generic;
using System.Linq;

namespace plink4
{
    internal static class TestEwicProcess
    {
        internal sealed class AplItem
        {
            public string Upc { get; set; }
            public string Description { get; set; }
            public string Category { get; set; }
            public string SubCategory { get; set; }
            public string SizeCode { get; set; }
            public decimal SizeValue { get; set; }
            public string SizeUnit { get; set; }
            public bool IsWicApproved { get; set; }
        }

        internal sealed class ScannedItem
        {
            public string Upc { get; set; }
            public string Description { get; set; }
            public int Qty { get; set; }
        }

        internal sealed class WicBenefit
        {
            public string Category { get; set; }
            public decimal RemainingQty { get; set; }
            public string Unit { get; set; }
        }

        internal sealed class EwicCheckResult
        {
            public string Upc { get; set; }
            public string Description { get; set; }

            public bool AplLookup { get; set; }
            public bool WicMatch { get; set; }
            public bool CategoryMatch { get; set; }
            public bool QtyCheck { get; set; }
            public bool SizeCheck { get; set; }

            public string Category { get; set; }
            public decimal NeededQty { get; set; }
            public decimal RemainingQty { get; set; }
            public string Unit { get; set; }

            public string ResultMessage { get; set; }
        }

        public static void Run()
        {
            Logger.Debug("TestEwicProcess.Run start");

            var aplLoaded = false;
            var upcScan = "";

            var aplList = BuildApl();
            var benefits = BuildBenefits();
            var basket = BuildScannedBasket();

            aplLoaded = aplList.Count > 0;

            Logger.Debug("EWIC STEP 1: APL Loaded = " + aplLoaded);
            Logger.Debug("EWIC APL Count = " + aplList.Count);
            Logger.Debug("EWIC Benefit Count = " + benefits.Count);
            Logger.Debug("EWIC Basket Count = " + basket.Count);
            Logger.Debug("------------------------------------------------------------");

            foreach (var item in basket)
            {
                upcScan = item.Upc;

                Logger.Debug("Processing basket item UPC=" + item.Upc +
                             " Desc=" + item.Description +
                             " Qty=" + item.Qty);

                var result = ProcessItem(upcScan, item, aplList, benefits);

                Logger.Debug("UPC Scan      : " + result.Upc);
                Logger.Debug("Description   : " + result.Description);
                Logger.Debug("APL Lookup    : " + result.AplLookup);
                Logger.Debug("WIC Match     : " + result.WicMatch);
                Logger.Debug("Category Match: " + result.CategoryMatch);
                Logger.Debug("Qty Check     : " + result.QtyCheck);
                Logger.Debug("Size Check    : " + result.SizeCheck);
                Logger.Debug("Category      : " + result.Category);
                Logger.Debug("Needed Qty    : " + result.NeededQty + " " + result.Unit);
                Logger.Debug("Remaining Qty : " + result.RemainingQty + " " + result.Unit);
                Logger.Debug("Result        : " + result.ResultMessage);
                Logger.Debug("------------------------------------------------------------");
            }

            Logger.Debug("TestEwicProcess.Run end");
        }

        private static EwicCheckResult ProcessItem(
            string upcScan,
            ScannedItem scanned,
            List<AplItem> aplList,
            List<WicBenefit> benefits)
        {
            var result = new EwicCheckResult
            {
                Upc = upcScan,
                Description = scanned.Description,
                AplLookup = false,
                WicMatch = false,
                CategoryMatch = false,
                QtyCheck = false,
                SizeCheck = false,
                Category = "",
                NeededQty = 0,
                RemainingQty = 0,
                Unit = "",
                ResultMessage = "NOT PROCESSED"
            };

            var aplItem = aplList.FirstOrDefault(x =>
                string.Equals(x.Upc, upcScan, StringComparison.OrdinalIgnoreCase));

            result.AplLookup = aplItem != null;

            if (aplItem == null)
            {
                Logger.Debug("ProcessItem: UPC not found in APL");
                result.ResultMessage = "UPC not found in APL";
                return result;
            }

            Logger.Debug("ProcessItem: APL match found Category=" + aplItem.Category +
                         " SizeValue=" + aplItem.SizeValue +
                         " SizeUnit=" + aplItem.SizeUnit +
                         " IsWicApproved=" + aplItem.IsWicApproved);

            result.WicMatch = aplItem.IsWicApproved;
            result.Category = aplItem.Category;
            result.Unit = aplItem.SizeUnit;

            if (!aplItem.IsWicApproved)
            {
                Logger.Debug("ProcessItem: UPC found but not WIC approved");
                result.ResultMessage = "UPC found but not WIC approved";
                return result;
            }

            var benefit = benefits.FirstOrDefault(x =>
                string.Equals(x.Category, aplItem.Category, StringComparison.OrdinalIgnoreCase));

            result.CategoryMatch = benefit != null;

            if (benefit == null)
            {
                Logger.Debug("ProcessItem: No matching WIC benefit category");
                result.ResultMessage = "No matching WIC benefit category";
                return result;
            }

            Logger.Debug("ProcessItem: Benefit match found Category=" + benefit.Category +
                         " RemainingQty=" + benefit.RemainingQty +
                         " Unit=" + benefit.Unit);

            result.RemainingQty = benefit.RemainingQty;
            result.Unit = benefit.Unit;

            result.SizeCheck = string.Equals(aplItem.SizeUnit, benefit.Unit, StringComparison.OrdinalIgnoreCase);

            if (!result.SizeCheck)
            {
                Logger.Debug("ProcessItem: Unit/size mismatch aplUnit=" + aplItem.SizeUnit +
                             " benefitUnit=" + benefit.Unit);
                result.ResultMessage = "Unit/size mismatch";
                return result;
            }

            result.NeededQty = aplItem.SizeValue * scanned.Qty;
            result.QtyCheck = benefit.RemainingQty >= result.NeededQty;

            Logger.Debug("ProcessItem: NeededQty=" + result.NeededQty +
                         " AvailableQty=" + benefit.RemainingQty +
                         " QtyCheck=" + result.QtyCheck);

            if (!result.QtyCheck)
            {
                Logger.Debug("ProcessItem: Not enough remaining benefit quantity");
                result.ResultMessage = "Not enough remaining benefit quantity";
                return result;
            }

            result.ResultMessage = "WIC ELIGIBLE";

            benefit.RemainingQty -= result.NeededQty;
            result.RemainingQty = benefit.RemainingQty;

            Logger.Debug("ProcessItem: Benefit consumed newRemainingQty=" + benefit.RemainingQty);

            return result;
        }

        private static List<AplItem> BuildApl()
        {
            return new List<AplItem>
            {
                new AplItem
                {
                    Upc = "012345678901",
                    Description = "Whole Milk Gallon",
                    Category = "MILK",
                    SubCategory = "WHOLE",
                    SizeCode = "1GAL",
                    SizeValue = 1m,
                    SizeUnit = "GAL",
                    IsWicApproved = true
                },
                new AplItem
                {
                    Upc = "012345678902",
                    Description = "Large Eggs Dozen",
                    Category = "EGGS",
                    SubCategory = "GRADE_A",
                    SizeCode = "12CT",
                    SizeValue = 1m,
                    SizeUnit = "DOZEN",
                    IsWicApproved = true
                },
                new AplItem
                {
                    Upc = "012345678903",
                    Description = "Sugary Soda",
                    Category = "DRINK",
                    SubCategory = "SOFTDRINK",
                    SizeCode = "2LTR",
                    SizeValue = 2m,
                    SizeUnit = "LTR",
                    IsWicApproved = false
                },
                new AplItem
                {
                    Upc = "012345678904",
                    Description = "Cereal 18oz",
                    Category = "CEREAL",
                    SubCategory = "WHOLEGRAIN",
                    SizeCode = "18OZ",
                    SizeValue = 18m,
                    SizeUnit = "OZ",
                    IsWicApproved = true
                }
            };
        }

        private static List<WicBenefit> BuildBenefits()
        {
            return new List<WicBenefit>
            {
                new WicBenefit
                {
                    Category = "MILK",
                    RemainingQty = 2m,
                    Unit = "GAL"
                },
                new WicBenefit
                {
                    Category = "EGGS",
                    RemainingQty = 2m,
                    Unit = "DOZEN"
                },
                new WicBenefit
                {
                    Category = "CEREAL",
                    RemainingQty = 36m,
                    Unit = "OZ"
                }
            };
        }

        private static List<ScannedItem> BuildScannedBasket()
        {
            return new List<ScannedItem>
            {
                new ScannedItem
                {
                    Upc = "012345678901",
                    Description = "Whole Milk Gallon",
                    Qty = 1
                },
                new ScannedItem
                {
                    Upc = "012345678902",
                    Description = "Large Eggs Dozen",
                    Qty = 1
                },
                new ScannedItem
                {
                    Upc = "012345678903",
                    Description = "Sugary Soda",
                    Qty = 1
                },
                new ScannedItem
                {
                    Upc = "012345678904",
                    Description = "Cereal 18oz",
                    Qty = 2
                }
            };
        }
    }
}