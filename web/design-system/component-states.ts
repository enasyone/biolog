/**
 * Core component states (BIO-UX-001-AC05).
 *
 * PRODUCT-EXPERIENCE.md requires "default, loading, empty, success, warning,
 * error, permission-denied and offline/degraded" as a page-level
 * interaction-state contract, and my own prompt requires "default, hover,
 * focus, disabled, loading, success, warning, error and offline" at the
 * component level. This module freezes the component-level list (the
 * superset a shared control such as a button/input/card needs to support)
 * and maps each state to the token(s) that express it, so later stories
 * building actual components have one place to look up "what does a
 * disabled control look like" rather than inventing it per feature.
 *
 * Page-level states (empty/permission-denied) are route-level compositions,
 * not a single control's visual state, so they are documented in this
 * story's README rather than tokenised here.
 */
import type { StatusColorTokens, ThemeTokens } from './tokens';

export type ComponentState =
  | 'default'
  | 'hover'
  | 'focus'
  | 'disabled'
  | 'loading'
  | 'success'
  | 'warning'
  | 'error'
  | 'offline';

export interface ComponentStateSpec {
  /** Human-readable description of the visual treatment. */
  description: string;
  /** Token(s) from `ThemeTokens`/`StatusColorTokens` that express this state. */
  tokens: ReadonlyArray<keyof ThemeTokens | keyof StatusColorTokens>;
  /** Opacity applied on top of the base token(s), when relevant. */
  opacity?: number;
}

export const COMPONENT_STATES: readonly ComponentState[] = Object.freeze([
  'default',
  'hover',
  'focus',
  'disabled',
  'loading',
  'success',
  'warning',
  'error',
  'offline',
]);

export const componentStateSpecs: Readonly<Record<ComponentState, ComponentStateSpec>> =
  Object.freeze({
    default: Object.freeze({
      description: 'Resting appearance: surface/border/text tokens, no overlay.',
      tokens: ['colorSurface', 'colorBorder', 'colorText'] as const,
    }),
    hover: Object.freeze({
      description: 'Pointer is over an interactive element; background shifts toward colorBg.',
      tokens: ['colorBg', 'colorBorder'] as const,
    }),
    focus: Object.freeze({
      description:
        'Keyboard/programmatic focus: a visible 3px colorAccent outline, 2px offset (see globals.css :focus-visible).',
      tokens: ['colorAccent'] as const,
    }),
    disabled: Object.freeze({
      description: 'Non-interactive: reduced opacity, no hover/focus treatment applies.',
      tokens: ['colorText', 'colorBorder'] as const,
      opacity: 0.5,
    }),
    loading: Object.freeze({
      description:
        'Async work in progress: content is replaced or overlaid by a skeleton/spinner using colorBorder as the base and colorSurface as the shimmer highlight; the control remains non-interactive like disabled.',
      tokens: ['colorBorder', 'colorSurface'] as const,
      opacity: 0.5,
    }),
    success: Object.freeze({
      description: 'Positive outcome confirmed (e.g. simulated action completed).',
      tokens: ['colorSuccess', 'colorSuccessOn', 'colorSuccessBg'] as const,
    }),
    warning: Object.freeze({
      description: 'Action succeeded with a caveat, or a non-blocking issue needs attention.',
      tokens: ['colorWarning', 'colorWarningOn', 'colorWarningBg'] as const,
    }),
    error: Object.freeze({
      description: 'Action failed or input is invalid.',
      tokens: ['colorError', 'colorErrorOn', 'colorErrorBg'] as const,
    }),
    offline: Object.freeze({
      description:
        'Connectivity/device is degraded or unavailable; distinct from error because no action failed, and distinct from disabled because the control may become usable again without a page reload.',
      tokens: ['colorOffline', 'colorOfflineOn', 'colorOfflineBg'] as const,
    }),
  });
