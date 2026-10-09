'use client';

import { useEffect, useMemo, useState } from 'react';
import { CardCanvas } from './CardCanvas';
import { CUSTOM_SIZE_BOUNDS_MM, validateCustomSize, type CardFormatKind } from './cardFormats';
import { createElement } from './elements';
import {
  addElement,
  bringForwardOn,
  bringToFrontOn,
  createInitialDesignState,
  moveElementOn,
  removeElement,
  resizeElementOn,
  rotateElementOn,
  sendBackwardOn,
  sendToBackOn,
  setLockedOn,
  switchDesignOrientation,
  type CardDesignState,
  type CardSide,
} from './cardDesignState';
import styles from './card-designer.module.css';

const FORMAT_LABELS: Record<CardFormatKind, string> = {
  cr80: 'CR80 (ID-1, 85.60 × 53.98mm)',
  cr79: 'CR79 (83.90 × 51.90mm)',
  cr100: 'CR100 (98.50 × 67.00mm)',
  custom: 'Custom size',
};

const CANVAS_MAX_ON_SCREEN_PX = 420;
let elementCounter = 0;

function nextElementId(): string {
  elementCounter += 1;
  return `element-${elementCounter}`;
}

export interface CardDesignerAppProps {
  /**
   * BIO-CRD-008-AC05: additive, optional, defaults to undefined - every existing caller (every
   * test written before this story) is completely unaffected. Per Kemi's ruling (see
   * `SaveDesignBar`'s own header), this component itself still does zero I/O - it only reports
   * its own current state upward so a PARENT (this route's own page, or BIO-CRD-008's own
   * `TemplateLifecycleBar`) can read it. The canvas stays exactly as pure as it was.
   */
  onChange?: (state: CardDesignState) => void;
}

