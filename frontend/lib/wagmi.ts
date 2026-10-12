"use client";

import { getDefaultConfig } from "@rainbow-me/rainbowkit";
import { http } from "wagmi";
import { mainnet, sepolia, hardhat } from "wagmi/chains";

export const wagmiConfig = getDefaultConfig({
  appName: "DexCore",
  projectId:
    process.env.NEXT_PUBLIC_WALLETCONNECT_PROJECT_ID ?? "dexcore-demo-project",
  chains: [hardhat, sepolia, mainnet],
  transports: {
    [hardhat.id]: http("http://127.0.0.1:8545"),
    [sepolia.id]: http(),
    [mainnet.id]: http(),
  },
  ssr: true,
});
