'use client';

import { useEffect, useState, type CSSProperties } from 'react';
import { STANDARD_FORMATS, mmToPx } from '../bio-crd-002/cardFormats';
import { bringForward, bringToFront, moveElement, rotateElement, sendBackward, sendToBack, setLocked } from './canvasElementOps';
import {
  ALL_BARCODE_PROFILES,
  DEFAULT_BARCODE_PROFILE,
  createBarcodeElement,
  isBarcodeElement,
  setColors,
  setProfile,
  setQuietZoneMm,
  setSizeMm,
  setVisibilityRule as setBarcodeVisibilityRule,
  validate,
  type BarcodeElement,
  type BarcodeProfile,
} from './barcodeObjects';
import { createSignatureElement, isSignatureElement, setVersion, setVisibilityRule as setSignatureVisibilityRule, type SignatureElement } from './signatureObjects';
import { createLogoElement, isLogoElement, setContent, setVisibilityRule as setLogoVisibilityRule, type LogoElement } from './logoObjects';
import { CARD_CLASSES, isVisible, type CardClass, type VisibilityRule } from './visibility';
import styles from './card-objects.module.css';

type AnyElement = BarcodeElement | SignatureElement | LogoElement;

const CANVAS_WIDTH_PX = mmToPx(STANDARD_FORMATS.cr80.longEdgeMm);
const CANVAS_HEIGHT_PX = mmToPx(STANDARD_FORMATS.cr80.shortEdgeMm);
const CANVAS_MAX_ON_SCREEN_PX = 420;

const PROFILE_LABELS: Record<BarcodeProfile, string> = {
  alphanumeric: 'Alphanumeric',
  qr: 'QR',
  pdf417: 'PDF417',
  dataMatrix: 'Data Matrix',
  aztec: 'Aztec',
};

let counter = 0;
function nextId(prefix: string): string {
  counter += 1;
  return `${prefix}-${counter}`;
}

function elementLabel(element: AnyElement): string {
  if (isBarcodeElement(element)) return `Barcode (${PROFILE_LABELS[element.profile]})`;
  if (isSignatureElement(element)) return `Signature v${element.version}`;
  return `Logo (${element.content})`;
}

function setVisibilityRuleGeneric(element: AnyElement, rule: VisibilityRule): AnyElement {
  if (isBarcodeElement(element)) return setBarcodeVisibilityRule(element, rule);
  if (isSignatureElement(element)) return setSignatureVisibilityRule(element, rule);
  return setLogoVisibilityRule(element, rule);
}

export interface CardObjectsAppProps {
  /** BIO-CRD-008-AC05: additive, optional - see `CardDesignerApp`'s own identically-shaped prop for the full rationale (Kemi's "zero I/O" ruling: this component still does none itself). */
  onChange?: (elements: readonly AnyElement[]) => void;
}

