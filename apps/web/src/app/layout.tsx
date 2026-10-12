import type { Metadata } from "next";
import type { ReactNode } from "react";
import { FontScale } from "@/components/page-tools";
import "./globals.css";

export const metadata: Metadata = {
  title: "Vortex Trading Terminal",
  description:
    "Professional financial trading terminal engineering project by Vortex Software.",
};

export default function RootLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  return (
    <html lang="pt-BR">
      <body>
        <FontScale>{children}</FontScale>
      </body>
    </html>
  );
}
