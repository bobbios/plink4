using System;
using System.Linq;
using System.Reflection;
using POSLinkCore.CommunicationSetting;
using POSLinkSemiIntegration;

namespace plink4
{
    internal static class PoslinkSemi
    {
        // ---------------------------
        // CONNECT (strong-typed now)
        // ---------------------------
        public static object ConnectTcp(string ip, int port, int timeoutMs)
        {
            var pos = POSLinkSemi.GetPOSLinkSemi();
            if (pos == null) throw new Exception("GetPOSLinkSemi() returned null.");

            var tcp = new TcpSetting
            {
                Ip = ip,
                Port = port,
                Timeout = timeoutMs
            };

            var term = pos.GetTerminal(tcp);
            if (term == null) throw new Exception("GetTerminal returned null (terminal offline/IP wrong?).");

            return term;
        }

        // ---------------------------
        // EBT (keeps reflection for req/rsp/types)
        // ---------------------------
        // ---------------------------
        // EBT (keeps reflection for req/rsp/types)
        // ---------------------------
        public static int DoEbt(object term, ArgsModel a, out object rsp)
        {
            rsp = null;

            var tx = GetProp(term, "Transaction");
            if (tx == null) throw new Exception("Terminal.Transaction is null.");

            var reqType = FindTypeByNameContains("DoEbt", "Req") ?? FindTypeByNameContains("DoEbt", "Request");
            var rspType = FindTypeByNameContains("DoEbt", "Rsp") ?? FindTypeByNameContains("DoEbt", "Response");
            if (reqType == null || rspType == null) throw new Exception("DoEbtReq/DoEbtRsp types not found.");

            var req = Activator.CreateInstance(reqType);
            rsp = Activator.CreateInstance(rspType);

            ApplyTraceSmart(req, a.RefNum);

            var txn = (a.TxnType ?? "").Trim().ToUpperInvariant();
            var isCash = string.Equals(a.CardType, "EBT_CASHBENEFIT", StringComparison.OrdinalIgnoreCase);

            // ---------------------------------------------------
            // 0) ENTRY MODE: enable CHIP for EBT_FOODSTAMP/CASH
            //    (EntryMode is a bitmap object with bool flags)
            // ---------------------------------------------------
            var behavior = GetOrCreateProp(req, "TransactionBehavior");
            if (behavior != null)
            {
                var entryMode = GetOrCreateProp(behavior, "EntryMode");
                if (entryMode != null)
                {
                    // ✅ enable chip (EMV)
                    SetProp(entryMode, "Chip", true);

                    // ✅ allow swipe fallback (recommended)
                    SetProp(entryMode, "Swipe", true);

                    //optional: disable manual entry
                    //SetProp(entryMode, "Manual", false);

                    Logger.Info("EBT: EntryMode -> Chip=true, Swipe=true");
                }
                else
                {
                    Logger.Info("EBT: TransactionBehavior.EntryMode missing/not creatable");
                }
            }
            else
            {
                Logger.Info("EBT: TransactionBehavior missing/not creatable");
            }

            // ---------------------------------------------------
            // 1) AccountInformation
            // ---------------------------------------------------
            var acct =
                GetOrCreateProp(req, "AccountInformation") ??
                GetOrCreateProp(req, "AccountInfo");

            if (acct != null)
            {
                // Your log showed EbtType is an enum (string "F"/"C" cast fails)
                var ok = SetEnumByName(acct, "EbtType", new[]
                {
            isCash ? "Cash" : "Food",
            isCash ? "CashBenefit" : "FoodStamp",
            isCash ? "C" : "F"
        });

                if (!ok)
                    Logger.Info("EBT: WARNING - could not set EbtType (terminal may prompt for EBT type).");
            }
            else
            {
                Logger.Info("EBT: AccountInformation missing/not creatable");
            }

            // ---------------------------------------------------
            // 2) AmountInformation
            // ---------------------------------------------------
            var amt =
                GetOrCreateProp(req, "AmountInformation") ??
                GetOrCreateProp(req, "AmountInfo");

            if (amt != null && (txn == "SALE" || txn == "RETURN"))
            {
                var amountString = (a.Amount ?? "").Trim();

                var ok = false;
                ok |= SetPropLogged(amt, "TransactionAmount", amountString, "EBT");

                if (!ok)
                    Logger.Info("EBT: Could not set TransactionAmount (terminal may prompt for amount).");
            }

            // ---------------------------------------------------
            // 3) TransactionType
            // ---------------------------------------------------
            if (txn == "SALE")
            {
                var candidates = isCash
                    ? new[] { "EbtCash", "EBTCash", "CashBenefit", "EBT_CASHBENEFIT", "Sale" }
                    : new[] { "EbtFood", "EBTFood", "FoodStamp", "EBT_FOODSTAMP", "Sale" };

                if (!SetEnumByName(req, "TransactionType", candidates))
                    SetEnumByName(req, "TransactionType", new[] { "Sale" });
            }
            else if (txn == "RETURN")
            {
                if (!SetEnumByName(req, "TransactionType", new[] { "EbtCashReturn", "EbtFoodReturn", "Return" }))
                    SetEnumByName(req, "TransactionType", new[] { "Return" });
            }
            else if (txn == "INQUIRY" || txn == "INQURY" || txn == "EBTBALANCE")
            {
                SetEnumByName(req, "TransactionType", new[] { "Inquiry", "BalanceInquiry", "Balance" });
            }
            else
            {
                throw new Exception("Unsupported EBT TxnType: " + a.TxnType);
            }

            var doEbt = tx.GetType().GetMethods()
                .FirstOrDefault(m => m.Name.Equals("DoEbt", StringComparison.OrdinalIgnoreCase)
                                  && m.GetParameters().Length >= 2);
            if (doEbt == null) throw new Exception("Transaction.DoEbt not found.");

            object[] callArgs = new object[] { req, rsp };
            var execResult = doEbt.Invoke(tx, callArgs);
            rsp = callArgs[1];

            return GetErrorCodeInt(execResult) == 0 ? 0 : 1;
        }
        // ---------------------------
        // CREDIT
        // ---------------------------
        public static int DoCredit(object term, ArgsModel a, out object rsp)
        {
            rsp = null;

            var tx = GetProp(term, "Transaction");
            if (tx == null) throw new Exception("Terminal.Transaction is null.");

            var reqType = FindTypeByNameContains("DoCredit", "Req") ?? FindTypeByNameContains("DoCredit", "Request");
            var rspType = FindTypeByNameContains("DoCredit", "Rsp") ?? FindTypeByNameContains("DoCredit", "Response");
            if (reqType == null || rspType == null) throw new Exception("DoCreditReq/DoCreditRsp types not found.");

            var req = Activator.CreateInstance(reqType);
            rsp = Activator.CreateInstance(rspType);

            ApplyTraceSmart(req, a.RefNum);

            var txn = (a.TxnType ?? "").Trim().ToUpperInvariant();
            if (txn == "SALE") SetEnumByName(req, "TransactionType", new[] { "Sale" });
            else if (txn == "RETURN") SetEnumByName(req, "TransactionType", new[] { "Return" });
            else throw new Exception("Unsupported CREDIT TxnType: " + a.TxnType);

            var amt = GetProp(req, "AmountInformation");
            if (amt != null) SetProp(amt, "TransactionAmount", a.Amount);

            var trace = GetProp(req, "TraceInformation");
            if (trace != null) SetProp(trace, "EcrRefNum", a.RefNum);

            var doCredit = tx.GetType().GetMethods()
                .FirstOrDefault(m => m.Name.Equals("DoCredit", StringComparison.OrdinalIgnoreCase)
                                  && m.GetParameters().Length >= 2);
            if (doCredit == null) throw new Exception("Transaction.DoCredit not found.");

            object[] callArgs = new object[] { req, rsp };
            var execResult = doCredit.Invoke(tx, callArgs);
            rsp = callArgs[1];

            return GetErrorCodeInt(execResult) == 0 ? 0 : 1;
        }

