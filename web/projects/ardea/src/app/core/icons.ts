import { provideLucideConfig } from '@lucide/angular';

/**
 * Icons: 1.6px line drawings on a 24px grid, shown at 16px (DESIGN.md, Shapes). Provided by the three
 * layouts (sign-in frame, signed-in shell, component library) rather than at the root, so the icon set
 * loads with the first layout and stays out of the start-up bundle; every page renders inside one of
 * them, and a dialog the shell opens takes the shell's injector.
 */
export const ICON_CONFIG = provideLucideConfig({ size: 16, strokeWidth: 1.6 });
