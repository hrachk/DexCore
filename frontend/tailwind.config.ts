import type { Config } from "tailwindcss";

const config: Config = {
  content: [
    "./app/**/*.{js,ts,jsx,tsx,mdx}",
    "./components/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      colors: {
        surface: {
          DEFAULT: "#0b0e11",
          50: "#12161c",
          100: "#1a1f27",
          200: "#242b36",
          300: "#2f3847",
        },
        border: {
          DEFAULT: "#2a3340",
          light: "#3a4556",
        },
        accent: {
          DEFAULT: "#3b82f6",
          hover: "#2563eb",
        },
        bid: {
          DEFAULT: "#0ecb81",
          dim: "rgba(14, 203, 129, 0.12)",
          text: "#0ecb81",
        },
        ask: {
          DEFAULT: "#f6465d",
          dim: "rgba(246, 70, 93, 0.12)",
          text: "#f6465d",
        },
        muted: "#848e9c",
      },
      fontFamily: {
        mono: ["ui-monospace", "SFMono-Regular", "Menlo", "Monaco", "Consolas", "monospace"],
        sans: ["Inter", "system-ui", "-apple-system", "sans-serif"],
      },
    },
  },
  plugins: [],
};

export default config;