export function CardObjectsApp({ onChange }: CardObjectsAppProps = {}) {
  const [elements, setElements] = useState<AnyElement[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [previewCardClass, setPreviewCardClass] = useState<CardClass>('staff');
  const [previewProfile, setPreviewProfile] = useState<BarcodeProfile>(DEFAULT_BARCODE_PROFILE);
  const [newSignatureLineageId, setNewSignatureLineageId] = useState('lineage-1');
  const [newSignatureVersion, setNewSignatureVersion] = useState(1);
  const [newLogoContent, setNewLogoContent] = useState('brand-asset-1');

  useEffect(() => {
    onChange?.(elements);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reports every state change; onChange identity is the caller's concern
  }, [elements]);

  const scale = Math.min(1, CANVAS_MAX_ON_SCREEN_PX / Math.max(CANVAS_WIDTH_PX, CANVAS_HEIGHT_PX));
  const selected = elements.find((e) => e.id === selectedId) ?? null;

  function updateOne(id: string, update: (element: AnyElement) => AnyElement) {
    setElements((current) => current.map((e) => (e.id === id ? update(e) : e)));
  }

  function addBarcode(profile: BarcodeProfile) {
    const id = nextId('barcode');
    const element = createBarcodeElement(id, { xPx: 20, yPx: 20 + elements.length * 10, widthPx: 100, heightPx: 40 }, elements.length, profile);
    setElements((current) => [...current, element]);
    setSelectedId(id);
  }

  function addSignature() {
    if (!newSignatureLineageId.trim() || newSignatureVersion < 1) return;
    const id = nextId('signature');
    const element = createSignatureElement(id, newSignatureLineageId.trim(), newSignatureVersion, { xPx: 20, yPx: 20 + elements.length * 10, widthPx: 80, heightPx: 30 }, elements.length);
    setElements((current) => [...current, element]);
    setSelectedId(id);
  }

  function addLogo() {
    if (!newLogoContent.trim()) return;
    const id = nextId('logo');
    const element = createLogoElement(id, newLogoContent.trim(), { xPx: 20, yPx: 20 + elements.length * 10, widthPx: 40, heightPx: 40 }, elements.length);
    setElements((current) => [...current, element]);
    setSelectedId(id);
  }

  function removeSelected() {
    if (!selectedId) return;
    setElements((current) => current.filter((e) => e.id !== selectedId));
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
    updateOne(selectedId, (e) => moveElement(e, delta[0], delta[1]));
  }

  return (
    <div className={styles.app} data-testid="bio-crd-004-app">
      <h2>Card Objects: Barcode, Signature &amp; Logo</h2>
      <p>
        Dynamic barcode/QR objects with quiet-zone/size/contrast validation, versioned signature
        references and conditionally-visible card objects (BIO-CRD-004). Canvas shown at CR80
        landscape size for context.
      </p>

      <div className={styles.toolbar}>
        <div className={styles.toolbarGroup} aria-label="Add a barcode">
          <button type="button" onClick={() => addBarcode(DEFAULT_BARCODE_PROFILE)}>
            + Barcode (default: alphanumeric)
          </button>
          {ALL_BARCODE_PROFILES.filter((p) => p !== DEFAULT_BARCODE_PROFILE).map((profile) => (
            <button key={profile} type="button" onClick={() => addBarcode(profile)}>
              + {PROFILE_LABELS[profile]} barcode
            </button>
          ))}
        </div>

        <div className={styles.toolbarGroup} aria-label="Add a signature">
          <label className={styles.field}>
            New signature lineage id
            <input type="text" value={newSignatureLineageId} onChange={(event) => setNewSignatureLineageId(event.target.value)} />
          </label>
          <label className={styles.field}>
            New signature version
            <input type="number" min={1} value={newSignatureVersion} onChange={(event) => setNewSignatureVersion(Number(event.target.value))} />
          </label>
          <button type="button" onClick={addSignature}>
            + Signature
          </button>
        </div>

        <div className={styles.toolbarGroup} aria-label="Add a logo">
          <label className={styles.field}>
            Asset reference
            <input type="text" value={newLogoContent} onChange={(event) => setNewLogoContent(event.target.value)} />
          </label>
          <button type="button" onClick={addLogo}>
            + Logo
          </button>
        </div>

        <div className={styles.toolbarGroup} aria-label="Preview context">
          <label className={styles.field}>
            Preview card class
            <select value={previewCardClass} onChange={(event) => setPreviewCardClass(event.target.value as CardClass)}>
              {CARD_CLASSES.map((cardClass) => (
                <option key={cardClass} value={cardClass}>
                  {cardClass}
                </option>
              ))}
            </select>
          </label>
          <label className={styles.field}>
            Preview symbology
            <select value={previewProfile} onChange={(event) => setPreviewProfile(event.target.value as BarcodeProfile)}>
              {ALL_BARCODE_PROFILES.map((profile) => (
                <option key={profile} value={profile}>
                  {PROFILE_LABELS[profile]}
                </option>
              ))}
            </select>
          </label>
        </div>
      </div>

      <div className={styles.workspace}>
        <div
          className={styles.canvasFrame}
          style={{ width: CANVAS_WIDTH_PX * scale, height: CANVAS_HEIGHT_PX * scale }}
          role="group"
          aria-label="Card objects canvas"
        >
          <div className={styles.canvasSurface} style={{ width: CANVAS_WIDTH_PX, height: CANVAS_HEIGHT_PX, transform: `scale(${scale})` }}>
            {[...elements]
              .sort((a, b) => a.zIndex - b.zIndex)
              .map((element) => {
                const visible = isVisible(element.visibilityRule, { cardClass: previewCardClass, profile: previewProfile });
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
                    data-hidden={!visible}
                    onClick={() => setSelectedId(element.id)}
                    aria-pressed={element.id === selectedId}
                    style={style}
                  >
                    {elementLabel(element)}
                  </button>
                );
              })}
          </div>
        </div>

        <aside className={styles.sidebar} aria-label="Card object layers and properties">
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
                    {elementLabel(element)}
                    {element.locked ? ' \u{1F512}' : ''}
                  </button>
                </li>
              ))}
          </ul>

          {selected ? (
            // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions
            <div className={styles.properties} onKeyDown={handlePropertiesKeyDown}>
              <h2>Properties</h2>
              <p className={styles.hint}>Use the arrow keys anywhere in this panel to nudge the selected object.</p>

              {isBarcodeElement(selected) ? (
                <BarcodeProperties
                  element={selected}
                  onChangeProfile={(profile) => updateOne(selected.id, (e) => setProfile(e as BarcodeElement, profile))}
                  onChangeQuietZone={(mm) => updateOne(selected.id, (e) => setQuietZoneMm(e as BarcodeElement, mm))}
                  onChangeSize={(w, h) => updateOne(selected.id, (e) => setSizeMm(e as BarcodeElement, w, h))}
                  onChangeColors={(fg, bg) => updateOne(selected.id, (e) => setColors(e as BarcodeElement, fg, bg))}
                />
              ) : isSignatureElement(selected) ? (
                <SignatureProperties
                  element={selected}
                  onChangeVersion={(version) => updateOne(selected.id, (e) => setVersion(e as SignatureElement, version))}
                />
              ) : (
                <LogoProperties
                  element={selected}
                  onChangeContent={(content) => updateOne(selected.id, (e) => setContent(e as LogoElement, content))}
                />
              )}

              <VisibilityRuleEditor
                rule={selected.visibilityRule}
                onChange={(rule) => updateOne(selected.id, (e) => setVisibilityRuleGeneric(e, rule))}
              />

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
                Remove object
              </button>
            </div>
          ) : (
            <p className={styles.hint}>Choose a barcode, signature or logo above to adjust its properties, visibility, position, rotation or layer.</p>
          )}
        </aside>
      </div>
    </div>
  );
}

