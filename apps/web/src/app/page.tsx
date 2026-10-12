import { connection } from "next/server";
import { TerminalView } from "@/components/terminal-view";

export default async function Home() {
  await connection();
  return <TerminalView />;
}