        // ---------------------------
        // DEBIT
        // ---------------------------
        public static int DoDebit(object term, ArgsModel a, out object rsp)
        {
            rsp = null;

            var tx = GetProp(term, "Transaction");
            if (tx == null) throw new Exception("Terminal.Transaction is null.");

            var reqType = FindTypeByNameContains("DoDebit", "Req") ?? FindTypeByNameContains("DoDebit", "Request");
            var rspType = FindTypeByNameContains("DoDebit", "Rsp") ?? FindTypeByNameContains("DoDebit", "Response");
            if (reqType == null || rspType == null) throw new Exception("DoDebitReq/DoDebitRsp types not found.");

            var req = Activator.CreateInstance(reqType);
            rsp = Activator.CreateInstance(rspType);

            ApplyTraceSmart(req, a.RefNum);

            var txn = (a.TxnType ?? "").Trim().ToUpperInvariant();
            if (txn == "SALE") SetEnumByName(req, "TransactionType", new[] { "Sale" });
            else if (txn == "RETURN") SetEnumByName(req, "TransactionType", new[] { "Return" });
            else throw new Exception("Unsupported DEBIT TxnType: " + a.TxnType);

            var amt = GetProp(req, "AmountInformation");
            if (amt != null) SetProp(amt, "TransactionAmount", a.Amount);

            var trace = GetProp(req, "TraceInformation");
            if (trace != null) SetProp(trace, "EcrRefNum", a.RefNum);

            var doDebit = tx.GetType().GetMethods()
                .FirstOrDefault(m => m.Name.Equals("DoDebit", StringComparison.OrdinalIgnoreCase)
                                  && m.GetParameters().Length >= 2);
            if (doDebit == null) throw new Exception("Transaction.DoDebit not found.");

            object[] callArgs = new object[] { req, rsp };
            var execResult = doDebit.Invoke(tx, callArgs);
            rsp = callArgs[1];

            return GetErrorCodeInt(execResult) == 0 ? 0 : 1;
        }

