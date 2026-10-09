'use client';

import { useEffect, useState, type CSSProperties } from 'react';
import { STANDARD_FORMATS, mmToPx } from '../bio-crd-002/cardFormats';
import { bringForward, bringToFront, moveElement, rotateElement, sendBackward, sendToBack, setLocked } from './canvasElementOps';
import { updateTypography, type TypographyStyle } from './typography';
import {
  NAME_FIELD_BINDINGS,
  copyStyleTo,
  createNameFieldElement,
  isNameFieldElement,
  setBinding,
  setTypography,
  type NameFieldBinding,
  type NameFieldElement,
} from './nameFields';
import {
  WATERMARK_PRESETS,
  createWatermarkElement,
  createWatermarkFromPreset,
  isWatermarkElement,
  setContent,
  setOpacity,
  type WatermarkElement,
  type WatermarkPresetKind,
} from './watermarks';
import styles from './card-typography.module.css';

type AnyElement = NameFieldElement | WatermarkElement;

const CANVAS_WIDTH_PX = mmToPx(STANDARD_FORMATS.cr80.longEdgeMm);
const CANVAS_HEIGHT_PX = mmToPx(STANDARD_FORMATS.cr80.shortEdgeMm);
const CANVAS_MAX_ON_SCREEN_PX = 420;

let counter = 0;
function nextId(prefix: string): string {
  counter += 1;
  return `${prefix}-${counter}`;
}

function typographyCss(t: TypographyStyle): CSSProperties {
  return {
    fontFamily: t.fontFamily,
    fontSize: `${t.fontSizePt}pt`,
    color: t.color,
    fontWeight: t.bold ? 700 : 400,
    fontStyle: t.italic ? 'italic' : 'normal',
    textDecoration: t.underline ? 'underline' : 'none',
    textTransform: t.textCase === 'none' ? 'none' : t.textCase === 'titlecase' ? 'capitalize' : t.textCase,
    textAlign: t.textAlign,
    letterSpacing: `${t.letterSpacingPx}px`,
    opacity: t.opacityPercent / 100,
    width: '100%',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  };
}

const BINDING_LABELS: Record<NameFieldBinding, string> = {
  title: 'Title',
  firstName: 'First Name',
  middleName: 'Middle Name',
  lastName: 'Last Name',
  fullName: 'Full Name',
  displayName: 'Display Name',
};

const PRESET_KEYS: readonly WatermarkPresetKind[] = ['staff', 'student', 'visitor'];

export interface CardTypographyAppProps {
  /** BIO-CRD-008-AC05: additive, optional - see `CardDesignerApp`'s own identically-shaped prop for the full rationale (Kemi's "zero I/O" ruling: this component still does none itself). */
  onChange?: (elements: readonly AnyElement[]) => void;
}

