"use client";

import { useSyncExternalStore } from "react";

const fontSteps = ["1", "1.1", "1.2"] as const;
const fontLabels = ["100%", "110%", "120%"] as const;
const fontKey = "vortex-font-step";
const enteredKey = "vortex-demo-entered";

let fontStep = 0;
let fontLoaded = false;
let demoEntered = false;
let demoLoaded = false;
const listeners = new Set<() => void>();

function emit() {
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function readFontStep() {
  if (fontLoaded) return;
  fontLoaded = true;
  const stored = Number(window.localStorage.getItem(fontKey));
  fontStep = stored === 1 || stored === 2 ? stored : 0;
}

function readDemoEntered() {
  if (demoLoaded) return;
  demoLoaded = true;
  demoEntered = window.sessionStorage.getItem(enteredKey) === "1";
}

export function useFontStep() {
  return useSyncExternalStore(subscribe, () => {
    readFontStep();
    return fontStep;
  }, () => 0);
}

export function fontStepLabel(step: number) {
  return fontLabels[step] ?? fontLabels[0];
}

export function fontStepZoom(step: number) {
  return fontSteps[step] ?? fontSteps[0];
}

export function cycleFontStep() {
  readFontStep();
  fontStep = (fontStep + 1) % fontSteps.length;
  window.localStorage.setItem(fontKey, String(fontStep));
  emit();
}

export function useDemoEntered() {
  return useSyncExternalStore(subscribe, () => {
    readDemoEntered();
    return demoEntered;
  }, () => false);
}

export function enterDemo() {
  window.sessionStorage.setItem(enteredKey, "1");
  demoEntered = true;
  demoLoaded = true;
  emit();
}

export function leaveDemo() {
  window.sessionStorage.removeItem(enteredKey);
  demoEntered = false;
  demoLoaded = true;
  emit();
}
