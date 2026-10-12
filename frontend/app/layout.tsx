import type { Metadata } from "next";
import { Providers } from "@/components/wallet/Providers";
import "./globals.css";

export const metadata: Metadata = {
  title: "DexCore — Institutional Perpetual DEX",
  description:
    "High-performance decentralized perpetual exchange terminal",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body className="antialiased">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