function BarcodeProperties({
  element,
  onChangeProfile,
  onChangeQuietZone,
  onChangeSize,
  onChangeColors,
}: {
  element: BarcodeElement;
  onChangeProfile: (profile: BarcodeProfile) => void;
  onChangeQuietZone: (mm: number) => void;
  onChangeSize: (widthMm: number, heightMm: number) => void;
  onChangeColors: (foreground: string, background: string) => void;
}) {
  const result = validate(element);

  return (
    <>
      <label className={styles.field}>
        Barcode profile
        <select value={element.profile} onChange={(event) => onChangeProfile(event.target.value as BarcodeProfile)}>
          {ALL_BARCODE_PROFILES.map((profile) => (
            <option key={profile} value={profile}>
              {PROFILE_LABELS[profile]}
            </option>
          ))}
        </select>
      </label>
      <label className={styles.field}>
        Quiet zone (mm)
        <input type="number" step={0.1} value={element.quietZoneMm} onChange={(event) => onChangeQuietZone(Number(event.target.value))} />
      </label>
      <label className={styles.field}>
        Width (mm)
        <input type="number" step={0.1} value={element.widthMm} onChange={(event) => onChangeSize(Number(event.target.value), element.heightMm)} />
      </label>
      <label className={styles.field}>
        Height (mm)
        <input type="number" step={0.1} value={element.heightMm} onChange={(event) => onChangeSize(element.widthMm, Number(event.target.value))} />
      </label>
      <label className={styles.field}>
        Foreground colour
        <input type="color" value={element.foregroundColorHex} onChange={(event) => onChangeColors(event.target.value, element.backgroundColorHex)} />
      </label>
      <label className={styles.field}>
        Background colour
        <input type="color" value={element.backgroundColorHex} onChange={(event) => onChangeColors(element.foregroundColorHex, event.target.value)} />
      </label>
      {result.isValid ? (
        <p className={styles.validationOk} data-testid="barcode-validation-ok">Valid: quiet zone, size and contrast all pass.</p>
      ) : (
        <ul className={styles.validationError} data-testid="barcode-validation-violations">
          {result.violations.map((violation) => (
            <li key={violation}>{violation}</li>
          ))}
        </ul>
      )}
    </>
  );
}

