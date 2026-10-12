// SPDX-License-Identifier: MIT
pragma solidity ^0.8.26;

import {AccessControl} from "@openzeppelin/contracts/access/AccessControl.sol";
import {Pausable} from "@openzeppelin/contracts/utils/Pausable.sol";
import {OracleAdapter} from "./OracleAdapter.sol";

/**
 * @title RiskGuard
 * @notice On-chain risk parameter store and liquidation authorization gate.
 *         Works together with OracleAdapter and VaultManager.
 */
contract RiskGuard is AccessControl, Pausable {
    bytes32 public constant RISK_ADMIN_ROLE = keccak256("RISK_ADMIN_ROLE");
    bytes32 public constant LIQUIDATOR_ROLE = keccak256("LIQUIDATOR_ROLE");
    bytes32 public constant PAUSER_ROLE = keccak256("PAUSER_ROLE");

    OracleAdapter public oracle;

    struct MarketRisk {
        uint256 initialMarginRateBps;      // e.g. 500 = 5%
        uint256 maintenanceMarginRateBps;  // e.g. 250 = 2.5%
        uint256 maxPositionSize;           // absolute qty (scaled)
        uint256 openInterestCap;
        uint256 liquidationPenaltyBps;
        bool listed;
    }

    mapping(uint256 => MarketRisk) public markets;

    event MarketListed(uint256 indexed marketId, uint256 imBps, uint256 mmBps);
    event MarketUpdated(uint256 indexed marketId);
    event OracleUpdated(address indexed oldOracle, address indexed newOracle);

    error MarketNotListed();
    error InvalidRates();
    error ZeroAddress();

    constructor(address oracle_, address admin) {
        if (oracle_ == address(0) || admin == address(0)) revert ZeroAddress();
        oracle = OracleAdapter(oracle_);

        _grantRole(DEFAULT_ADMIN_ROLE, admin);
        _grantRole(RISK_ADMIN_ROLE, admin);
        _grantRole(LIQUIDATOR_ROLE, admin);
        _grantRole(PAUSER_ROLE, admin);
    }

    function listMarket(
        uint256 marketId,
        uint256 imBps,
        uint256 mmBps,
        uint256 maxPositionSize,
        uint256 openInterestCap,
        uint256 liquidationPenaltyBps
    ) external onlyRole(RISK_ADMIN_ROLE) {
        if (imBps == 0 || imBps >= 10_000 || mmBps == 0 || mmBps >= imBps) revert InvalidRates();

        markets[marketId] = MarketRisk({
            initialMarginRateBps: imBps,
            maintenanceMarginRateBps: mmBps,
            maxPositionSize: maxPositionSize,
            openInterestCap: openInterestCap,
            liquidationPenaltyBps: liquidationPenaltyBps,
            listed: true
        });

        emit MarketListed(marketId, imBps, mmBps);
    }

    function updateMarket(
        uint256 marketId,
        uint256 imBps,
        uint256 mmBps,
        uint256 maxPositionSize,
        uint256 openInterestCap,
        uint256 liquidationPenaltyBps
    ) external onlyRole(RISK_ADMIN_ROLE) {
        if (!markets[marketId].listed) revert MarketNotListed();
        if (imBps == 0 || imBps >= 10_000 || mmBps == 0 || mmBps >= imBps) revert InvalidRates();

        markets[marketId] = MarketRisk({
            initialMarginRateBps: imBps,
            maintenanceMarginRateBps: mmBps,
            maxPositionSize: maxPositionSize,
            openInterestCap: openInterestCap,
            liquidationPenaltyBps: liquidationPenaltyBps,
            listed: true
        });

        emit MarketUpdated(marketId);
    }

    function getMarkPrice(uint256 marketId) external view returns (uint256 price, uint256 timestamp) {
        if (!markets[marketId].listed) revert MarketNotListed();
        return oracle.getPrice(marketId);
    }

    function isLiquidator(address account) external view returns (bool) {
        return hasRole(LIQUIDATOR_ROLE, account);
    }

    function setOracle(address newOracle) external onlyRole(DEFAULT_ADMIN_ROLE) {
        if (newOracle == address(0)) revert ZeroAddress();
        address old = address(oracle);
        oracle = OracleAdapter(newOracle);
        emit OracleUpdated(old, newOracle);
    }

    function pause() external onlyRole(PAUSER_ROLE) {
        _pause();
    }

    function unpause() external onlyRole(PAUSER_ROLE) {
        _unpause();
    }
}
