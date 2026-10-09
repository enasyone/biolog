'use client';

import { useEffect, useState, type CSSProperties } from 'react';
import { STANDARD_FORMATS, mmToPx } from '../bio-crd-002/cardFormats';
import { createPhotoElement, setContent, type PhotoElement } from './photoElement';
import {
  addPhotoOn,
  bringPhotoForwardOn,
  bringPhotoToFrontOn,
  createInitialPhotoState,
  movePhotoOn,
  removePhotoOn,
  resizePhotoOn,
  rotatePhotoOn,
  sendPhotoBackwardOn,
  sendPhotoToBackOn,
  setPhotoLockedOn,
  type PhotoDesignState,
  type PhotoSide,
} from './photoDesignState';
import styles from './card-photo.module.css';

const CANVAS_WIDTH_PX = mmToPx(STANDARD_FORMATS.cr80.longEdgeMm);
const CANVAS_HEIGHT_PX = mmToPx(STANDARD_FORMATS.cr80.shortEdgeMm);
const CANVAS_MAX_ON_SCREEN_PX = 420;

let counter = 0;
function nextId(): string {
  counter += 1;
  return `photo-${counter}`;
}

export interface CardPhotoAppProps {
  /** BIO-CRD-008-AC05: additive, optional - see `CardDesignerApp`'s own identically-shaped prop for the full rationale (Kemi's "zero I/O" ruling: this component still does none itself). */
  onChange?: (state: PhotoDesignState) => void;
}

/**
 * BIO-CRD-009: places a real, positionable cardholder photo on the card canvas - front and back
 * independently (AC03), same move/resize/rotate/layer/lock operations as `LogoElement`/
 * `WatermarkElement` (AC01), `content` is an opaque reference only (AC02).
 */