export function CardDesignerApp({ onChange }: CardDesignerAppProps = {}) {
  const [state, setState] = useState<CardDesignState>(() => createInitialDesignState('cr80', 'landscape'));
  const [side, setSide] = useState<CardSide>('front');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [customWidthInput, setCustomWidthInput] = useState('90');
  const [customHeightInput, setCustomHeightInput] = useState('60');

  useEffect(() => {
    onChange?.(state);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reports every state change; onChange identity is the caller's concern, not a dependency worth re-running for
  }, [state]);

  const currentSide = side === 'front' ? state.front : state.back;
  const selectedElement = currentSide.elements.find((e) => e.id === selectedId) ?? null;
  const scale = Math.min(1, CANVAS_MAX_ON_SCREEN_PX / Math.max(currentSide.widthPx, currentSide.heightPx));

  const customValidation = useMemo(
    () => validateCustomSize(Number(customWidthInput), Number(customHeightInput)),
    [customWidthInput, customHeightInput],
  );

  function selectFormat(kind: CardFormatKind) {
    setSelectedId(null);
    if (kind !== 'custom') {
      setState(createInitialDesignState(kind, state.orientation));
      return;
    }
    if (!customValidation.isValid) return;
    setState(createInitialDesignState('custom', state.orientation, { widthMm: Number(customWidthInput), heightMm: Number(customHeightInput) }));
  }

  function applyCustomSize() {
    if (!customValidation.isValid) return;
    setState(createInitialDesignState('custom', state.orientation, { widthMm: Number(customWidthInput), heightMm: Number(customHeightInput) }));
  }

  function toggleOrientation() {
    setState(switchDesignOrientation(state));
  }

  function addNewElement() {
    const id = nextElementId();
    setState((current) =>
      addElement(current, side, createElement({ id, label: `Element ${id.split('-')[1]}`, xPx: 40, yPx: 40, widthPx: 160, heightPx: 80, zIndex: currentSide.elements.length })),
    );
    setSelectedId(id);
  }

  function removeSelected() {
    if (!selectedId) return;
    setState((current) => removeElement(current, side, selectedId));
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
    setState((current) => moveElementOn(current, side, selectedId, delta[0], delta[1]));
  }

  return (
    <div className={styles.designer}>
      <h1>Card Designer</h1>
      <p>
        True front/back WYSIWYG editing with CR80, CR79, CR100 and custom sizes, one-click
        landscape/portrait switching, and move/resize/rotate/layer/lock element controls
        (BIO-CRD-002).
      </p>

      <div className={styles.controlsRow}>
        <label className={styles.field}>
          Format
          <select
            value={state.formatKind}
            onChange={(event) => selectFormat(event.target.value as CardFormatKind)}
          >
            {(Object.keys(FORMAT_LABELS) as CardFormatKind[]).map((kind) => (
              <option key={kind} value={kind}>
                {FORMAT_LABELS[kind]}
              </option>
            ))}
          </select>
        </label>

        {state.formatKind === 'custom' ? (
          <div className={styles.customSizeFields}>
            <label className={styles.field}>
              Width (mm)
              <input
                type="number"
                min={CUSTOM_SIZE_BOUNDS_MM.min}
                max={CUSTOM_SIZE_BOUNDS_MM.max}
                value={customWidthInput}
                onChange={(event) => setCustomWidthInput(event.target.value)}
              />
            </label>
            <label className={styles.field}>
              Height (mm)
              <input
                type="number"
                min={CUSTOM_SIZE_BOUNDS_MM.min}
                max={CUSTOM_SIZE_BOUNDS_MM.max}
                value={customHeightInput}
                onChange={(event) => setCustomHeightInput(event.target.value)}
              />
            </label>
            <button type="button" onClick={applyCustomSize} disabled={!customValidation.isValid}>
              Apply custom size
            </button>
            {!customValidation.isValid ? (
              <p role="alert" data-testid="custom-size-validation-error" className={styles.validationError}>
                {customValidation.errors.join('; ')}
              </p>
            ) : null}
          </div>
        ) : null}

        <button type="button" onClick={toggleOrientation}>
          Switch to {state.orientation === 'landscape' ? 'portrait' : 'landscape'}
        </button>
      </div>

      <div className={styles.controlsRow}>
        <div role="tablist" aria-label="Front or back">
          <button type="button" role="tab" aria-selected={side === 'front'} onClick={() => setSide('front')}>
            Front
          </button>
          <button type="button" role="tab" aria-selected={side === 'back'} onClick={() => setSide('back')}>
            Back
          </button>
        </div>
        <button type="button" onClick={addNewElement}>
          Add element
        </button>
      </div>

      <div className={styles.workspace}>
        <CardCanvas side={currentSide} scale={scale} selectedId={selectedId} onSelect={setSelectedId} sideLabel={side === 'front' ? 'Front' : 'Back'} />

        <aside className={styles.sidebar}>
          <h2>Layers ({side})</h2>
          <ul className={styles.layerList}>
            {[...currentSide.elements]
              .sort((a, b) => b.zIndex - a.zIndex)
              .map((element) => (
                <li key={element.id}>
                  <button
                    type="button"
                    onClick={() => setSelectedId(element.id)}
                    aria-pressed={element.id === selectedId}
                    className={styles.layerListItem}
                  >
                    {element.label}
                    {element.locked ? ' 🔒' : ''}
                  </button>
                </li>
              ))}
          </ul>

          {selectedElement ? (
            // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions
            <div className={styles.properties} onKeyDown={handlePropertiesKeyDown}>
              <h2>Properties</h2>
              <p className={styles.hint}>Use the arrow keys anywhere in this panel to nudge the selected element.</p>

              <label className={styles.field}>
                X (px)
                <input
                  type="number"
                  value={selectedElement.xPx}
                  disabled={selectedElement.locked}
                  onChange={(event) => setState((current) => moveElementOn(current, side, selectedElement.id, Number(event.target.value) - selectedElement.xPx, 0))}
                />
              </label>
              <label className={styles.field}>
                Y (px)
                <input
                  type="number"
                  value={selectedElement.yPx}
                  disabled={selectedElement.locked}
                  onChange={(event) => setState((current) => moveElementOn(current, side, selectedElement.id, 0, Number(event.target.value) - selectedElement.yPx))}
                />
              </label>
              <label className={styles.field}>
                Width (px)
                <input
                  type="number"
                  min={1}
                  value={selectedElement.widthPx}
                  disabled={selectedElement.locked}
                  onChange={(event) => {
                    const width = Number(event.target.value);
                    if (width > 0) setState((current) => resizeElementOn(current, side, selectedElement.id, width, selectedElement.heightPx));
                  }}
                />
              </label>
              <label className={styles.field}>
                Height (px)
                <input
                  type="number"
                  min={1}
                  value={selectedElement.heightPx}
                  disabled={selectedElement.locked}
                  onChange={(event) => {
                    const height = Number(event.target.value);
                    if (height > 0) setState((current) => resizeElementOn(current, side, selectedElement.id, selectedElement.widthPx, height));
                  }}
                />
              </label>

              <div className={styles.buttonRow}>
                <button type="button" disabled={selectedElement.locked} onClick={() => setState((current) => rotateElementOn(current, side, selectedElement.id, -15))}>
                  Rotate -15°
                </button>
                <button type="button" disabled={selectedElement.locked} onClick={() => setState((current) => rotateElementOn(current, side, selectedElement.id, 15))}>
                  Rotate +15°
                </button>
              </div>

              <div className={styles.buttonRow}>
                <button type="button" onClick={() => setState((current) => bringToFrontOn(current, side, selectedElement.id))}>Bring to front</button>
                <button type="button" onClick={() => setState((current) => bringForwardOn(current, side, selectedElement.id))}>Forward</button>
                <button type="button" onClick={() => setState((current) => sendBackwardOn(current, side, selectedElement.id))}>Backward</button>
                <button type="button" onClick={() => setState((current) => sendToBackOn(current, side, selectedElement.id))}>Send to back</button>
              </div>

              <label className={styles.field}>
                <input
                  type="checkbox"
                  checked={selectedElement.locked}
                  onChange={(event) => setState((current) => setLockedOn(current, side, selectedElement.id, event.target.checked))}
                />
                Locked
              </label>

              <button type="button" onClick={removeSelected}>
                Remove element
              </button>
            </div>
          ) : (
            <p className={styles.hint}>Select an element to edit its position, size, rotation, layer or lock state.</p>
          )}
        </aside>
      </div>
    </div>
  );
}
