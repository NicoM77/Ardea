import {
  LucideBell,
  LucideFolder,
  LucideInbox,
  LucideLayers,
  LucideLayoutGrid,
  LucideListChecks,
  LucideRadioTower,
  LucideServerCog,
  LucideShieldExclamationPoint,
  LucideSlidersHorizontal,
  LucideUsers,
  type LucideIconData,
} from '@lucide/angular';
import type { AreaId } from './areas';

/**
 * The icon of each navigation area (16px in the sidebar, DESIGN.md Navigation). Apart from the areas module,
 * which the routes read at start-up, so the icon set loads with the signed-in shell and not with the
 * first screen.
 */
export const AREA_ICONS: Readonly<Record<AreaId, LucideIconData>> = {
  overview: LucideLayoutGrid.icon,
  notices: LucideInbox.icon,
  reminders: LucideListChecks.icon,
  vulnerabilities: LucideShieldExclamationPoint.icon,
  projects: LucideFolder.icon,
  stack: LucideLayers.icon,
  sources: LucideRadioTower.icon,
  rules: LucideBell.icon,
  team: LucideUsers.icon,
  settings: LucideSlidersHorizontal.icon,
  administration: LucideServerCog.icon,
};
