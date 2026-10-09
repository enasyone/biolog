#!/usr/bin/env python3
"""Strict machine-enforced validation of Munder governance (BIO-FND-005).
Exit 0 only if AC01-AC05 hold structurally. Usage: validate-governance.py [repo_root]"""
import json,sys,subprocess,os
MISSING=object()
root=sys.argv[1] if len(sys.argv)>1 else os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
j=os.path.join(root,"governance","munder-agents.json")
errs=[]
try: d=json.load(open(j))
except Exception as e: print(f"FAIL cannot load {j}: {e}"); sys.exit(2)
agents=d.get("agents",[]); rules=d.get("rules",{})
by={a.get("name"):a for a in agents}

# Canonical required roster (name -> role). AC01 requires EXACTLY these 12.
REQUIRED={
 "Chinedu":"Programme Orchestrator","Kemi":"Chief Architect & Integrator","Tunde":"Identity & Tenant Engineer",
 "Ngozi":"Credential & Card Engineer","Amina":"Biometrics Engineer","Ifeanyi":"Document Intelligence Engineer",
 "Busola":"Mobile & Edge Engineer","Emeka":"Platform & Infrastructure Engineer","Seyi":"Adversarial QA Engineer",
 "Zainab":"Security Engineer","Femi":"Performance & Resilience Engineer","Chioma":"Acceptance & Traceability Auditor"}

# AC01: exactly the required 12 names, each with matching role + engine/model + permissions dict
names=[a.get("name") for a in agents]
if len(agents)!=12: errs.append(f"AC01 expected 12 agents, got {len(agents)}")
if set(names)!=set(REQUIRED): errs.append(f"AC01 roster names must be exactly the canonical 12; diff={set(names)^set(REQUIRED)}")
for a in agents:
    n=a.get("name")
    if n in REQUIRED and a.get("role")!=REQUIRED[n]: errs.append(f"AC01 {n} role must be '{REQUIRED[n]}', got '{a.get('role')}'")
    for k in ("engine","model"):
        if not a.get(k): errs.append(f"AC01 {n} missing {k}")
    if not isinstance(a.get("permissions"),dict): errs.append(f"AC01 {n} permissions not an object")

# AC02: structured dispatch eligibility
de=rules.get("dispatch_eligibility")
if not isinstance(de,dict): errs.append("AC02 dispatch_eligibility must be a structured object")
else:
    if de.get("require_dependencies_done") is not True: errs.append("AC02 require_dependencies_done must be true")
    if de.get("respect_conflict_locks") is not True: errs.append("AC02 respect_conflict_locks must be true")
    if de.get("allow_incomplete_dependencies") is not False: errs.append("AC02 allow_incomplete_dependencies must be false")

# AC03: exactly one approver = Kemi, and Kemi owns migration sequencing
appr=[a["name"] for a in agents if a.get("permissions",{}).get("approve_shared_contracts") is True]
if appr!=["Kemi"]: errs.append(f"AC03 sole approver must be Kemi, got {appr}")
kemi=by.get("Kemi",{})
if "migrations" not in (kemi.get("permissions",{}).get("write_scope") or []): errs.append("AC03 Kemi write_scope must include 'migrations'")
if rules.get("shared_contract_authority",{}).get("sole_approver")!="Kemi": errs.append("AC03 rule sole_approver must be Kemi")

# AC04: every agent must have self_accept key present AND boolean False (missing/non-bool/true => fail)
for a in agents:
    v=a.get("permissions",{}).get("self_accept",MISSING)
    if v is not False: errs.append(f"AC04 {a.get('name')} self_accept must be present and boolean false, got {v!r}")
if rules.get("no_self_accept",{}).get("self_accept_allowed") is not False: errs.append("AC04 rule self_accept_allowed must be false")

# AC05: every agent git_push present AND boolean False; no remote; rule flags false
for a in agents:
    v=a.get("permissions",{}).get("git_push",MISSING)
    if v is not False: errs.append(f"AC05 {a.get('name')} git_push must be present and boolean false, got {v!r}")
lf=rules.get("local_first",{})
if lf.get("git_push_allowed") is not False or lf.get("remote_allowed") is not False: errs.append("AC05 local_first must forbid push and remote")
try:
    r=subprocess.run(["git","-C",root,"remote"],capture_output=True,text=True)
    if r.stdout.strip(): errs.append(f"AC05 git remote configured: {r.stdout.split()}")
except Exception as e: errs.append(f"AC05 cannot check git remote: {e}")

if errs:
    print("GOVERNANCE CHECK: FAIL"); [print(" -",e) for e in errs]; sys.exit(1)
print("GOVERNANCE CHECK: PASS (AC01-AC05 enforced)"); sys.exit(0)
