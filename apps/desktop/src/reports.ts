import { decimalForDrawing } from "./chart/drawing";

/** `todos` e um ticker fora do catálogo não vão para a consulta. */
export function tradeQuery(filter: string, catalog: readonly string[]): string | null {
  if (!filter || filter === "todos") return null;
  return catalog.includes(filter) ? filter : null;
}

/** Escala só para o traço. O texto de cada ponto continua o do servidor. */
export function equityLine(values: readonly string[]): string | null {
  if (values.length < 2) return null;
  const numbers: number[] = [];
  for (const value of values) {
    try {
      numbers.push(decimalForDrawing(value));
    } catch {
      return null;
    }
  }
  const min = Math.min(...numbers);
  const max = Math.max(...numbers);
  const span = max - min || 1;
  return numbers
    .map((value, index) => {
      const x = (index / (numbers.length - 1)) * 100;
      const y = ((max - value) / span) * 100;
      return `${x.toFixed(2)},${y.toFixed(2)}`;
    })
    .join(" ");
}
