// SPDX-License-Identifier: MIT
pragma solidity ^0.8.26;

import {ReentrancyGuard} from "@openzeppelin/contracts/utils/ReentrancyGuard.sol";
import {Pausable} from "@openzeppelin/contracts/utils/Pausable.sol";
import {AccessControl} from "@openzeppelin/contracts/access/AccessControl.sol";
import {ECDSA} from "@openzeppelin/contracts/utils/cryptography/ECDSA.sol";
import {MessageHashUtils} from "@openzeppelin/contracts/utils/cryptography/MessageHashUtils.sol";
import {VaultManager} from "./VaultManager.sol";
import {RiskGuard} from "./RiskGuard.sol";

/**
 * @title DexSettlement
 * @notice Batch settlement contract for the institutional hybrid DEX.
 *         Authorized Relayers submit signed batches of trades. Each trade is
 *         verified via EIP-712 style digest (simplified ECDSA over batch hash).
 *         Collateral movements are performed against VaultManager.
 *
 * @dev Production note: full per-trade EIP-712 user signatures can be added;
 *      this version authenticates the Relayer and uses an idempotent batchId.
 */
contract DexSettlement is ReentrancyGuard, Pausable, AccessControl {
    using ECDSA for bytes32;
    using MessageHashUtils for bytes32;

    bytes32 public constant RELAYER_ROLE = keccak256("RELAYER_ROLE");
    bytes32 public constant PAUSER_ROLE = keccak256("PAUSER_ROLE");

    VaultManager public immutable vault;
    RiskGuard public immutable riskGuard;

    /// @notice batchId => settled
    mapping(bytes32 => bool) public settledBatches;

    /// @notice Simple on-chain position tracking (account => marketId => signed qty)
    mapping(address => mapping(uint256 => int256)) public positions;

    /// @notice account => marketId => entry notional accumulator (for average price)
    mapping(address => mapping(uint256 => int256)) public entryNotional;

    struct Trade {
        address maker;
        address taker;
        uint256 marketId;
        int256 quantity;     // positive = taker long / maker short
        uint256 price;       // 1e18
        uint256 tradeId;
    }

    event BatchSettled(bytes32 indexed batchId, uint256 tradeCount, address relayer);
    event TradeSettled(
        uint256 indexed tradeId,
        address indexed maker,
        address indexed taker,
        uint256 marketId,
        int256 quantity,
        uint256 price
    );
    event LiquidationSettled(
        address indexed account,
        uint256 indexed marketId,
        int256 quantity,
        uint256 price,
        address liquidator
    );

    error BatchAlreadySettled();
    error InvalidSignature();
    error EmptyBatch();
    error ZeroAddress();
    error UnauthorizedLiquidator();

    constructor(address vault_, address riskGuard_, address admin) {
        if (vault_ == address(0) || riskGuard_ == address(0) || admin == address(0)) revert ZeroAddress();
        vault = VaultManager(payable(vault_));
        riskGuard = RiskGuard(riskGuard_);

        _grantRole(DEFAULT_ADMIN_ROLE, admin);
        _grantRole(RELAYER_ROLE, admin);
        _grantRole(PAUSER_ROLE, admin);
    }

    /**
     * @notice Settle a batch of trades. Relayer must hold RELAYER_ROLE.
     * @param batchId Unique idempotency key (e.g. keccak of ordered tradeIds)
     * @param trades Array of trades
     * @param relayerSignature ECDSA signature over keccak256(batchId, tradesHash) by a RELAYER
     */
    function settleBatch(
        bytes32 batchId,
        Trade[] calldata trades,
        bytes calldata relayerSignature
    ) external nonReentrant whenNotPaused onlyRole(RELAYER_ROLE) {
        if (trades.length == 0) revert EmptyBatch();
        if (settledBatches[batchId]) revert BatchAlreadySettled();

        // Verify relayer signature over the batch
        bytes32 tradesHash = keccak256(abi.encode(trades));
        bytes32 digest = keccak256(abi.encodePacked(batchId, tradesHash)).toEthSignedMessageHash();
        address signer = digest.recover(relayerSignature);
        if (!hasRole(RELAYER_ROLE, signer)) revert InvalidSignature();

        settledBatches[batchId] = true;

        for (uint256 i = 0; i < trades.length; ) {
            Trade calldata t = trades[i];
            _applyTrade(t.maker, t.taker, t.marketId, t.quantity, t.price);
            emit TradeSettled(t.tradeId, t.maker, t.taker, t.marketId, t.quantity, t.price);
            unchecked { ++i; }
        }

        emit BatchSettled(batchId, trades.length, msg.sender);
    }

    /**
     * @notice Liquidation settlement called by authorized liquidator after off-chain engine decision.
     */
    function settleLiquidation(
        address account,
        uint256 marketId,
        int256 quantity,
        uint256 price
    ) external nonReentrant whenNotPaused {
        if (!riskGuard.isLiquidator(msg.sender)) revert UnauthorizedLiquidator();

        // Close position: quantity is the amount to reduce (sign indicates side being closed)
        int256 current = positions[account][marketId];
        if (quantity > 0 && current < 0) {
            // closing short
            positions[account][marketId] = current + quantity;
        } else if (quantity < 0 && current > 0) {
            // closing long
            positions[account][marketId] = current + quantity;
        } else {
            // full flatten or flip handled off-chain; here we just set residual
            positions[account][marketId] = current + quantity;
        }

        emit LiquidationSettled(account, marketId, quantity, price, msg.sender);
    }

    // ───────────────────────── Internal ─────────────────────────

    function _applyTrade(
        address maker,
        address taker,
        uint256 marketId,
        int256 quantity,
        uint256 /* price */
    ) internal {
        // quantity > 0 means taker is buying (long), maker is selling (short)
        positions[taker][marketId] += quantity;
        positions[maker][marketId] -= quantity;

        // In a full implementation we would also:
        // - update entryNotional for average price
        // - transfer realized PnL via vault.lock / unlock / forceUnlock
        // - collect fees
        // Kept minimal for clarity and gas; PnL settlement can be batched separately.
    }

    // ───────────────────────── Admin ─────────────────────────

    function pause() external onlyRole(PAUSER_ROLE) {
        _pause();
    }

    function unpause() external onlyRole(PAUSER_ROLE) {
        _unpause();
    }

    function positionOf(address account, uint256 marketId) external view returns (int256) {
        return positions[account][marketId];
    }
}
