import { TradingTerminal } from "@/components/trade/TradingTerminal";

export default function TradePage({
  params,
}: {
  params: { symbol: string };
}) {
  const symbol = decodeURIComponent(params.symbol).toUpperCase();
  return <TradingTerminal symbol={symbol} />;
}