function SignatureProperties({ element, onChangeVersion }: { element: SignatureElement; onChangeVersion: (version: number) => void }) {
  return (
    <>
      <label className={styles.field}>
        Lineage id
        <input type="text" value={element.lineageId} readOnly />
      </label>
      <label className={styles.field}>
        Version
        <input
          type="number"
          min={1}
          value={element.version}
          onChange={(event) => {
            const value = Number(event.target.value);
            if (value >= 1) onChangeVersion(value);
          }}
        />
      </label>
    </>
  );
}

function LogoProperties({ element, onChangeContent }: { element: LogoElement; onChangeContent: (content: string) => void }) {
  return (
    <label className={styles.field}>
      Asset reference
      <input type="text" value={element.content} onChange={(event) => onChangeContent(event.target.value)} />
    </label>
  );
}

function VisibilityRuleEditor({ rule, onChange }: { rule: VisibilityRule; onChange: (rule: VisibilityRule) => void }) {
  const restrictedByClass = rule.cardClasses !== null;
  const restrictedByProfile = rule.profiles !== null;

  return (
    <div className={styles.properties}>
      <h3>Conditional visibility</h3>
      <label className={styles.field}>
        <input
          type="checkbox"
          checked={restrictedByClass}
          onChange={(event) => onChange({ ...rule, cardClasses: event.target.checked ? [CARD_CLASSES[0]!] : null })}
        />
        Restrict to specific card classes
      </label>
      {restrictedByClass ? (
        <div className={styles.toggleRow}>
          {CARD_CLASSES.map((cardClass) => (
            <label key={cardClass} className={styles.field}>
              <input
                type="checkbox"
                checked={rule.cardClasses!.includes(cardClass)}
                onChange={(event) => {
                  const next = event.target.checked
                    ? [...rule.cardClasses!, cardClass]
                    : rule.cardClasses!.filter((c) => c !== cardClass);
                  onChange({ ...rule, cardClasses: next });
                }}
              />
              {cardClass}
            </label>
          ))}
        </div>
      ) : null}

      <label className={styles.field}>
        <input
          type="checkbox"
          checked={restrictedByProfile}
          onChange={(event) => onChange({ ...rule, profiles: event.target.checked ? [DEFAULT_BARCODE_PROFILE] : null })}
        />
        Restrict to specific symbologies
      </label>
      {restrictedByProfile ? (
        <div className={styles.toggleRow}>
          {ALL_BARCODE_PROFILES.map((profile) => (
            <label key={profile} className={styles.field}>
              <input
                type="checkbox"
                checked={rule.profiles!.includes(profile)}
                onChange={(event) => {
                  const next = event.target.checked
                    ? [...rule.profiles!, profile]
                    : rule.profiles!.filter((p) => p !== profile);
                  onChange({ ...rule, profiles: next });
                }}
              />
              {PROFILE_LABELS[profile]}
            </label>
          ))}
        </div>
      ) : null}
    </div>
  );
}
