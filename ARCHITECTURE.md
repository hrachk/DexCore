# Institutional-Grade Hybrid DEX — System Architecture Specification
**Version:** 1.0.0  
**Classification:** Production / Institutional DeFi  
**Date:** 2026-09-16  
**Authors:** Principal Architect / Quant / Security Team  

---

## 1. Hybrid Architecture Decision

### 1.1 AMM (Uniswap v4 Hooks) vs CLOB (dYdX / Hyperliquid)

| Criterion                    | Concentrated Liquidity + Hooks (v4) | Off-chain CLOB + On-chain Settlement | Hybrid Choice for Institutional DEX |
|-----------------------------|-------------------------------------|--------------------------------------|-------------------------------------|
| Latency                     | Block-time bound (seconds)          | Sub-millisecond matching             | **CLOB for matching**               |
| Capital Efficiency          | Excellent (concentrated)            | Excellent (order book depth)         | Both                                |
| Price Discovery             | Curve-based                         | True continuous double auction       | **CLOB**                            |
| MEV Surface                 | High (public mempool)               | Controllable via private RPC / batch | Private matching + batch settlement |
| Institutional UX            | Good for passive LP                 | CEX-like experience                  | **CLOB primary**                    |
| Composability               | Native EVM                          | Requires bridge / HyperEVM-style     | Settlement on EVM + optional Hooks  |
| Throughput                  | Limited by gas                      | 50k–200k+ orders/sec (off-chain)     | **CLOB engine**                     |

**Decision:**  
Primary execution model = **Off-chain High-Performance Matching Engine (C#)** with **on-chain Settlement & Risk Settlement** (EVM smart contracts).  
Optional Uniswap-v4-style Hooks layer for passive concentrated liquidity and permissioned / RWA pools.

This combines:
- Hyperliquid / dYdX performance & UX
- Uniswap v4 flexibility for long-tail / institutional permissioned markets
- Full non-custodial guarantees

### 1.2 Order Execution Model

```
Client → API Gateway (gRPC / WebSocket) 
       → Matching Engine (Lock-free Ring Buffer / Channels)
       → Risk Engine (pre-trade + post-trade)
       → Settlement Queue (batched)
       → On-chain Settlement Contract (via Nethereum)
```

Matching latency target: **< 200 µs p99** on co-located hardware (pure in-memory, zero allocation path on hot path).

---

## 2. Institutional Security Architecture

### 2.1 MEV / Front-running Protection

- All matching occurs **off-chain** in a private, permissioned or economically secured matching cluster.
- Settlement transactions are submitted via **private mempools** (Flashbots Protect / MEV-Share / builder API) or as **bundles**.
- Time-boost / priority fee auctions are not exposed to public searchers for core fills.
- Optional: encrypted order flow (future threshold encryption / TEE).

### 2.2 Real-time Risk Engine (C#)

- **Initial Margin (IM)** and **Maintenance Margin (MM)** calculated continuously.
- Support for **Cross-Margin** and **Isolated Margin**.
- Pre-trade risk check (available margin, position limits, open interest caps).
- Post-trade and mark-to-market liquidation engine with partial liquidations.
- Insurance fund + auto-deleveraging (ADL) as last resort.
- All calculations use fixed-point decimal (no floating point on critical path) or high-precision `decimal` with explicit rounding rules.

### 2.3 Smart Contract Security

- ReentrancyGuard on all external entry points.
- Pausable (circuit breaker) with multi-sig + timelock.
- Oracle Manipulation Guard:
  - Multi-source (Chainlink + Pyth + custom TWAP from CLOB itself)
  - Medianizer + deviation circuit breaker
  - Stale price rejection
- Formal verification of core settlement and margin math (Certora / Halmos style).
- Large bug bounty + continuous monitoring (OpenZeppelin Defender / custom).

---

## 3. .NET 9 Backend Infrastructure

### 3.1 High-Throughput Event Sourcing + CQRS

- Commands (PlaceOrder, CancelOrder, …) → Matching Engine
- Events (OrderAccepted, TradeExecuted, OrderCancelled, …) → Event Store + Projections
- Read models for Order Book L2/L3, Positions, Account Equity served from Redis + TimescaleDB.

### 3.2 Matching Core

- Lock-free design based on `System.Threading.Channels` + single-writer principle where possible.
- Price-Time Priority Order Book using sorted structures optimized for hot path (`SortedDictionary` with care, or custom skip-list / array-based price levels).
- Zero-allocation hot path via `ArrayPool<T>`, `Span<T>`, `ValueTask`, stackalloc where safe.
- Disruptor-style ring buffer pattern implemented with Channels for producer-consumer isolation.

### 3.3 Streaming

- gRPC bi-directional streaming for institutional clients.
- SignalR / WebSocket for retail / browser.
- L2/L3 Order Book snapshots + incremental deltas.

---

## 4. Non-Functional Targets

| Metric                          | Target                          |
|---------------------------------|---------------------------------|
| Matching latency (p99)          | < 200 µs                        |
| Throughput (orders/sec)         | > 50 000 sustained              |
| Settlement finality             | Dependent on L1/L2 (seconds)    |
| Availability                    | 99.99% (matching cluster)       |
| Data consistency                | Exactly-once settlement semantics via idempotent on-chain calls |

---

## 5. Technology Stack Confirmation

- Runtime: .NET 9
- Web: ASP.NET Core Minimal APIs + gRPC
- Messaging: System.Threading.Channels + MassTransit (Kafka / RabbitMQ)
- Persistence: PostgreSQL + TimescaleDB, Redis
- Blockchain: Nethereum
- Observability: OpenTelemetry + structured ILogger
- Testing: xUnit + FluentAssertions + NBomber / k6

This document is the single source of truth for Phase 1.  
All subsequent code must adhere to these principles.