export function CardPhotoApp({ onChange }: CardPhotoAppProps = {}) {
  const [state, setState] = useState<PhotoDesignState>(createInitialPhotoState);
  const [side, setSide] = useState<PhotoSide>('front');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [newPhotoContent, setNewPhotoContent] = useState('cardholder-photo-1');

  useEffect(() => {
    onChange?.(state);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reports every state change; onChange identity is the caller's concern
  }, [state]);

  const scale = Math.min(1, CANVAS_MAX_ON_SCREEN_PX / Math.max(CANVAS_WIDTH_PX, CANVAS_HEIGHT_PX));
  const currentElements = side === 'front' ? state.front : state.back;
  const selected = currentElements.find((e) => e.id === selectedId) ?? null;

  function updateOne(update: (element: PhotoElement) => PhotoElement) {
    if (!selectedId) return;
    setState((current) => {
      const target = (side === 'front' ? current.front : current.back).find((e) => e.id === selectedId);
      if (!target) return current;
      const updated = update(target);
      return side === 'front'
        ? { ...current, front: current.front.map((e) => (e.id === selectedId ? updated : e)) }
        : { ...current, back: current.back.map((e) => (e.id === selectedId ? updated : e)) };
    });
  }

  function addPhoto() {
    if (!newPhotoContent.trim()) return;
    const id = nextId();
    const element = createPhotoElement(
      id,
      newPhotoContent.trim(),
      { xPx: 20, yPx: 20 + currentElements.length * 10, widthPx: 90, heightPx: 110 },
      currentElements.length,
    );
    setState((current) => addPhotoOn(current, side, element));
    setSelectedId(id);
  }

  function removeSelected() {
    if (!selectedId) return;
    setState((current) => removePhotoOn(current, side, selectedId));
    setSelectedId(null);
  }

  const NUDGE_PX = 4;
  const MOVE_KEYS: Record<string, [number, number]> = {
    ArrowLeft: [-NUDGE_PX, 0],
    ArrowRight: [NUDGE_PX, 0],
    ArrowUp: [0, -NUDGE_PX],
    ArrowDown: [0, NUDGE_PX],
  };

  function handlePropertiesKeyDown(event: React.KeyboardEvent) {
    if (!selectedId) return;
    const delta = MOVE_KEYS[event.key];
    if (!delta) return;
    event.preventDefault();
    setState((current) => movePhotoOn(current, side, selectedId, delta[0], delta[1]));
  }

  return (
    <div className={styles.app} data-testid="bio-crd-009-app">
      <h2>Card Photo</h2>
      <p>
        Place, move, resize, rotate and layer the cardholder&apos;s photo on the card canvas - front
        and back independently (BIO-CRD-009). Canvas shown at CR80 landscape size for context.
      </p>

      <div className={styles.controlsRow}>
        <div role="tablist" aria-label="Front or back">
          <button type="button" role="tab" aria-selected={side === 'front'} onClick={() => setSide('front')}>
            Front
          </button>
          <button type="button" role="tab" aria-selected={side === 'back'} onClick={() => setSide('back')}>
            Back
          </button>
        </div>
      </div>

      <div className={styles.toolbar}>
        <div className={styles.toolbarGroup} aria-label="Add a photo">
          <label className={styles.field}>
            New photo reference
            <input type="text" value={newPhotoContent} onChange={(event) => setNewPhotoContent(event.target.value)} />
          </label>
          <button type="button" onClick={addPhoto} disabled={!newPhotoContent.trim()}>
            + Photo
          </button>
        </div>
      </div>

      <div className={styles.workspace}>
        <div
          className={styles.canvasFrame}
          style={{ width: CANVAS_WIDTH_PX * scale, height: CANVAS_HEIGHT_PX * scale }}
          role="group"
          aria-label={`${side === 'front' ? 'Front' : 'Back'} card photo canvas`}
        >
          <div className={styles.canvasSurface} style={{ width: CANVAS_WIDTH_PX, height: CANVAS_HEIGHT_PX, transform: `scale(${scale})` }}>
            {[...currentElements]
              .sort((a, b) => a.zIndex - b.zIndex)
              .map((element) => {
                const style: CSSProperties = {
                  left: element.xPx,
                  top: element.yPx,
                  width: element.widthPx,
                  height: element.heightPx,
                  transform: `rotate(${element.rotationDeg}deg)`,
                  zIndex: element.zIndex,
                };
                return (
                  <button
                    key={element.id}
                    type="button"
                    className={styles.element}
                    data-selected={element.id === selectedId}
                    data-locked={element.locked}
                    onClick={() => setSelectedId(element.id)}
                    aria-pressed={element.id === selectedId}
                    style={style}
                  >
                    {element.content}
                    {element.locked ? <span aria-hidden="true"> 🔒</span> : null}
                  </button>
                );
              })}
          </div>
        </div>

        <aside className={styles.sidebar} aria-label={`${side === 'front' ? 'Front' : 'Back'} photo layers and properties`}>
          <h2>Layers ({side})</h2>
          <ul className={styles.layerList}>
            {[...currentElements]
              .sort((a, b) => b.zIndex - a.zIndex)
              .map((element) => (
                <li key={element.id}>
                  <button
                    type="button"
                    className={styles.layerListItem}
                    aria-pressed={element.id === selectedId}
                    onClick={() => setSelectedId(element.id)}
                  >
                    {element.content}
                    {element.locked ? ' 🔒' : ''}
                  </button>
                </li>
              ))}
          </ul>

          {selected ? (
            // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions
            <div className={styles.properties} onKeyDown={handlePropertiesKeyDown}>
              <h2>Properties</h2>
              <p className={styles.hint}>Use the arrow keys anywhere in this panel to nudge the selected photo.</p>

              <label className={styles.field}>
                Photo reference
                <input
                  type="text"
                  value={selected.content}
                  disabled={selected.locked}
                  onChange={(event) => updateOne((e) => setContent(e, event.target.value))}
                />
              </label>
              <label className={styles.field}>
                Width (px)
                <input
                  type="number"
                  min={1}
                  value={selected.widthPx}
                  disabled={selected.locked}
                  onChange={(event) => {
                    const width = Number(event.target.value);
                    if (width > 0) setState((current) => resizePhotoOn(current, side, selected.id, width, selected.heightPx));
                  }}
                />
              </label>
              <label className={styles.field}>
                Height (px)
                <input
                  type="number"
                  min={1}
                  value={selected.heightPx}
                  disabled={selected.locked}
                  onChange={(event) => {
                    const height = Number(event.target.value);
                    if (height > 0) setState((current) => resizePhotoOn(current, side, selected.id, selected.widthPx, height));
                  }}
                />
              </label>

              <div className={styles.buttonRow}>
                <button type="button" disabled={selected.locked} onClick={() => setState((current) => rotatePhotoOn(current, side, selected.id, -15))}>
                  Rotate -15°
                </button>
                <button type="button" disabled={selected.locked} onClick={() => setState((current) => rotatePhotoOn(current, side, selected.id, 15))}>
                  Rotate +15°
                </button>
              </div>

              <div className={styles.buttonRow}>
                <button type="button" onClick={() => setState((current) => bringPhotoToFrontOn(current, side, selected.id))}>Bring to front</button>
                <button type="button" onClick={() => setState((current) => bringPhotoForwardOn(current, side, selected.id))}>Forward</button>
                <button type="button" onClick={() => setState((current) => sendPhotoBackwardOn(current, side, selected.id))}>Backward</button>
                <button type="button" onClick={() => setState((current) => sendPhotoToBackOn(current, side, selected.id))}>Send to back</button>
              </div>

              <label className={styles.field}>
                <input
                  type="checkbox"
                  checked={selected.locked}
                  onChange={(event) => setState((current) => setPhotoLockedOn(current, side, selected.id, event.target.checked))}
                />
                Locked
              </label>

              <button type="button" onClick={removeSelected}>
                Remove photo
              </button>
            </div>
          ) : (
            <p className={styles.hint}>Add or select a photo above to adjust its position, size, rotation, layer or lock state.</p>
          )}
        </aside>
      </div>
    </div>
  );
}
