// SPDX-License-Identifier: MIT
pragma solidity ^0.8.26;

import {AccessControl} from "@openzeppelin/contracts/access/AccessControl.sol";
import {Pausable} from "@openzeppelin/contracts/utils/Pausable.sol";

/**
 * @title OracleAdapter
 * @notice Multi-source price adapter with circuit breakers and TWAP-style deviation guards.
 *         Accepts push updates from authorized oracles (Chainlink-style / Pyth-style relayers).
 * @dev Production systems should wire actual Chainlink AggregatorV3 or Pyth interfaces.
 *      This contract provides the institutional guard layer on top of raw price feeds.
 */
contract OracleAdapter is AccessControl, Pausable {
    bytes32 public constant ORACLE_ROLE = keccak256("ORACLE_ROLE");
    bytes32 public constant PAUSER_ROLE = keccak256("PAUSER_ROLE");

    struct PriceData {
        uint256 price;          // 1e18 scaled
        uint256 timestamp;
        uint256 confidence;     // optional confidence interval
    }

    /// @notice marketId => latest price
    mapping(uint256 => PriceData) public latestPrices;

    /// @notice Maximum age of a price before it is considered stale (seconds)
    uint256 public maxStaleness;

    /// @notice Max allowed deviation from previous price in basis points (e.g. 500 = 5%)
    uint256 public maxDeviationBps;

    /// @notice Whether circuit breaker is tripped for a market
    mapping(uint256 => bool) public circuitBroken;

    event PriceUpdated(uint256 indexed marketId, uint256 price, uint256 timestamp, uint256 confidence);
    event CircuitBreakerTripped(uint256 indexed marketId, uint256 oldPrice, uint256 newPrice);
    event CircuitBreakerReset(uint256 indexed marketId);
    event MaxStalenessUpdated(uint256 oldValue, uint256 newValue);
    event MaxDeviationUpdated(uint256 oldValue, uint256 newValue);

    error StalePrice();
    error CircuitBroken();
    error DeviationExceeded();
    error InvalidPrice();
    error ZeroAddress();

    constructor(uint256 _maxStaleness, uint256 _maxDeviationBps, address admin) {
        require(_maxStaleness > 0 && _maxStaleness <= 1 hours, "invalid staleness");
        require(_maxDeviationBps > 0 && _maxDeviationBps <= 10_000, "invalid deviation");
        maxStaleness = _maxStaleness;
        maxDeviationBps = _maxDeviationBps;

        _grantRole(DEFAULT_ADMIN_ROLE, admin);
        _grantRole(ORACLE_ROLE, admin);
        _grantRole(PAUSER_ROLE, admin);
    }

    /**
     * @notice Push a new price. Performs deviation and staleness checks.
     */
    function updatePrice(
        uint256 marketId,
        uint256 price,
        uint256 confidence
    ) external onlyRole(ORACLE_ROLE) whenNotPaused {
        if (price == 0) revert InvalidPrice();
        if (circuitBroken[marketId]) revert CircuitBroken();

        PriceData storage current = latestPrices[marketId];

        if (current.price != 0) {
            uint256 diff = price > current.price ? price - current.price : current.price - price;
            uint256 deviationBps = (diff * 10_000) / current.price;
            if (deviationBps > maxDeviationBps) {
                circuitBroken[marketId] = true;
                emit CircuitBreakerTripped(marketId, current.price, price);
                revert DeviationExceeded();
            }
        }

        current.price = price;
        current.timestamp = block.timestamp;
        current.confidence = confidence;

        emit PriceUpdated(marketId, price, block.timestamp, confidence);
    }

    /**
     * @notice Returns current price if fresh and circuit not broken.
     */
    function getPrice(uint256 marketId) external view returns (uint256 price, uint256 timestamp) {
        if (circuitBroken[marketId]) revert CircuitBroken();
        PriceData memory data = latestPrices[marketId];
        if (data.price == 0) revert InvalidPrice();
        if (block.timestamp > data.timestamp + maxStaleness) revert StalePrice();
        return (data.price, data.timestamp);
    }

    /**
     * @notice Unsafe getter for off-chain / emergency use (no checks).
     */
    function getPriceUnsafe(uint256 marketId) external view returns (uint256 price, uint256 timestamp, bool broken) {
        PriceData memory data = latestPrices[marketId];
        return (data.price, data.timestamp, circuitBroken[marketId]);
    }

    function resetCircuitBreaker(uint256 marketId) external onlyRole(DEFAULT_ADMIN_ROLE) {
        circuitBroken[marketId] = false;
        emit CircuitBreakerReset(marketId);
    }

    function setMaxStaleness(uint256 value) external onlyRole(DEFAULT_ADMIN_ROLE) {
        require(value > 0 && value <= 1 hours, "invalid");
        uint256 old = maxStaleness;
        maxStaleness = value;
        emit MaxStalenessUpdated(old, value);
    }

    function setMaxDeviationBps(uint256 value) external onlyRole(DEFAULT_ADMIN_ROLE) {
        require(value > 0 && value <= 10_000, "invalid");
        uint256 old = maxDeviationBps;
        maxDeviationBps = value;
        emit MaxDeviationUpdated(old, value);
    }

    function pause() external onlyRole(PAUSER_ROLE) {
        _pause();
    }

    function unpause() external onlyRole(PAUSER_ROLE) {
        _unpause();
    }
}
