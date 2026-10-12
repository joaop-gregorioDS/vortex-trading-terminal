"use client";

export type ProtectionUnit = "ticks" | "points" | "percent";

export type ProtectionDraft = {
  enabled: boolean;
  unit: ProtectionUnit;
  gain: string;
  loss: string;
  trail: string;
  breakEven: string;
};

export const idleProtection: ProtectionDraft = {
  enabled: false,
  unit: "points",
  gain: "",
  loss: "",
  trail: "",
  breakEven: "",
};

export type ProtectionBody = {
  gain: string;
  loss: string;
  unit: ProtectionUnit;
  trail?: string;
  breakEven?: string;
};

/** The bracket to send with an order. Empty optionals stay off the JSON body. */
export function protectionPayload(draft: ProtectionDraft): { body?: ProtectionBody; error?: string } {
  if (!draft.enabled) return {};
  const gain = draft.gain.trim();
  const loss = draft.loss.trim();
  if (!gain || !loss) return { error: "Informe o ganho e a perda da estratégia." };
  const trail = draft.trail.trim();
  const breakEven = draft.breakEven.trim();
  return {
    body: {
      gain,
      loss,
      unit: draft.unit,
      ...(trail ? { trail } : {}),
      ...(breakEven ? { breakEven } : {}),
    },
  };
}

const units: Array<{ id: ProtectionUnit; label: string }> = [
  { id: "ticks", label: "Ticks" },
  { id: "points", label: "Pontos" },
  { id: "percent", label: "%" },
];

export function ProtectionFields({
  value,
  onChange,
}: {
  value: ProtectionDraft;
  onChange: (next: ProtectionDraft) => void;
}) {
  return (
    <div className="space-y-2">
      <label className="flex items-center gap-2 text-[11px] font-semibold text-stone-700">
        <input
          type="checkbox"
          checked={value.enabled}
          onChange={(event) => onChange({ ...value, enabled: event.target.checked })}
          className="accent-amber-600"
        />
        Estratégia OCO
      </label>
      {value.enabled && (
        <div className="space-y-2 rounded-md border border-[#e5e1d7] bg-[#f9f8f5] p-2">
          <div className="grid grid-cols-3 gap-1 text-[10px] font-semibold">
            {units.map((unit) => (
              <button
                key={unit.id}
                type="button"
                onClick={() => onChange({ ...value, unit: unit.id })}
                className={`rounded px-1 py-1 ${
                  value.unit === unit.id ? "bg-white text-stone-900 shadow-xs" : "text-stone-500"
                }`}
              >
                {unit.label}
              </button>
            ))}
          </div>
          <div className="grid grid-cols-2 gap-2">
            <Field label="Ganho" text={value.gain} onText={(gain) => onChange({ ...value, gain })} />
            <Field label="Perda" text={value.loss} onText={(loss) => onChange({ ...value, loss })} />
            <Field label="Trailing" text={value.trail} onText={(trail) => onChange({ ...value, trail })} />
            <Field label="Break-even" text={value.breakEven} onText={(breakEven) => onChange({ ...value, breakEven })} />
          </div>
          <p className="text-[10px] leading-snug text-stone-500">
            O alvo e o stop nascem depois da execução, a partir do preço médio. Trailing e break-even são opcionais.
          </p>
        </div>
      )}
    </div>
  );
}

function Field({ label, text, onText }: { label: string; text: string; onText: (value: string) => void }) {
  return (
    <label className="block text-[10px] font-semibold text-stone-600">
      {label}
      <input
        type="text"
        value={text}
        onChange={(event) => onText(event.target.value)}
        className="mt-0.5 w-full rounded border border-[#d8d2c4] bg-white px-2 py-1 text-right font-mono text-[11px] text-stone-900"
      />
    </label>
  );
}
