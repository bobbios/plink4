# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`plink4` is a small .NET Framework 4.7.2 console app (`plink4.exe`) that acts as a CLI bridge between a legacy retail POS system and a PAX payment terminal via the PAX **POSLink Semi-Integration** SDK. The legacy POS shells out to `plink4.exe` with positional command-line arguments for each transaction (sale, return, adjust, batch close, EBT balance, last-transaction lookup, etc.), and `plink4` talks to the physical terminal over TCP and writes the result back out to well-known fixed-format text files that the legacy POS then reads.

There is no README; this file is the primary source of orientation.

## Build

Open `plink4.sln` in Visual Studio (classic MSBuild `.csproj`, not SDK-style) or build with `msbuild plink4.sln`.

**Known gotcha:** `plink4.csproj` references the POSLink SDK assemblies (`POSLinkAdmin`, `POSLinkCore`, `POSLinkSemiIntegration`, `POSLinkUart`, `Renci.SshNet`, `SshNet.Security.Cryptography`) via `HintPath` pointing at `..\..\..\..\Desktop\POSLink_Semi_Integration_.Net_Standard_V2.02.00_20251017\...\Libs\*.dll` — a path outside the repo, specific to the original dev machine. The same DLLs are also checked into `plink4\lib\pax\`, but the project is *not* currently wired to reference them from there. A clean checkout on a machine without that Desktop folder will fail to build; either recreate that folder structure or repoint the `HintPath`s at `lib\pax\`.

There are no automated tests in this repo.

## Running

`plink4.exe` takes purely **positional** (not named/flagged) arguments, parsed by `ArgsParser.Parse` into `ArgsModel`:

| index | field | example |
|---|---|---|
| 0 | RefNum | `123456` |
| 1 | Amount | `125` (cents) |
| 2 | CardType | `CREDIT`, `DEBIT`, `EBT_FOOD`, `EBT_CASH`, `EBT_CASHBENEFIT`, `EBT_FOODSTAMP`, `BATCHCLOSE`, `LASTTRANSACTION` |
| 3 | TxnType | `SALE`, `RETURN`, `ADJUST`, `BALANCE`, `INQUIRY` |
| 4 | Ip | terminal IP, e.g. `192.168.3.165` |
| 5 | TcpFlag | `Y` |
| 6 | ArgPort | terminal TCP port, e.g. `10009` |
| 7 | ApprovalCode | required for `ADJUST` |
| 8 | TransactionId | required for `ADJUST` (original ref/trans id) |

`CardType` and `TxnType` are upper-cased during parsing and are compared case-sensitively downstream in `CommandRouter`, so callers don't need to worry about case but code changes should keep using upper-case literals.

Output/input file paths are hardcoded in `AppConfig.cs` under `C:\newretail\card\` (response.txt, response2.txt, batchresponse.txt, lasttransactionresponse.txt, last10Transactions.txt, ebt_balance.txt), and daily rotating logs go to `C:\newretail\card\log\plink4-yy-MM-dd.txt` via `Logger.cs`. These paths are Windows-machine-specific and assumed to exist/be creatable on the POS machine — don't assume they're relative to the repo.

## Architecture

**Flow:** `Program.Main` → `ArgsParser.Parse` (args → `ArgsModel`) → `CommandRouter.Execute` (routes by CardType/TxnType) → a per-transaction-type handler → `LegacyResponseWriter` (writes the result files the legacy POS reads back).

**`CommandRouter.Execute`** special-cases a few flows *before* opening a terminal connection (`BATCHCLOSE`, `LASTTRANSACTION`, `ADJUST`, EBT `BALANCE`), each of which manages its own terminal connection internally. For the remaining standard payment flows (`CREDIT`, `DEBIT`, `EBT_*`), it calls `ConnectTerminal` once and dispatches on `CardType` to `DoCreditHandler`, `DoDebitHandler`, or `DoEbtHandler`.

**Reflection-based SDK access is the core design pattern of this codebase.** Rather than referencing POSLink SDK request/response types directly, nearly everything goes through `PoslinkReflection.cs`:
- `ConnectTerminal` (in `CommandRouter`) resolves `POSLinkSemiIntegration.POSLinkSemi` and `POSLinkCore.CommunicationSetting.TcpSetting` by full type name via `Type.GetType`, then calls `GetTerminal(TcpSetting)` reflectively.
- `PoslinkReflection.CreateRequest`/`CreateResponse` find a request/response type by scanning all loaded assemblies for a type name containing the operation name plus `"Req"`/`"Rsp"` (or `"Request"`/`"Response"`), then `Activator.CreateInstance` it.
- `PoslinkReflection.InvokeTxMethod` finds the transaction method by name (e.g. `DoCredit`, `DoDebit`, `DoEbt`, `BatchClose`) via reflection and invokes it with `(req, ref rsp)`, returning 0/1 based on the SDK's `GetErrorCode()`.
- `PoslinkReflection.SetProperty`/`SetEnumProperty`/`GetOrCreateProperty` set fields defensively, trying several candidate property/enum-value names since the exact SDK shape isn't relied upon at compile time (and appears to have shifted across SDK versions during development — see commit history).
- `PoslinkRequestBuilder.cs` layers common request-building helpers (trace/reference numbers, transaction-type enums, amount/surcharge handling, EBT-specific fields) on top of `PoslinkReflection`, again probing multiple possible property names per field.

When adding a new transaction type or fixing a field mapping, follow this same defensive, name-probing pattern rather than hardcoding a single expected SDK property name — the existing handlers do this because the SDK's actual shape has needed rediscovery via reflection dumps (see `DoEbtBalanceHandler`'s use of `ObjectDumper`/inline dump helpers and `Logger.Debug` calls to inspect live terminal responses).

**Per-transaction handlers** (`Do*Handler.cs`) each own one flow end-to-end: build request → invoke → interpret response → return a 0/1 result code (and, in the special-cased flows like `DoBatchCloseHandler`, `DoCreditAdjustHandler`, `LastTransactionHandler`, `DoEbtBalanceHandler`, write their own dedicated output file directly rather than going through `LegacyResponseWriter`). `DoEbtBalanceHandler` is the outlier: instead of using `PoslinkReflection`'s generic type-name search, it pulls the `_transaction` private field directly off the terminal object and uses `dynamic` against the concrete `POSLinkSemiIntegration.Transaction.DoEbtRequest`/`DoEbtResponse` types, because the generic lookup didn't work reliably for this flow.

**`LegacyResponseWriter.cs`** writes `response.txt` in a fixed 26-line `Key: Value` schema (`Result`, `CardType`, `TxnType`, `Amount`, `AuthCode`, etc., with unused trailing lines as `FieldN: `) — the legacy POS reads this format, so line meaning/order must not change without also updating the POS side. `WriteDump` writes `response2.txt` as a raw reflection dump of the whole response object tree, mainly for debugging.
