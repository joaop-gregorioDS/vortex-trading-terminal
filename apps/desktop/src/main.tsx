import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { Shell } from "./views/Shell";
import "./styles.css";

const root = document.getElementById("root");
if (!root) throw new Error("A raiz da janela não existe.");
createRoot(root).render(
  <StrictMode>
    <Shell />
  </StrictMode>,
);
