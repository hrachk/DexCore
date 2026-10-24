import { createFileRoute } from "@tanstack/react-router";
import { TradeScreen } from "@/components/terminal/trade-screen";

export const Route = createFileRoute("/")({
  component: TradeScreen,
});
