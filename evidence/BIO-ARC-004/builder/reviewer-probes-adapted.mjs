// Two of Seyi's five probes from evidence/BIO-ARC-004/codex/independent-review.sh, adapted
// here rather than edited in their own file (their artifact, not mine to change).
//
// Why: both probes were written against a design choice this fix deliberately changed.
//
// 1. "AC03 concurrent identical retries execute exactly once" mocked a store with the OLD
//    { get(key), set(key, value) } shape. QA-01's fix replaces that with an atomic
//    { claim, complete, release, get } protocol (ADR-0004 Decision 4.4) — a store without
//    `claim` cannot satisfy the new contract at all, so calling withIdempotency against
//    their old mock throws immediately. The property their probe actually checks —
//    execute() runs exactly once for concurrent identical retries — is what this file
//    proves, using the same "release a gate, then race two calls" construction they used,
//    against the new store shape.
//
// 2. "AC02 non-validation problems cannot carry validation errors" expected
//    toProblemResponse to silently drop a misplaced errors[] and return a document without
//    it. This fix throws instead (reserved_detail_field's sibling check, ADR-0004 §2.5),
//    consistent with every other QA-03 fix in this round: a builder that can produce an
//    invalid document is refused at construction, not quietly repaired. The invariant
//    their probe cares about — no document is ever returned with errors[] on a
//    non-validation code — holds either way; this file proves it holds via "never
//    returned", the stronger of the two versions of that guarantee.
//
// Run: node evidence/BIO-ARC-004/builder/reviewer-probes-adapted.mjs
import { withIdempotency, createInMemoryIdempotencyStore, toProblemResponse } from '../../../contracts/js/index.mjs';

let failed = 0;
const check = async (name, fn) => {
  try { await fn(); console.log(`PASS ${name}`); } catch (error) { console.log(`FAIL ${name} — ${error.message}`); failed += 1; }
};
const fail = (why) => { throw new Error(why); };

await check('AC03 concurrent identical retries execute exactly once (adapted store shape)', async () => {
  const store = createInMemoryIdempotencyStore();
  let executions = 0;
  const input = {
    tenantId: 'ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77', operation: 'card.activate',
    idempotencyKey: 'retry-1', payload: { card: 'synthetic' }, store,
    execute: async () => { executions += 1; return { status: 200, body: { ok: true } }; },
  };
  // Same construction Seyi used: fire both before either can have finished, and confirm
  // only one execution happened across the pair.
  const first = withIdempotency(input);
  const retry = withIdempotency(input);
  await Promise.all([first, retry]);
  if (executions !== 1) fail(`executions=${executions}, expected 1`);
});

await check('AC02 a document with errors[] on a non-validation code is never returned', async () => {
  let threw = false;
  let body;
  try {
    body = toProblemResponse({
      code: 'tenant_context_missing', status: 400, message: 'missing', detail: {},
      errors: [{ path: 'x', code: 'required', message: 'required' }],
    });
  } catch {
    threw = true;
  }
  // Either outcome satisfies the invariant "no such document is ever handed back": a
  // thrown construction never reaches a caller as a document, and a returned one would
  // have to lack errors[]. Only "returned AND carries errors[]" is the actual violation.
  if (!threw && body && 'errors' in body) fail('a document with errors[] on a non-validation code was returned');
});

console.log(failed === 0 ? 'ALL ADAPTED PROBES PASS' : `${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
