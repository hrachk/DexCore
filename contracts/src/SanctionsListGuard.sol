// SPDX-License-Identifier: MIT
pragma solidity ^0.8.26;

import {AccessControl} from "@openzeppelin/contracts/access/AccessControl.sol";
import {Pausable} from "@openzeppelin/contracts/utils/Pausable.sol";

/**
 * @title SanctionsListGuard
 * @notice On-chain sanctions / high-risk address registry.
 *         Fed by an authorized Oracle / Compliance Operator (Chainalysis / Elliptic style feeds).
 *         Provides circuit-breaker style freezes for blocked addresses.
 */
contract SanctionsListGuard is AccessControl, Pausable {
    bytes32 public constant SANCTIONS_ORACLE_ROLE = keccak256("SANCTIONS_ORACLE_ROLE");
    bytes32 public constant PAUSER_ROLE = keccak256("PAUSER_ROLE");

    enum RiskLevel {
        Clear,          // 0
        Elevated,       // 1
        High,           // 2
        Blocked         // 3 — freeze
    }

    struct AddressRisk {
        RiskLevel level;
        uint64 updatedAt;
        bytes32 reasonCode; // opaque reference to off-chain case id
    }

    mapping(address => AddressRisk) public risks;

    /// @notice Global freeze switch (extreme event)
    bool public globalFreeze;

    event RiskUpdated(address indexed account, RiskLevel level, bytes32 reasonCode);
    event GlobalFreezeSet(bool frozen);
    event AddressFrozen(address indexed account, bytes32 reasonCode);

    error AddressBlocked();
    error GlobalFreezeActive();
    error ZeroAddress();

    constructor(address admin) {
        if (admin == address(0)) revert ZeroAddress();
        _grantRole(DEFAULT_ADMIN_ROLE, admin);
        _grantRole(SANCTIONS_ORACLE_ROLE, admin);
        _grantRole(PAUSER_ROLE, admin);
    }

    function setRisk(
        address account,
        RiskLevel level,
        bytes32 reasonCode
    ) external onlyRole(SANCTIONS_ORACLE_ROLE) {
        if (account == address(0)) revert ZeroAddress();
        risks[account] = AddressRisk({
            level: level,
            updatedAt: uint64(block.timestamp),
            reasonCode: reasonCode
        });
        emit RiskUpdated(account, level, reasonCode);
        if (level == RiskLevel.Blocked) {
            emit AddressFrozen(account, reasonCode);
        }
    }

    function setRiskBatch(
        address[] calldata accounts,
        RiskLevel[] calldata levels,
        bytes32[] calldata reasonCodes
    ) external onlyRole(SANCTIONS_ORACLE_ROLE) {
        require(accounts.length == levels.length && levels.length == reasonCodes.length, "length mismatch");
        for (uint256 i = 0; i < accounts.length; ) {
            if (accounts[i] != address(0)) {
                risks[accounts[i]] = AddressRisk({
                    level: levels[i],
                    updatedAt: uint64(block.timestamp),
                    reasonCode: reasonCodes[i]
                });
                emit RiskUpdated(accounts[i], levels[i], reasonCodes[i]);
            }
            unchecked { ++i; }
        }
    }

    function setGlobalFreeze(bool frozen) external onlyRole(DEFAULT_ADMIN_ROLE) {
        globalFreeze = frozen;
        emit GlobalFreezeSet(frozen);
    }

    /**
     * @notice Reverts if the address is blocked or global freeze is on.
     *         Called by VaultManager / ComplianceHook before withdrawals and transfers.
     */
    function requireClear(address account) external view {
        if (globalFreeze) revert GlobalFreezeActive();
        if (risks[account].level == RiskLevel.Blocked) revert AddressBlocked();
    }

    function isBlocked(address account) external view returns (bool) {
        return globalFreeze || risks[account].level == RiskLevel.Blocked;
    }

    function getRisk(address account) external view returns (RiskLevel level, uint64 updatedAt, bytes32 reasonCode) {
        AddressRisk memory r = risks[account];
        return (r.level, r.updatedAt, r.reasonCode);
    }

    function pause() external onlyRole(PAUSER_ROLE) {
        _pause();
    }

    function unpause() external onlyRole(PAUSER_ROLE) {
        _unpause();
    }
}
