'use client';

import type { CardCanvasSide } from './orientation';
import styles from './card-designer.module.css';

/**
 * BIO-CRD-002: a true WYSIWYG rendering of one card side - every element renders at its exact
 * model position/size/rotation (`elements.ts`), scaled down uniformly to fit the screen by a
 * single CSS transform on the outer frame. What changes on screen is only the zoom level; the
 * proportions a card designer sees are always the real geometry the model holds, which is what
 * makes this "what you see is what you get" rather than an approximation of it.
 */
export function CardCanvas({
  side,
  scale,
  selectedId,
  onSelect,
  sideLabel,
}: {
  side: CardCanvasSide;
  scale: number;
  selectedId: string | null;
  onSelect: (id: string) => void;
  sideLabel: string;
}) {
  const sorted = [...side.elements].sort((a, b) => a.zIndex - b.zIndex);

  return (
    <div
      className={styles.canvasFrame}
      style={{ width: side.widthPx * scale, height: side.heightPx * scale }}
      role="group"
      aria-label={`${sideLabel} card canvas`}
    >
      <div
        className={styles.canvasSurface}
        style={{ width: side.widthPx, height: side.heightPx, transform: `scale(${scale})` }}
      >
        {sorted.map((element) => (
          <button
            key={element.id}
            type="button"
            className={styles.element}
            data-selected={element.id === selectedId}
            data-locked={element.locked}
            onClick={() => onSelect(element.id)}
            aria-pressed={element.id === selectedId}
            style={{
              left: element.xPx,
              top: element.yPx,
              width: element.widthPx,
              height: element.heightPx,
              transform: `rotate(${element.rotationDeg}deg)`,
              zIndex: element.zIndex,
            }}
          >
            {element.label}
            {element.locked ? <span aria-hidden="true"> 🔒</span> : null}
          </button>
        ))}
      </div>
    </div>
  );
}
