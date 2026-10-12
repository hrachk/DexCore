// SPDX-License-Identifier: MIT
pragma solidity ^0.8.26;

import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import {ReentrancyGuard} from "@openzeppelin/contracts/utils/ReentrancyGuard.sol";
import {Pausable} from "@openzeppelin/contracts/utils/Pausable.sol";
import {AccessControl} from "@openzeppelin/contracts/access/AccessControl.sol";
import {ComplianceHook} from "./ComplianceHook.sol";

/**
 * @title VaultManagerAdvanced
 * @notice Multi-asset aware vault with:
 *         - ComplianceHook integration (KYC/AML/ZK gate)
 *         - Anti-bank-run rate limiting on withdrawals (rolling window)
 *         - Emergency timelocked unpause (multisig / social recovery pattern)
 *         - Per-asset risk pool isolation (simple ERC-20 registry)
 *
 *         Builds on the same non-custodial model as VaultManager.
 */
contract VaultManagerAdvanced is ReentrancyGuard, Pausable, AccessControl {
    using SafeERC20 for IERC20;

    bytes32 public constant RELAYER_ROLE = keccak256("RELAYER_ROLE");
    bytes32 public constant LIQUIDATOR_ROLE = keccak256("LIQUIDATOR_ROLE");
    bytes32 public constant PAUSER_ROLE = keccak256("PAUSER_ROLE");
    bytes32 public constant GUARDIAN_ROLE = keccak256("GUARDIAN_ROLE"); // social recovery / multisig

    ComplianceHook public complianceHook;

    /// @notice token => account => balance
    mapping(address => mapping(address => uint256)) public balances;
    mapping(address => mapping(address => uint256)) public locked;

    /// @notice Supported collateral tokens (address(0) = native ETH)
    mapping(address => bool) public supportedAssets;
    address[] public assetList;

    // ── Anti-bank-run ──
    uint256 public maxWithdrawPerWindow;          // global notional cap per window
    uint256 public withdrawWindowSeconds;         // e.g. 1 hours
    uint256 public windowStart;
    uint256 public windowWithdrawn;

    // ── Emergency unpause timelock ──
    uint256 public unpauseEta;                    // 0 = no pending
    uint256 public unpauseDelay = 24 hours;

    struct WithdrawalRequest {
        address token;
        uint256 amount;
        uint256 availableAt;
        bool executed;
    }
    mapping(address => WithdrawalRequest) public withdrawalRequests;
    uint256 public withdrawalDelay;

    event AssetAdded(address indexed token);
    event Deposit(address indexed account, address indexed token, uint256 amount);
    event WithdrawalRequested(address indexed account, address indexed token, uint256 amount, uint256 availableAt);
    event WithdrawalExecuted(address indexed account, address indexed token, uint256 amount);
    event BankRunCircuitTripped(uint256 windowWithdrawn, uint256 cap);
    event UnpauseQueued(uint256 eta);
    event UnpauseExecuted();
    event ComplianceHookUpdated(address indexed hook);

    error UnsupportedAsset();
    error ZeroAmount();
    error InsufficientBalance();
    error InsufficientUnlocked();
    error WithdrawalPending();
    error NoWithdrawalRequest();
    error WithdrawalNotReady();
    error BankRunLimitExceeded();
    error UnpauseNotQueued();
    error UnpauseNotReady();
    error TransferFailed();
    error ZeroAddress();

    constructor(
        address complianceHook_,
        uint256 withdrawalDelay_,
        uint256 maxWithdrawPerWindow_,
        uint256 withdrawWindowSeconds_,
        address admin
    ) {
        if (admin == address(0)) revert ZeroAddress();
        complianceHook = ComplianceHook(complianceHook_);
        withdrawalDelay = withdrawalDelay_;
        maxWithdrawPerWindow = maxWithdrawPerWindow_;
        withdrawWindowSeconds = withdrawWindowSeconds_;
        windowStart = block.timestamp;

        _grantRole(DEFAULT_ADMIN_ROLE, admin);
        _grantRole(PAUSER_ROLE, admin);
        _grantRole(RELAYER_ROLE, admin);
        _grantRole(LIQUIDATOR_ROLE, admin);
        _grantRole(GUARDIAN_ROLE, admin);

        // Native ETH supported by default
        supportedAssets[address(0)] = true;
        assetList.push(address(0));
    }

    function addAsset(address token) external onlyRole(DEFAULT_ADMIN_ROLE) {
        if (supportedAssets[token]) return;
        supportedAssets[token] = true;
        assetList.push(token);
        emit AssetAdded(token);
    }

    // ───────────────────────── Deposit ─────────────────────────

    function deposit(
        address token,
        uint256 amount,
        bytes32 commitment,
        bytes32 nullifier,
        bytes calldata proof,
        uint256[] calldata publicInputs
    ) external payable nonReentrant whenNotPaused {
        if (!supportedAssets[token]) revert UnsupportedAsset();

        if (token == address(0)) {
            if (msg.value == 0) revert ZeroAmount();
            amount = msg.value;
        } else {
            if (amount == 0) revert ZeroAmount();
            if (msg.value != 0) revert TransferFailed();
            IERC20(token).safeTransferFrom(msg.sender, address(this), amount);
        }

        // Compliance gate
        if (address(complianceHook) != address(0)) {
            complianceHook.beforeDepositOrTrade(msg.sender, amount, commitment, nullifier, proof, publicInputs);
        }

        balances[token][msg.sender] += amount;
        emit Deposit(msg.sender, token, amount);
    }

    // ───────────────────────── Withdrawal ─────────────────────────

    function requestWithdrawal(address token, uint256 amount) external nonReentrant whenNotPaused {
        if (!supportedAssets[token]) revert UnsupportedAsset();
        if (amount == 0) revert ZeroAmount();

        uint256 available = balances[token][msg.sender] - locked[token][msg.sender];
        if (amount > available) revert InsufficientUnlocked();

        WithdrawalRequest storage req = withdrawalRequests[msg.sender];
        if (req.amount > 0 && !req.executed) revert WithdrawalPending();

        // Anti-bank-run check
        _consumeWithdrawWindow(amount);

        req.token = token;
        req.amount = amount;
        req.availableAt = block.timestamp + withdrawalDelay;
        req.executed = false;
        locked[token][msg.sender] += amount;

        emit WithdrawalRequested(msg.sender, token, amount, req.availableAt);
    }

    function executeWithdrawal(
        bytes32 commitment,
        bytes32 nullifier,
        bytes calldata proof,
        uint256[] calldata publicInputs
    ) external nonReentrant whenNotPaused {
        WithdrawalRequest storage req = withdrawalRequests[msg.sender];
        if (req.amount == 0) revert NoWithdrawalRequest();
        if (req.executed) revert NoWithdrawalRequest();
        if (block.timestamp < req.availableAt) revert WithdrawalNotReady();

        if (address(complianceHook) != address(0)) {
            complianceHook.beforeWithdraw(msg.sender, req.amount, commitment, nullifier, proof, publicInputs);
        }

        uint256 amount = req.amount;
        address token = req.token;
        req.executed = true;
        req.amount = 0;

        locked[token][msg.sender] -= amount;
        balances[token][msg.sender] -= amount;
        _transferOut(token, msg.sender, amount);

        emit WithdrawalExecuted(msg.sender, token, amount);
    }

    // ───────────────────────── Lock / Unlock ─────────────────────────

    function lock(address token, address account, uint256 amount) external onlyRole(RELAYER_ROLE) {
        if (amount == 0) revert ZeroAmount();
        uint256 available = balances[token][account] - locked[token][account];
        if (amount > available) revert InsufficientUnlocked();
        locked[token][account] += amount;
    }

    function unlock(address token, address account, uint256 amount) external onlyRole(RELAYER_ROLE) {
        if (amount == 0) revert ZeroAmount();
        if (amount > locked[token][account]) revert InsufficientBalance();
        locked[token][account] -= amount;
    }

    // ───────────────────────── Anti-bank-run ─────────────────────────

    function _consumeWithdrawWindow(uint256 amount) internal {
        if (block.timestamp >= windowStart + withdrawWindowSeconds) {
            windowStart = block.timestamp;
            windowWithdrawn = 0;
        }
        if (windowWithdrawn + amount > maxWithdrawPerWindow) {
            emit BankRunCircuitTripped(windowWithdrawn + amount, maxWithdrawPerWindow);
            revert BankRunLimitExceeded();
        }
        windowWithdrawn += amount;
    }

    function setBankRunParams(uint256 cap, uint256 windowSecs) external onlyRole(DEFAULT_ADMIN_ROLE) {
        maxWithdrawPerWindow = cap;
        withdrawWindowSeconds = windowSecs;
    }

    // ───────────────────────── Emergency unpause (timelock + guardian) ─────────────────────────

    function queueUnpause() external onlyRole(GUARDIAN_ROLE) {
        unpauseEta = block.timestamp + unpauseDelay;
        emit UnpauseQueued(unpauseEta);
    }

    function executeUnpause() external onlyRole(GUARDIAN_ROLE) {
        if (unpauseEta == 0) revert UnpauseNotQueued();
        if (block.timestamp < unpauseEta) revert UnpauseNotReady();
        unpauseEta = 0;
        _unpause();
        emit UnpauseExecuted();
    }

    function pause() external onlyRole(PAUSER_ROLE) {
        _pause();
    }

    // Direct unpause only by admin (emergency override); normal path uses timelock
    function forceUnpause() external onlyRole(DEFAULT_ADMIN_ROLE) {
        unpauseEta = 0;
        _unpause();
    }

    function setComplianceHook(address hook) external onlyRole(DEFAULT_ADMIN_ROLE) {
        complianceHook = ComplianceHook(hook);
        emit ComplianceHookUpdated(hook);
    }

    // ───────────────────────── Internal ─────────────────────────

    function _transferOut(address token, address to, uint256 amount) internal {
        if (token == address(0)) {
            (bool ok, ) = to.call{value: amount}("");
            if (!ok) revert TransferFailed();
        } else {
            IERC20(token).safeTransfer(to, amount);
        }
    }

    function availableBalance(address token, address account) external view returns (uint256) {
        return balances[token][account] - locked[token][account];
    }

    receive() external payable {
        if (!supportedAssets[address(0)]) revert UnsupportedAsset();
    }
}
