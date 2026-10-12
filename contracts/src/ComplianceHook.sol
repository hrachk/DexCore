// SPDX-License-Identifier: MIT
pragma solidity ^0.8.26;

import {AccessControl} from "@openzeppelin/contracts/access/AccessControl.sol";
import {Pausable} from "@openzeppelin/contracts/utils/Pausable.sol";
import {ZKCredentialVerifier} from "./ZKCredentialVerifier.sol";
import {SanctionsListGuard} from "./SanctionsListGuard.sol";

/**
 * @title ComplianceHook
 * @notice Uniswap-v4-style compliance gate used before deposits, withdrawals and settlements.
 *         Checks:
 *           1. Sanctions / AML blocklist (SanctionsListGuard)
 *           2. Optional ZK institutional credential (ZKCredentialVerifier)
 *           3. Configurable per-market / per-tier rules (max notional without KYC, etc.)
 */
contract ComplianceHook is AccessControl, Pausable {
    bytes32 public constant COMPLIANCE_ADMIN_ROLE = keccak256("COMPLIANCE_ADMIN_ROLE");
    bytes32 public constant PAUSER_ROLE = keccak256("PAUSER_ROLE");

    ZKCredentialVerifier public credentialVerifier;
    SanctionsListGuard public sanctionsGuard;

    /// @notice If true, every participant must present a valid ZK attestation
    bool public requireZkCredential;

    /// @notice Max notional (1e18 scaled) allowed without ZK credential when requireZkCredential=false
    uint256 public maxNotionalWithoutKyc;

    /// @notice Tier thresholds: commitment => tier (1 = retail screened, 2 = institutional, …)
    mapping(bytes32 => uint8) public credentialTier;

    event CredentialVerifierUpdated(address indexed oldAddr, address indexed newAddr);
    event SanctionsGuardUpdated(address indexed oldAddr, address indexed newAddr);
    event PolicyUpdated(bool requireZk, uint256 maxNotionalWithoutKyc);
    event TierAssigned(bytes32 indexed commitment, uint8 tier);

    error ComplianceCheckFailed(string reason);
    error ZeroAddress();

    constructor(
        address credentialVerifier_,
        address sanctionsGuard_,
        address admin,
        bool requireZkCredential_,
        uint256 maxNotionalWithoutKyc_
    ) {
        if (credentialVerifier_ == address(0) || sanctionsGuard_ == address(0) || admin == address(0)) {
            revert ZeroAddress();
        }
        credentialVerifier = ZKCredentialVerifier(credentialVerifier_);
        sanctionsGuard = SanctionsListGuard(sanctionsGuard_);
        requireZkCredential = requireZkCredential_;
        maxNotionalWithoutKyc = maxNotionalWithoutKyc_;

        _grantRole(DEFAULT_ADMIN_ROLE, admin);
        _grantRole(COMPLIANCE_ADMIN_ROLE, admin);
        _grantRole(PAUSER_ROLE, admin);
    }

    /**
     * @notice Pre-deposit / pre-trade compliance gate.
     * @param account User address
     * @param notional Notional value of the operation (1e18)
     * @param commitment Optional ZK commitment (bytes32(0) if none)
     * @param nullifier Optional nullifier for the proof
     * @param proof ZK proof bytes (empty if not required)
     * @param publicInputs Circuit public inputs
     */
    function beforeDepositOrTrade(
        address account,
        uint256 notional,
        bytes32 commitment,
        bytes32 nullifier,
        bytes calldata proof,
        uint256[] calldata publicInputs
    ) external whenNotPaused {
        // 1. Sanctions
        sanctionsGuard.requireClear(account);

        // 2. ZK credential policy
        if (requireZkCredential) {
            if (commitment == bytes32(0) || proof.length == 0) {
                revert ComplianceCheckFailed("ZK credential required");
            }
            bool ok = credentialVerifier.verify(commitment, nullifier, proof, publicInputs);
            if (!ok) revert ComplianceCheckFailed("ZK verification failed");
        } else if (notional > maxNotionalWithoutKyc) {
            if (commitment == bytes32(0) || proof.length == 0) {
                revert ComplianceCheckFailed("Notional exceeds KYC-free limit");
            }
            bool ok = credentialVerifier.verify(commitment, nullifier, proof, publicInputs);
            if (!ok) revert ComplianceCheckFailed("ZK verification failed");
        }
    }

    /**
     * @notice Pre-withdrawal gate (sanctions only by default; can require ZK for large amounts).
     */
    function beforeWithdraw(
        address account,
        uint256 amount,
        bytes32 commitment,
        bytes32 nullifier,
        bytes calldata proof,
        uint256[] calldata publicInputs
    ) external whenNotPaused {
        sanctionsGuard.requireClear(account);

        if (amount > maxNotionalWithoutKyc && requireZkCredential) {
            if (commitment == bytes32(0) || proof.length == 0) {
                revert ComplianceCheckFailed("ZK credential required for large withdrawal");
            }
            bool ok = credentialVerifier.verify(commitment, nullifier, proof, publicInputs);
            if (!ok) revert ComplianceCheckFailed("ZK verification failed");
        }
    }

    // ───────────────────────── Admin ─────────────────────────

    function setPolicy(bool requireZk, uint256 maxNotional) external onlyRole(COMPLIANCE_ADMIN_ROLE) {
        requireZkCredential = requireZk;
        maxNotionalWithoutKyc = maxNotional;
        emit PolicyUpdated(requireZk, maxNotional);
    }

    function setCredentialVerifier(address newVerifier) external onlyRole(COMPLIANCE_ADMIN_ROLE) {
        if (newVerifier == address(0)) revert ZeroAddress();
        address old = address(credentialVerifier);
        credentialVerifier = ZKCredentialVerifier(newVerifier);
        emit CredentialVerifierUpdated(old, newVerifier);
    }

    function setSanctionsGuard(address newGuard) external onlyRole(COMPLIANCE_ADMIN_ROLE) {
        if (newGuard == address(0)) revert ZeroAddress();
        address old = address(sanctionsGuard);
        sanctionsGuard = SanctionsListGuard(newGuard);
        emit SanctionsGuardUpdated(old, newGuard);
    }

    function assignTier(bytes32 commitment, uint8 tier) external onlyRole(COMPLIANCE_ADMIN_ROLE) {
        credentialTier[commitment] = tier;
        emit TierAssigned(commitment, tier);
    }

    function pause() external onlyRole(PAUSER_ROLE) {
        _pause();
    }

    function unpause() external onlyRole(PAUSER_ROLE) {
        _unpause();
    }
}
