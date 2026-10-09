#!/usr/bin/env python3
"""Independent dependency and shell boundary probes for BIO-FND-008."""

from pathlib import Path
import os
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[3]
project = (ROOT / "api/Biolog.Api/Biolog.Api.csproj").read_text(encoding="utf-8")
tenant = (ROOT / "api/Biolog.Api/Tenancy/TenantContextMiddleware.cs").read_text(encoding="utf-8")
module_host = (ROOT / "api/Biolog.Api/Modules/ModuleHost.cs").read_text(encoding="utf-8")

checks: list[tuple[str, bool]] = []

match = re.search(r'PackageReference Include="Microsoft\.OpenApi" Version="([0-9.]+)"', project)
version = tuple(int(part) for part in match.group(1).split(".")) if match else (0,)
checks.append(("Microsoft.OpenApi is on patched 2.x release 2.7.5 or later", version >= (2, 7, 5)))
checks.append(("tenant hook does not infer tenant data from request input", "accessor.Context = null;" in tenant and "Request." not in tenant))
checks.append(("module host rejects dependency cycles", "CircularModuleDependencyException" in module_host and "TopologicalSort" in module_host))

with tempfile.TemporaryDirectory(prefix="bio-fnd-008-audit-") as tmp:
    fake_dotnet = Path(tmp) / "dotnet"
    fake_dotnet.write_text("#!/usr/bin/env bash\necho 'synthetic audit transport failure' >&2\nexit 42\n", encoding="utf-8")
    fake_dotnet.chmod(0o755)
    env = os.environ.copy()
    env["PATH"] = f"{tmp}:{env['PATH']}"
    audit = subprocess.run(
        ["bash", str(ROOT / "scripts/check-dotnet-vulnerabilities.sh"), "synthetic.csproj"],
        cwd=ROOT,
        env=env,
        text=True,
        capture_output=True,
        check=False,
    )
    checks.append(("vulnerability audit fails closed when dotnet audit fails", audit.returncode != 0))

failed = 0
for label, ok in checks:
    print(("PASS" if ok else "FAIL"), label)
    failed += not ok

raise SystemExit(1 if failed else 0)
