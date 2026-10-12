# Formal Verification & Audit Invariants

## Scope
- `VaultManagerAdvanced.sol`
- `DexSettlement.sol`
- `ComplianceHook.sol` / `SanctionsListGuard.sol` (supporting)

## Tools
- **Certora Prover** (CVL specs in this folder)
- **Halmos** (symbolic Foundry tests — complementary)
- Manual audit checklist below

---

## Core Invariants

### I1 — Vault Solvency
```
∀ token t, user u:  locked[t][u] ≤ balances[t][u]
∀ token t:  Σ_u balances[t][u] ≤ contract_token_balance(t)
```
No user can lock or withdraw more than their recorded balance. Aggregate user claims never exceed on-chain reserves.

### I2 — Settlement Replay Protection
```
settledBatches[batchId] = true  ⇒  permanently true
settleBatch(batchId, …) when settledBatches[batchId]  ⇒  revert
```
Each batchId is executed at most once.

### I3 — Unauthorized Withdrawal Impossibility
```
executeWithdrawal(user) requires:
  1. prior requestWithdrawal by user
  2. block.timestamp ≥ request.availableAt (timelock)
  3. ComplianceHook.beforeWithdraw succeeds (sanctions + optional ZK)
  4. amount ≤ balances - locked
```
No path exists to move funds without timelock + compliance.

### I4 — Trade Zero-Sum
```
For every applied trade (maker, taker, qty):
  Δposition[taker] + Δposition[maker] = 0
```

### I5 — Bank-Run Cap
```
Within any window [windowStart, windowStart + windowSeconds):
  windowWithdrawn ≤ maxWithdrawPerWindow
```

### I6 — Access Control
```
settleBatch  → only RELAYER_ROLE
settleLiquidation → only RiskGuard.isLiquidator
pause / setPolicy → only designated admin/pauser roles
```

---

## Audit Checklist (manual)

- [ ] Reentrancy: all external asset transfers after state updates (CEI)
- [ ] Integer overflow: Solidity 0.8.26 checked arithmetic
- [ ] Oracle staleness / deviation circuit breaker tested
- [ ] EIP-712 / ECDSA signature malleability (v,r,s normalization)
- [ ] Timelock cannot be bypassed via forceUnpause without DEFAULT_ADMIN
- [ ] ComplianceHook cannot be unset to address(0) accidentally in production
- [ ] Event completeness for off-chain indexers

## Running Certora (example)

```bash
certoraRun contracts/src/VaultManagerAdvanced.sol \
  --verify VaultManagerAdvanced:contracts/formal/VaultManagerAdvanced.spec \
  --solc solc8.26
```
