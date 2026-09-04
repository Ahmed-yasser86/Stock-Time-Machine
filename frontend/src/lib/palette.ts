/** Categorical series colors, mirroring the --color-pick-* tokens in index.css.
 * Colorblind-safe set, AA on paper, max 4 comparison picks (Phase 0 lock). */
export const PICK_COLORS = ['#3b5bdb', '#0c5b66', '#b97a0c', '#7c4d8c'] as const;

// Two-company development/testing scope: quota is not a blocker at this size,
// and every shared view below is designed around pairs. Keep the 4-color
// palette (harmless) so widening later is a one-line change.
export const MAX_COMPARE_PICKS = 2;

export function pickColor(index: number): string {
  return PICK_COLORS[index % PICK_COLORS.length];
}
