'use client';

import { useState } from 'react';
import { CardDesignerApp } from '../../features/bio-crd-002/CardDesignerApp';
import { createInitialDesignState, type CardDesignState } from '../../features/bio-crd-002/cardDesignState';
import { CardTypographyApp } from '../../features/bio-crd-003/CardTypographyApp';
import type { NameFieldElement } from '../../features/bio-crd-003/nameFields';
import type { WatermarkElement } from '../../features/bio-crd-003/watermarks';
import { CardObjectsApp } from '../../features/bio-crd-004/CardObjectsApp';
import type { BarcodeElement } from '../../features/bio-crd-004/barcodeObjects';
import type { SignatureElement } from '../../features/bio-crd-004/signatureObjects';
import type { LogoElement } from '../../features/bio-crd-004/logoObjects';
import { CardPhotoApp } from '../../features/bio-crd-009/CardPhotoApp';
import { createInitialPhotoState, type PhotoDesignState } from '../../features/bio-crd-009/photoDesignState';
import { SaveDesignBar } from '../../features/cards/SaveDesignBar';
import { TemplateLifecycleBar } from '../../features/bio-crd-008/TemplateLifecycleBar';

/**
 * BIO-CRD-002: the real `/card-designer` route the nav has pointed at since BIO-FND-007
 * (`web/lib/nav-items.ts`), previously 404-ing to the shell's own not-found page
 * (`docs/MANUAL.md`: "None of these routes resolve to a real page yet ... until the story that
 * owns that area builds it").
 *
 * BIO-CRD-003 adds its own typography/name-field/watermark tool as a second section on this
 * same page, rather than reopening `CardDesignerApp.tsx` (owned by BIO-CRD-002, outside this
 * story's own declared write scope) or splitting into a separate route away from the nav's one
 * `/card-designer` entry. This file itself is not declared as owned by either story in
 * `governance/feature-path-ownership.json` - a shared, unlisted entry point every
 * `card-designer`-conflict-group story updates, the same role `Program.cs`/`Biolog.Api.csproj`
 * play for every backend story. See `docs/features/bio-crd-003/README.md` "Two coordinated
 * tools, not yet one merged canvas" for the disclosed scope boundary this composition choice
 * carries.
 *
 * BIO-CRD-004 adds a third, independent section (dynamic barcode/QR, versioned signature
 * references, logo, all with conditional visibility) for the same reason - it does not reopen
 * either sibling's own component. See `docs/features/bio-crd-004/README.md` for this story's
 * own disclosed scope boundary.
 *
 * BIO-UX-007 adds a fourth, independent section: `SaveDesignBar` (AC06's "stable service ports
 * for later card lifecycle, production and encoding integrations" applied to Card Designer).
 * It does not read or touch any of the three canvas apps' own internal design state - per
 * Kemi's ruling, the canvas itself stays pure client-side with zero I/O; this is only a real,
 * working "Save design" action for the route's own primary action name (`route-manifest.ts`),
 * not a redesign of the canvas. See `docs/features/bio-ux-007/README.md`.
 *
 * BIO-CRD-008 adds a fifth, final section: `TemplateLifecycleBar`, the real Draft->Validation->
 * Approval->Published->Retired lifecycle AC05 requires. This IS the first section to read the
 * three canvas apps' own state - each canvas app itself still does zero I/O (per Kemi's ruling,
 * untouched); only this PAGE (already the shared, unlisted coordination point every sibling
 * story above already edits) now holds a small amount of lifted state via each canvas's own new,
 * additive `onChange` prop, and only `TemplateLifecycleBar` (this story's own new file) reads it
 * to build a template snapshot. `SaveDesignBar` itself is untouched, still the real action behind
 * `route-manifest.ts`'s own "Save design" name. See docs/features/bio-crd-008/README.md.
 *
 * BIO-CRD-009 adds a sixth section: `CardPhotoApp`, a real positionable cardholder photo element
 * (front/back independent, unlike its BIO-CRD-003/004 siblings) - the same "own canvas app, own
 * onChange prop, zero I/O" shape every section above already follows. `TemplateLifecycleBar`
 * gains one new optional `photoElements` prop so its snapshot capture (AC04) includes a placed
 * photo the same way it already includes a logo. See docs/features/bio-crd-009/README.md.
 */
export default function CardDesignerPage() {
  const [designState, setDesignState] = useState<CardDesignState>(() => createInitialDesignState('cr80', 'landscape'));
  const [typographyElements, setTypographyElements] = useState<readonly (NameFieldElement | WatermarkElement)[]>([]);
  const [objectElements, setObjectElements] = useState<readonly (BarcodeElement | SignatureElement | LogoElement)[]>([]);
  const [photoState, setPhotoState] = useState<PhotoDesignState>(() => createInitialPhotoState());

  return (
    <>
      <CardDesignerApp onChange={setDesignState} />
      <hr />
      <CardTypographyApp onChange={setTypographyElements} />
      <hr />
      <CardObjectsApp onChange={setObjectElements} />
      <hr />
      <CardPhotoApp onChange={setPhotoState} />
      <hr />
      <SaveDesignBar />
      <hr />
      <TemplateLifecycleBar
        designState={designState}
        typographyElements={typographyElements}
        objectElements={objectElements}
        photoElements={photoState}
      />
    </>
  );
}
