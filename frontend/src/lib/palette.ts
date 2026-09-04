/** Categorical series colors, mirroring the --color-pick-* tokens in index.css.
 * Colorblind-safe set, AA on paper, max 4 comparison picks (Phase 0 lock). */
export const PICK_COLORS = ['#3b5bdb', '#0c5b66', '#b97a0c', '#7c4d8c'] as const;

export const MAX_COMPARE_PICKS = 4;

export function pickColor(index: number): string {
  return PICK_COLORS[index % PICK_COLORS.length];
}
