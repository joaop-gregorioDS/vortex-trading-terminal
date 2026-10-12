"use client";

import type { ReactNode } from "react";
import { cycleFontStep, fontStepLabel, fontStepZoom, useFontStep } from "@/lib/page-preferences";

/**
 * Scales the whole terminal while laying it out in a narrower box, so a larger
 * font still ends at the right edge of the window instead of sliding past it.
 */
export function FontScale({ children }: { children: ReactNode }) {
  const zoom = Number(fontStepZoom(useFontStep()));
  return (
    <div
      style={{
        width: `${100 / zoom}%`,
        height: `${100 / zoom}vh`,
        transform: `scale(${zoom})`,
        transformOrigin: "top left",
      }}
    >
      {children}
    </div>
  );
}

/** Same two shortcuts on the demo screen and on the terminal header. */
export function PageTools() {
  const step = useFontStep();

  return (
    <div className="flex items-center gap-1.5">
      <button
        type="button"
        onClick={cycleFontStep}
        title={`Aumentar a fonte. Agora ${fontStepLabel(step)}.`}
        className="rounded-full border border-[#e5e1d7] bg-white px-2.5 py-1 text-[11px] font-semibold text-stone-700 shadow-xs transition hover:border-amber-400 hover:bg-amber-50"
      >
        <span className="font-bold text-[#9a3412]">A+</span> Fonte
      </button>
      <a
        href="/swagger/index.html"
        target="_blank"
        rel="noreferrer"
        className="rounded-full border border-[#e5e1d7] bg-white px-2.5 py-1 text-[11px] font-semibold text-stone-700 shadow-xs transition hover:border-amber-400 hover:bg-amber-50"
      >
        Swagger
      </a>
    </div>
  );
}
