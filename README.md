# DexCore — Institutional Hybrid DEX

Production-ready next-generation decentralized exchange core  
**Architecture:** Off-chain CLOB Matching + On-chain Settlement (Hyperliquid / dYdX / Uniswap v4 inspired)

---

## Module Status

| # | Module | Status |
|---|--------|--------|
| 1 | Matching Engine (Price-Time CLOB, Channels) | ✅ |
| 2 | Risk Engine (Cross/Isolated IM/MM, Liquidations) | ✅ |
| 3 | Smart Contracts + Settlement Service | ✅ |
| 4 | Market Data Streaming (SignalR, Delta Compression, gRPC protos) | ✅ |
| 5 | Institutional Compliance (KYC/AML Hooks, ZK Credentials, Sanctions) | ✅ |
| 6 | Integration / Load Tests + Formal Verification Specs | ✅ |

---

## Quick Start

### Prerequisites
- .NET 9 SDK
- Node.js 18+ (for Hardhat contracts)
- Docker (optional — Postgres / Redis)

### 1. Infrastructure
```bash
docker compose -f deploy/docker-compose.yml up -d
```

### 2. .NET — restore, build, test
```bash
cd artifacts/DexCore   # or your clone root
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

### 3. Contracts
```bash
cd contracts
npm install
npx hardhat compile
npx hardhat test
```

### 4. Load benchmarks (standalone)
```bash
dotnet run -c Release --project tests/Dex.LoadTests
# or via xUnit:
dotnet test tests/Dex.LoadTests -c Release --logger "console;verbosity=detailed"
```

### 5. Formal verification specs
See `contracts/formal/INVARIANTS.md` and `*.spec` (Certora CVL / Halmos).

### 6. Run ApiGateway + Blazor Terminal

```bash
# Terminal 1 — matching + risk + order API
dotnet run --project src/Dex.ApiGateway
# listens on http://localhost:5080

# Terminal 2 — trading UI
dotnet run --project src/Dex.BlazorUI
# open http://localhost:5200
```

Blazor uses live Binance market data and dual-writes orders to local portfolio + ApiGateway when available.

---

## Project Layout

```
DexCore/
├── ARCHITECTURE.md
├── README.md
├── DexCore.sln
├── deploy/docker-compose.yml
├── contracts/
│   ├── src/           # Solidity 0.8.26 (Vault, Settlement, Oracle, Risk, Compliance, ZK)
│   ├── test/          # Hardhat tests
│   └── formal/        # Certora CVL specs + invariants
├── src/
│   ├── Dex.Domain
│   ├── Dex.MatchingEngine
│   ├── Dex.RiskEngine
│   ├── Dex.SettlementService
│   ├── Dex.Contracts          # .proto (market_data, trading)
│   ├── Dex.ApiGateway         # SignalR, rate limit, HMAC, trading session
│   ├── Dex.ComplianceService  # AML, ZK adapter, rule engine
│   └── Dex.BlazorUI           # Trading terminal (WASM)
└── tests/
    ├── Dex.*.Tests            # Unit tests per module
    ├── Dex.IntegrationTests   # E2E lifecycle
    └── Dex.LoadTests          # Throughput / fan-out / GC pressure
```

---

## Performance Targets (Load Tests)

| Component | Target | Notes |
|-----------|--------|-------|
| Matching OrderBook place/match | ≥ 100k ops/sec (ideal) | Soft floor asserted in CI |
| Single-op latency p99 | < 500 µs (ideal) | Hard ceiling 5 ms in CI |
| Risk concurrent validations | thousands parallel | Parallel.For harness |
| Market data fan-out | 500–10k+ subscribers | Channel DropOldest |

Run: `dotnet test tests/Dex.LoadTests -c Release`

---

## Security & Compliance Highlights

- Non-custodial Vault with timelocked withdrawals + anti-bank-run window  
- ComplianceHook (sanctions + optional ZK credential) before deposit/trade/withdraw  
- ZKCredentialVerifier (nullifier, attestation expiry)  
- Batch settlement idempotency (`settledBatches`)  
- Formal invariants: solvency, replay protection, unauthorized withdrawal impossibility  

---

## Production Checklist

1. Replace simulated AML / ZK prover with Chainalysis (or equivalent) + real circuit verifier  
2. Wire Nethereum sender to live RPC + HSM-backed relayer keys  
3. Deploy contracts behind multisig + timelock  
4. Enable OpenTelemetry metrics/traces on ApiGateway & MatchingEngine  
5. Run Certora / external audit on VaultManagerAdvanced + DexSettlement  
6. Configure TimescaleDB hypertables for trades / mark prices  
7. Load-test on target hardware and tune Channel capacities / batch sizes  

---

## License

Proprietary / institutional use — configure as needed.