export function CardTypographyApp({ onChange }: CardTypographyAppProps = {}) {
  const [elements, setElements] = useState<AnyElement[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [copySourceId, setCopySourceId] = useState<string>('');

  useEffect(() => {
    onChange?.(elements);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reports every state change; onChange identity is the caller's concern
  }, [elements]);

  const scale = Math.min(1, CANVAS_MAX_ON_SCREEN_PX / Math.max(CANVAS_WIDTH_PX, CANVAS_HEIGHT_PX));
  const selected = elements.find((e) => e.id === selectedId) ?? null;
  const nameFields = elements.filter(isNameFieldElement);

  function updateOne(id: string, update: (element: AnyElement) => AnyElement) {
    setElements((current) => current.map((e) => (e.id === id ? update(e) : e)));
  }

  function addNameField(binding: NameFieldBinding) {
    const id = nextId('field');
    const field = createNameFieldElement(id, binding, { xPx: 20, yPx: 20 + elements.length * 10, widthPx: 220, heightPx: 30 }, elements.length);
    setElements((current) => [...current, field]);
    setSelectedId(id);
  }

  function addWatermarkFromPreset(kind: WatermarkPresetKind) {
    const id = nextId('watermark');
    const watermark = createWatermarkFromPreset(id, kind, { xPx: 10, yPx: 10, widthPx: CANVAS_WIDTH_PX - 20, heightPx: CANVAS_HEIGHT_PX - 20 }, elements.length);
    setElements((current) => [...current, watermark]);
    setSelectedId(id);
  }

  function addCustomWatermark() {
    const id = nextId('watermark');
    const watermark = createWatermarkElement(id, 'text', 'WATERMARK', { xPx: 10, yPx: 10, widthPx: CANVAS_WIDTH_PX - 20, heightPx: CANVAS_HEIGHT_PX - 20 }, elements.length);
    setElements((current) => [...current, watermark]);
    setSelectedId(id);
  }

  function removeSelected() {
    if (!selectedId) return;
    setElements((current) => current.filter((e) => e.id !== selectedId));
    setSelectedId(null);
  }

  function applyCopyStyle() {
    const source = nameFields.find((f) => f.id === copySourceId);
    if (!source || !selected || !isNameFieldElement(selected)) return;
    updateOne(selected.id, (e) => copyStyleTo(source, e as NameFieldElement));
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
    updateOne(selectedId, (e) => moveElement(e, delta[0], delta[1]));
  }

  return (
    <div className={styles.app} data-testid="bio-crd-003-app">
      <h2>Card Typography, Name Fields &amp; Watermarks</h2>
      <p>
        Independent Title/First/Middle/Last/Full/Display Name fields with per-field typography,
        and text/image watermarks with opacity, rotation and layer control (BIO-CRD-003). Canvas
        shown at CR80 landscape size for context.
      </p>

      <div className={styles.toolbar}>
        <div className={styles.toolbarGroup} aria-label="Add a name field">
          {NAME_FIELD_BINDINGS.map((binding) => (
            <button key={binding} type="button" onClick={() => addNameField(binding)}>
              + {BINDING_LABELS[binding]}
            </button>
          ))}
        </div>
        <div className={styles.toolbarGroup} aria-label="Add a watermark">
          {PRESET_KEYS.map((kind) => (
            <button key={kind} type="button" onClick={() => addWatermarkFromPreset(kind)}>
              + {WATERMARK_PRESETS[kind].text} watermark
            </button>
          ))}
          <button type="button" onClick={addCustomWatermark}>
            + Custom watermark
          </button>
        </div>
      </div>

      <div className={styles.workspace}>
        <div
          className={styles.canvasFrame}
          style={{ width: CANVAS_WIDTH_PX * scale, height: CANVAS_HEIGHT_PX * scale }}
          role="group"
          aria-label="Card typography canvas"
        >
          <div className={styles.canvasSurface} style={{ width: CANVAS_WIDTH_PX, height: CANVAS_HEIGHT_PX, transform: `scale(${scale})` }}>
            {[...elements]
              .sort((a, b) => a.zIndex - b.zIndex)
              .map((element) =>
                isNameFieldElement(element) ? (
                  <button
                    key={element.id}
                    type="button"
                    className={styles.element}
                    data-selected={element.id === selectedId}
                    onClick={() => setSelectedId(element.id)}
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
                    <span style={typographyCss(element.typography)}>[{BINDING_LABELS[element.binding]}]</span>
                  </button>
                ) : (
                  <button
                    key={element.id}
                    type="button"
                    className={`${styles.element} ${styles.watermarkElement}`}
                    data-selected={element.id === selectedId}
                    onClick={() => setSelectedId(element.id)}
                    aria-pressed={element.id === selectedId}
                    style={{
                      left: element.xPx,
                      top: element.yPx,
                      width: element.widthPx,
                      height: element.heightPx,
                      transform: `rotate(${element.rotationDeg}deg)`,
                      zIndex: element.zIndex,
                      opacity: element.opacityPercent / 100,
                    }}
                  >
                    {element.content}
                  </button>
                ),
              )}
          </div>
        </div>

        <aside className={styles.sidebar} aria-label="Card typography layers and properties">
          <h2>Layers</h2>
          <ul className={styles.layerList}>
            {[...elements]
              .sort((a, b) => b.zIndex - a.zIndex)
              .map((element) => (
                <li key={element.id}>
                  <button
                    type="button"
                    className={styles.layerListItem}
                    aria-pressed={element.id === selectedId}
                    onClick={() => setSelectedId(element.id)}
                  >
                    {isNameFieldElement(element) ? BINDING_LABELS[element.binding] : element.content}
                    {element.locked ? ' 🔒' : ''}
                  </button>
                </li>
              ))}
          </ul>

          {selected ? (
            // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions
            <div className={styles.properties} onKeyDown={handlePropertiesKeyDown}>
              <h2>Properties</h2>
              <p className={styles.hint}>Use the arrow keys anywhere in this panel to nudge the selected element.</p>

              {isNameFieldElement(selected) ? (
                <NameFieldProperties
                  field={selected}
                  onChangeBinding={(binding) => updateOne(selected.id, (e) => setBinding(e as NameFieldElement, binding))}
                  onChangeTypography={(typography) => updateOne(selected.id, (e) => setTypography(e as NameFieldElement, typography))}
                  copySourceId={copySourceId}
                  onChangeCopySourceId={setCopySourceId}
                  copyCandidates={nameFields.filter((f) => f.id !== selected.id)}
                  onApplyCopyStyle={applyCopyStyle}
                />
              ) : (
                <WatermarkProperties
                  watermark={selected}
                  onChangeContent={(content) => updateOne(selected.id, (e) => setContent(e as WatermarkElement, content))}
                  onChangeOpacity={(opacity) => updateOne(selected.id, (e) => setOpacity(e as WatermarkElement, opacity))}
                />
              )}

              <div className={styles.buttonRow}>
                <button type="button" disabled={selected.locked} onClick={() => updateOne(selected.id, (e) => rotateElement(e, -15))}>
                  Rotate -15°
                </button>
                <button type="button" disabled={selected.locked} onClick={() => updateOne(selected.id, (e) => rotateElement(e, 15))}>
                  Rotate +15°
                </button>
              </div>

              <div className={styles.buttonRow}>
                <button type="button" onClick={() => setElements((current) => bringToFront(current, selected.id))}>Bring to front</button>
                <button type="button" onClick={() => setElements((current) => bringForward(current, selected.id))}>Forward</button>
                <button type="button" onClick={() => setElements((current) => sendBackward(current, selected.id))}>Backward</button>
                <button type="button" onClick={() => setElements((current) => sendToBack(current, selected.id))}>Send to back</button>
              </div>

              <label className={styles.field}>
                <input
                  type="checkbox"
                  checked={selected.locked}
                  onChange={(event) => updateOne(selected.id, (e) => setLocked(e, event.target.checked))}
                />
                Locked
              </label>

              <button type="button" onClick={removeSelected}>
                Remove element
              </button>
            </div>
          ) : (
            <p className={styles.hint}>Choose a name field or watermark above to adjust its typography, watermark settings, position, rotation, layer or lock state.</p>
          )}
        </aside>
      </div>
    </div>
  );
}

function NameFieldProperties({
  field,
  onChangeBinding,
  onChangeTypography,
  copySourceId,
  onChangeCopySourceId,
  copyCandidates,
  onApplyCopyStyle,
}: {
  field: NameFieldElement;
  onChangeBinding: (binding: NameFieldBinding) => void;
  onChangeTypography: (typography: TypographyStyle) => void;
  copySourceId: string;
  onChangeCopySourceId: (id: string) => void;
  copyCandidates: NameFieldElement[];
  onApplyCopyStyle: () => void;
}) {
  const t = field.typography;
  function change(partial: Partial<TypographyStyle>) {
    try {
      onChangeTypography(updateTypography(t, partial));
    } catch {
      // Out-of-range input (e.g. a font size the browser's own number input still let through
      // mid-edit) is simply not applied - the last valid typography stays in effect.
    }
  }

  return (
    <>
      <label className={styles.field}>
        Data binding
        <select value={field.binding} onChange={(event) => onChangeBinding(event.target.value as NameFieldBinding)}>
          {NAME_FIELD_BINDINGS.map((binding) => (
            <option key={binding} value={binding}>
              {BINDING_LABELS[binding]}
            </option>
          ))}
        </select>
      </label>

      <label className={styles.field}>
        Font family
        <input type="text" value={t.fontFamily} onChange={(event) => change({ fontFamily: event.target.value })} />
      </label>
      <label className={styles.field}>
        Font size (pt)
        <input type="number" min={1} value={t.fontSizePt} onChange={(event) => change({ fontSizePt: Number(event.target.value) })} />
      </label>
      <label className={styles.field}>
        Colour
        <input type="color" value={t.color} onChange={(event) => change({ color: event.target.value })} />
      </label>

      <div className={styles.toggleRow}>
        <label className={styles.field}>
          <input type="checkbox" checked={t.bold} onChange={(event) => change({ bold: event.target.checked })} />
          Bold
        </label>
        <label className={styles.field}>
          <input type="checkbox" checked={t.italic} onChange={(event) => change({ italic: event.target.checked })} />
          Italic
        </label>
        <label className={styles.field}>
          <input type="checkbox" checked={t.underline} onChange={(event) => change({ underline: event.target.checked })} />
          Underline
        </label>
      </div>

      <label className={styles.field}>
        Case
        <select value={t.textCase} onChange={(event) => change({ textCase: event.target.value as TypographyStyle['textCase'] })}>
          <option value="none">None</option>
          <option value="uppercase">UPPERCASE</option>
          <option value="lowercase">lowercase</option>
          <option value="titlecase">Title Case</option>
        </select>
      </label>
      <label className={styles.field}>
        Alignment
        <select value={t.textAlign} onChange={(event) => change({ textAlign: event.target.value as TypographyStyle['textAlign'] })}>
          <option value="left">Left</option>
          <option value="center">Center</option>
          <option value="right">Right</option>
        </select>
      </label>
      <label className={styles.field}>
        Letter spacing (px)
        <input type="number" value={t.letterSpacingPx} onChange={(event) => change({ letterSpacingPx: Number(event.target.value) })} />
      </label>
      <label className={styles.field}>
        Opacity (%)
        <input type="number" min={0} max={100} value={t.opacityPercent} onChange={(event) => change({ opacityPercent: Number(event.target.value) })} />
      </label>

      {copyCandidates.length > 0 ? (
        <div className={styles.field}>
          <label htmlFor="bio-crd-003-copy-style-source">Copy style from</label>
          <div className={styles.buttonRow}>
            <select id="bio-crd-003-copy-style-source" value={copySourceId} onChange={(event) => onChangeCopySourceId(event.target.value)}>
              <option value="">Choose a field...</option>
              {copyCandidates.map((candidate) => (
                <option key={candidate.id} value={candidate.id}>
                  {BINDING_LABELS[candidate.binding]}
                </option>
              ))}
            </select>
            <button type="button" onClick={onApplyCopyStyle} disabled={!copySourceId}>
              Apply style (binding unaffected)
            </button>
          </div>
        </div>
      ) : null}
    </>
  );
}

function WatermarkProperties({
  watermark,
  onChangeContent,
  onChangeOpacity,
}: {
  watermark: WatermarkElement;
  onChangeContent: (content: string) => void;
  onChangeOpacity: (opacity: number) => void;
}) {
  return (
    <>
      <label className={styles.field}>
        Text
        <input type="text" value={watermark.content} onChange={(event) => onChangeContent(event.target.value)} />
      </label>
      <label className={styles.field}>
        Opacity (%)
        <input
          type="number"
          min={0}
          max={100}
          value={watermark.opacityPercent}
          onChange={(event) => {
            const value = Number(event.target.value);
            if (value >= 0 && value <= 100) onChangeOpacity(value);
          }}
        />
      </label>
    </>
  );
}
