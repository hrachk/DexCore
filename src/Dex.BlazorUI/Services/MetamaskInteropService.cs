using Microsoft.JSInterop;

namespace Dex.BlazorUI.Services;

/// <summary>
/// JSInterop wrapper for MetaMask / injected EIP-1193 providers.
/// Handles connect, chain switch, personal_sign and EIP-712 typed data signing.
/// </summary>
public sealed class MetamaskInteropService
{
    private readonly IJSRuntime _js;
    private readonly ILogger<MetamaskInteropService> _logger;

    public string? Account { get; private set; }
    public long? ChainId { get; private set; }
    public bool IsConnected => !string.IsNullOrEmpty(Account);

    public event Action? StateChanged;

    public MetamaskInteropService(IJSRuntime js, ILogger<MetamaskInteropService> logger)
    {
        _js = js;
        _logger = logger;
    }

    public async Task<bool> IsMetaMaskAvailableAsync()
    {
        try
        {
            return await _js.InvokeAsync<bool>("dexWeb3.isMetaMaskAvailable");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MetaMask probe failed");
            return false;
        }
    }

    public async Task<string?> ConnectAsync()
    {
        try
        {
            var result = await _js.InvokeAsync<ConnectResult>("dexWeb3.connect");
            Account = result?.Account;
            ChainId = result?.ChainId;
            StateChanged?.Invoke();
            return Account;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Wallet connect failed");
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        Account = null;
        ChainId = null;
        StateChanged?.Invoke();
        await Task.CompletedTask;
    }

    public async Task SwitchChainAsync(long chainId)
    {
        await _js.InvokeVoidAsync("dexWeb3.switchChain", $"0x{chainId:X}");
        ChainId = chainId;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Signs EIP-712 typed data for relayer batch / order authorization.
    /// </summary>
    public async Task<string> SignTypedDataAsync(string typedDataJson)
    {
        if (string.IsNullOrEmpty(Account))
            throw new InvalidOperationException("Wallet not connected.");
        return await _js.InvokeAsync<string>("dexWeb3.signTypedData", Account, typedDataJson);
    }

    public async Task<string> PersonalSignAsync(string message)
    {
        if (string.IsNullOrEmpty(Account))
            throw new InvalidOperationException("Wallet not connected.");
        return await _js.InvokeAsync<string>("dexWeb3.personalSign", Account, message);
    }

    /// <summary>
    /// Sends native or ERC-20 deposit tx to VaultManager (value for ETH vault).
    /// </summary>
    public async Task<string> SendDepositAsync(string vaultAddress, string amountWeiHex)
    {
        if (string.IsNullOrEmpty(Account))
            throw new InvalidOperationException("Wallet not connected.");
        return await _js.InvokeAsync<string>("dexWeb3.sendDeposit", Account, vaultAddress, amountWeiHex);
    }

    private sealed class ConnectResult
    {
        public string? Account { get; set; }
        public long ChainId { get; set; }
    }
}
