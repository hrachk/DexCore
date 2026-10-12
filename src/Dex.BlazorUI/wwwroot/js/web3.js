window.dexWeb3 = {
  isMetaMaskAvailable: function () {
    return typeof window.ethereum !== "undefined";
  },

  connect: async function () {
    if (!window.ethereum) throw new Error("No EIP-1193 provider");
    const accounts = await window.ethereum.request({ method: "eth_requestAccounts" });
    const chainIdHex = await window.ethereum.request({ method: "eth_chainId" });
    return {
      account: accounts[0],
      chainId: parseInt(chainIdHex, 16)
    };
  },

  switchChain: async function (chainIdHex) {
    try {
      await window.ethereum.request({
        method: "wallet_switchEthereumChain",
        params: [{ chainId: chainIdHex }]
      });
    } catch (err) {
      if (err.code === 4902) {
        throw new Error("Chain not added to wallet");
      }
      throw err;
    }
  },

  personalSign: async function (account, message) {
    return await window.ethereum.request({
      method: "personal_sign",
      params: [message, account]
    });
  },

  signTypedData: async function (account, typedDataJson) {
    const data = typeof typedDataJson === "string" ? typedDataJson : JSON.stringify(typedDataJson);
    return await window.ethereum.request({
      method: "eth_signTypedData_v4",
      params: [account, data]
    });
  },

  sendDeposit: async function (from, vaultAddress, valueWeiHex) {
    const txHash = await window.ethereum.request({
      method: "eth_sendTransaction",
      params: [{
        from,
        to: vaultAddress,
        value: valueWeiHex,
        data: "0x"
      }]
    });
    return txHash;
  }
};