        // ---------------------------
        // APPLY TRACE SMART (with full debug logging)
        // ---------------------------
        private static void ApplyTraceSmart(object req, string refNum)
        {
            if (req == null) return;
            if (string.IsNullOrWhiteSpace(refNum)) return;

            object trace = GetProp(req, "TraceInformation")
                        ?? GetProp(req, "TraceInfo")
                        ?? GetProp(req, "Trace")
                        ?? GetProp(req, "TraceData");

            if (trace == null)
            {
                var tReq = req.GetType();
                foreach (var p in tReq.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        var pt = p.PropertyType;
                        var name = (pt.FullName ?? pt.Name ?? "");
                        if (name.IndexOf("Trace", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            trace = p.GetValue(req, null);
                            if (trace == null)
                            {
                                if (p.CanWrite && p.PropertyType.GetConstructor(Type.EmptyTypes) != null)
                                {
                                    trace = Activator.CreateInstance(p.PropertyType);
                                    p.SetValue(req, trace, null);
                                }
                            }
                            if (trace != null) break;
                        }
                    }
                    catch { }
                }
            }

            if (trace == null)
            {
                Logger.Error("ApplyTraceSmart: no trace object found on req type " + req.GetType().FullName);
                return;
            }

            Logger.Info("ApplyTraceSmart: trace type=" + trace.GetType().FullName + " ref=" + refNum);

            Logger.Info("ApplyTraceSmart: --- ALL trace properties ---");
            foreach (var p in trace.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    var val = p.GetValue(trace, null);
                    Logger.Info("ApplyTraceSmart: trace." + p.Name + " [" + p.PropertyType.Name + "] = " + (val ?? "(null)"));
                }
                catch (Exception ex)
                {
                    Logger.Info("ApplyTraceSmart: trace." + p.Name + " [unreadable: " + ex.Message + "]");
                }
            }
            Logger.Info("ApplyTraceSmart: --- END trace properties ---");

            string[] candidates =
            {
                "EcrReferenceNumber",
                "InvoiceNumber",
                "EcrRefNum", "ECRRefNum", "EcrRefNo", "ECRRefNo",
                "InvoiceNo", "InvoiceNum",
                "RefNo", "RefNum", "ReferenceNo", "ReferenceNum", "ReferenceNumber",
                "TransId", "TransactionId", "TicketNo", "TicketNum",
                "Ref", "SeqNum", "SequenceNumber", "SequenceNum"
            };

            foreach (var propName in candidates)
            {
                bool ok1 = SetProp(trace, propName, refNum);
                bool ok2 = false;
                if (long.TryParse(refNum, out var n))
                    ok2 = SetProp(trace, propName, n);

                if (ok1 || ok2)
                    Logger.Info("ApplyTraceSmart: SET " + propName + " = " + refNum
                        + " (string=" + ok1 + " long=" + ok2 + ")");
            }

            // ✅ Timestamp fields (set TimeStamp too)
            var now = DateTime.Now;
   
            //SetProp(trace, "OriginalTransactionDate", now.ToString("yyyyMMdd"));
            //SetProp(trace, "OriginalTransactionTime", now.ToString("HHmmss"));

            Logger.Info("ApplyTraceSmart: SET TimeStamp=" + now.ToString("HHmmss")
                + " Date=" + now.ToString("yyyyMMdd")
                + " Time=" + now.ToString("HHmmss"));

