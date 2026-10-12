// SPDX-License-Identifier: MIT
pragma solidity ^0.8.26;

import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import {ReentrancyGuard} from "@openzeppelin/contracts/utils/ReentrancyGuard.sol";
import {Pausable} from "@openzeppelin/contracts/utils/Pausable.sol";
import {AccessControl} from "@openzeppelin/contracts/access/AccessControl.sol";

/**
 * @title VaultManager
 * @notice Non-custodial deposit vault for institutional DEX collateral.
 *         Supports ERC-20 and native ETH. Withdrawals go through a timelocked request
 *         to allow risk/liquidation systems to react. Emergency pause available.
 * @dev Not a full ERC-4626 share token; balances are tracked 1:1 in underlying.
 *      Designed for integration with DexSettlement and RiskEngine.
 */
contract VaultManager is ReentrancyGuard, Pausable, AccessControl {
    using SafeERC20 for IERC20;

    bytes32 public constant RELAYER_ROLE = keccak256("RELAYER_ROLE");
    bytes32 public constant LIQUIDATOR_ROLE = keccak256("LIQUIDATOR_ROLE");
    bytes32 public constant PAUSER_ROLE = keccak256("PAUSER_ROLE");

    /// @notice Collateral token (address(0) = native ETH)
    address public immutable collateralToken;

    /// @notice Withdrawal delay in seconds (e.g. 1 hour = 3600)
    uint256 public withdrawalDelay;

    /// @notice Account => available balance (settled deposits minus locked / withdrawn)
    mapping(address => uint256) public balances;

    /// @notice Account => locked balance (used by settlement / open positions)
    mapping(address => uint256) public locked;

    struct WithdrawalRequest {
        uint256 amount;
        uint256 availableAt;
        bool executed;
    }

    /// @notice Account => current pending withdrawal (single request for simplicity)
    mapping(address => WithdrawalRequest) public withdrawalRequests;

    event Deposit(address indexed account, uint256 amount, uint256 newBalance);
    event WithdrawalRequested(address indexed account, uint256 amount, uint256 availableAt);
    event WithdrawalExecuted(address indexed account, uint256 amount, uint256 newBalance);
    event WithdrawalCancelled(address indexed account, uint256 amount);
    event Locked(address indexed account, uint256 amount, uint256 newLocked);
    event Unlocked(address indexed account, uint256 amount, uint256 newLocked);
    event ForceUnlock(address indexed account, uint256 amount, address indexed liquidator);
    event WithdrawalDelayUpdated(uint256 oldDelay, uint256 newDelay);

    error ZeroAmount();
    error InsufficientBalance();
    error InsufficientUnlocked();
    error WithdrawalPending();
    error NoWithdrawalRequest();
    error WithdrawalNotReady();
    error WithdrawalAlreadyExecuted();
    error TransferFailed();
    error InvalidDelay();

    constructor(address _collateralToken, uint256 _withdrawalDelay, address admin) {
        if (_withdrawalDelay == 0 || _withdrawalDelay > 7 days) revert InvalidDelay();
        collateralToken = _collateralToken;
        withdrawalDelay = _withdrawalDelay;

        _grantRole(DEFAULT_ADMIN_ROLE, admin);
        _grantRole(PAUSER_ROLE, admin);
        _grantRole(RELAYER_ROLE, admin);
        _grantRole(LIQUIDATOR_ROLE, admin);
    }

    // ───────────────────────── Deposit ─────────────────────────

    function deposit(uint256 amount) external payable nonReentrant whenNotPaused {
        if (collateralToken == address(0)) {
            if (msg.value == 0) revert ZeroAmount();
            amount = msg.value;
        } else {
            if (amount == 0) revert ZeroAmount();
            if (msg.value != 0) revert TransferFailed();
            IERC20(collateralToken).safeTransferFrom(msg.sender, address(this), amount);
        }

        balances[msg.sender] += amount;
        emit Deposit(msg.sender, amount, balances[msg.sender]);
    }

    // ───────────────────────── Withdrawal (timelocked) ─────────────────────────

    function requestWithdrawal(uint256 amount) external nonReentrant whenNotPaused {
        if (amount == 0) revert ZeroAmount();
        uint256 available = balances[msg.sender] - locked[msg.sender];
        if (amount > available) revert InsufficientUnlocked();

        WithdrawalRequest storage req = withdrawalRequests[msg.sender];
        if (req.amount > 0 && !req.executed) revert WithdrawalPending();

        req.amount = amount;
        req.availableAt = block.timestamp + withdrawalDelay;
        req.executed = false;

        // Lock the amount so it cannot be used for trading while pending
        locked[msg.sender] += amount;

        emit WithdrawalRequested(msg.sender, amount, req.availableAt);
    }

    function executeWithdrawal() external nonReentrant whenNotPaused {
        WithdrawalRequest storage req = withdrawalRequests[msg.sender];
        if (req.amount == 0) revert NoWithdrawalRequest();
        if (req.executed) revert WithdrawalAlreadyExecuted();
        if (block.timestamp < req.availableAt) revert WithdrawalNotReady();

        uint256 amount = req.amount;
        req.executed = true;
        req.amount = 0;

        locked[msg.sender] -= amount;
        balances[msg.sender] -= amount;

        _transferOut(msg.sender, amount);
        emit WithdrawalExecuted(msg.sender, amount, balances[msg.sender]);
    }

    function cancelWithdrawal() external nonReentrant {
        WithdrawalRequest storage req = withdrawalRequests[msg.sender];
        if (req.amount == 0 || req.executed) revert NoWithdrawalRequest();

        uint256 amount = req.amount;
        req.amount = 0;
        req.executed = true; // mark consumed

        locked[msg.sender] -= amount;
        emit WithdrawalCancelled(msg.sender, amount);
    }

    // ───────────────────────── Lock / Unlock (Relayer / Settlement) ─────────────────────────

    function lock(address account, uint256 amount) external onlyRole(RELAYER_ROLE) {
        if (amount == 0) revert ZeroAmount();
        uint256 available = balances[account] - locked[account];
        if (amount > available) revert InsufficientUnlocked();
        locked[account] += amount;
        emit Locked(account, amount, locked[account]);
    }

    function unlock(address account, uint256 amount) external onlyRole(RELAYER_ROLE) {
        if (amount == 0) revert ZeroAmount();
        if (amount > locked[account]) revert InsufficientBalance();
        locked[account] -= amount;
        emit Unlocked(account, amount, locked[account]);
    }

    /// @notice Liquidator can force-unlock and transfer residual to insurance / settlement
    function forceUnlockAndTransfer(
        address account,
        uint256 amount,
        address recipient
    ) external onlyRole(LIQUIDATOR_ROLE) nonReentrant {
        if (amount == 0) revert ZeroAmount();
        if (amount > balances[account]) revert InsufficientBalance();

        if (amount <= locked[account]) {
            locked[account] -= amount;
        } else {
            locked[account] = 0;
        }
        balances[account] -= amount;

        _transferOut(recipient, amount);
        emit ForceUnlock(account, amount, msg.sender);
    }

    // ───────────────────────── Admin ─────────────────────────

    function setWithdrawalDelay(uint256 newDelay) external onlyRole(DEFAULT_ADMIN_ROLE) {
        if (newDelay == 0 || newDelay > 7 days) revert InvalidDelay();
        uint256 old = withdrawalDelay;
        withdrawalDelay = newDelay;
        emit WithdrawalDelayUpdated(old, newDelay);
    }

    function pause() external onlyRole(PAUSER_ROLE) {
        _pause();
    }

    function unpause() external onlyRole(PAUSER_ROLE) {
        _unpause();
    }

    // ───────────────────────── Views ─────────────────────────

    function availableBalance(address account) external view returns (uint256) {
        return balances[account] - locked[account];
    }

    // ───────────────────────── Internal ─────────────────────────

    function _transferOut(address to, uint256 amount) internal {
        if (collateralToken == address(0)) {
            (bool ok, ) = to.call{value: amount}("");
            if (!ok) revert TransferFailed();
        } else {
            IERC20(collateralToken).safeTransfer(to, amount);
        }
    }

    receive() external payable {
        // Only accept native if this vault is configured for ETH
        if (collateralToken != address(0)) revert TransferFailed();
    }
}