            Logger.Info("ApplyTraceSmart: --- trace values AFTER set ---");
            foreach (var p in trace.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    var val = p.GetValue(trace, null);
                    Logger.Info("ApplyTraceSmart: trace." + p.Name + " = " + (val ?? "(null)"));
                }
                catch { }
            }
            Logger.Info("ApplyTraceSmart: --- END after-set dump ---");
        }

        private static void ApplyTrace(object req, string refNum)
        {
            if (string.IsNullOrWhiteSpace(refNum)) return;

            var trace = GetProp(req, "TraceInformation");
            if (trace == null) return;

            SetProp(trace, "EcrRefNum", refNum);
            SetProp(trace, "InvoiceNo", refNum);
            SetProp(trace, "InvoiceNum", refNum);
            SetProp(trace, "TransId", refNum);
            SetProp(trace, "TransactionId", refNum);

            if (long.TryParse(refNum, out var n))
            {
                SetProp(trace, "InvoiceNo", n);
                SetProp(trace, "InvoiceNum", n);
            }
        }

        // ---------- tiny reflection helpers ----------
        private static Type FindTypeByNameContains(params string[] containsAll)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }

                foreach (var t in types)
                {
                    var name = (t.FullName ?? "") + "|" + (t.Name ?? "");
                    bool ok = true;
                    foreach (var c in containsAll)
                    {
                        if (name.IndexOf(c, StringComparison.OrdinalIgnoreCase) < 0) { ok = false; break; }
                    }
                    if (ok) return t;
                }
            }
            return null;
        }

        private static object GetProp(object obj, string prop)
        {
            if (obj == null) return null;
            var pi = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
            return pi?.GetValue(obj);
        }

        private static bool SetProp(object obj, string prop, object value)
        {
            try
            {
                if (obj == null) return false;
                var pi = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null) return false;

                object v = value;
                if (value != null && !pi.PropertyType.IsAssignableFrom(value.GetType()))
                    v = Convert.ChangeType(value, pi.PropertyType);

                pi.SetValue(obj, v);
                return true;
            }
            catch { return false; }
        }

        private static bool SetEnumByName(object obj, string prop, string[] candidates)
        {
            try
            {
                var pi = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null || !pi.PropertyType.IsEnum) return false;

                foreach (var c in candidates)
                {
                    try
                    {
                        var v = Enum.Parse(pi.PropertyType, c, ignoreCase: true);
                        pi.SetValue(obj, v);
                        Logger.Info($"EBT: SET {prop} = {v} ({pi.PropertyType.Name})");
                        return true;
                    }
                    catch { }
                }
                return false;
            }
            catch { return false; }
        }

        private static object GetOrCreateProp(object obj, string propName)
        {
            if (obj == null) return null;

            var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null) return null;

            var val = pi.GetValue(obj);
            if (val != null) return val;

            if (!pi.CanWrite) return null;
            var ctor = pi.PropertyType.GetConstructor(Type.EmptyTypes);
            if (ctor == null) return null;

            val = Activator.CreateInstance(pi.PropertyType);
            pi.SetValue(obj, val);
            return val;
        }

        private static bool SetPropLogged(object obj, string prop, object value, string labelForLog)
        {
            if (obj == null)
            {
                Logger.Info($"{labelForLog}: {prop} SKIP (obj null)");
                return false;
            }

            var pi = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null)
            {
                Logger.Info($"{labelForLog}: {prop} MISSING on {obj.GetType().FullName}");
                return false;
            }

            if (!pi.CanWrite)
            {
                Logger.Info($"{labelForLog}: {prop} READONLY on {obj.GetType().FullName}");
                return false;
            }

            try
            {
                object v = value;

                if (value != null && !pi.PropertyType.IsAssignableFrom(value.GetType()))
                {
                    if (pi.PropertyType == typeof(decimal))
                        v = Convert.ToDecimal(value);
                    else if (pi.PropertyType == typeof(int))
                        v = Convert.ToInt32(value);
                    else
                        v = Convert.ChangeType(value, pi.PropertyType);
                }

                pi.SetValue(obj, v);
                Logger.Info($"{labelForLog}: SET {prop} = {v} ({pi.PropertyType.Name})");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Info($"{labelForLog}: SET {prop} FAILED ({pi.PropertyType.Name}) val='{value}' err={ex.Message}");
                return false;
            }
        }

        private static int GetErrorCodeInt(object execResult)
        {
            try
            {
                if (execResult == null) return -1;
                var m = execResult.GetType().GetMethod("GetErrorCode", BindingFlags.Public | BindingFlags.Instance);
                if (m == null) return -1;
                var v = m.Invoke(execResult, null);
                return Convert.ToInt32(v);
            }
            catch { return -1; }
        }
    }
}