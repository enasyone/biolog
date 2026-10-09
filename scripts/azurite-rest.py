#!/usr/bin/env python3
"""Minimal Azure Blob Storage REST client for the local Azurite emulator
(BIO-OPS-009-AC03: "photo/object evidence restore is verified").

No `azure-storage-blob` SDK or `azcopy` is available on this machine's
toolchain (see docs/TOOLCHAIN-POLICY.md - neither is a pinned category), and
BIO-FND-003's own "zero required configuration" policy argues against adding
a new runtime dependency just to seed/verify a handful of synthetic rehearsal
blobs. Azurite speaks the real Azure Blob Storage REST wire protocol
(Shared Key authorization, same as production Azure Storage), so this
implements just the operations the rehearsal needs - put-container,
put-blob, get-blob, list-blobs - directly against that protocol using only
the Python standard library.

The account name/key below are Azurite's fixed, publicly documented emulator
defaults (https://learn.microsoft.com/azure/storage/common/storage-use-
azurite - "Well-known storage account and key"), identical on every Azurite
instance everywhere. They are not a secret: they authenticate against a
local emulator, never a real Azure account, and are allowlisted in
.gitleaks.toml for exactly this reason.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import hmac
import sys
import time
import urllib.error
import urllib.request

ACCOUNT_NAME = "devstoreaccount1"
# Azurite's well-known emulator account key - see module docstring.
ACCOUNT_KEY = (
    "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/"
    "K1SZFPTOtr/KBHBeksoGMGw=="
)
API_VERSION = "2021-08-06"


def _rfc1123_now() -> str:
    return time.strftime("%a, %d %b %Y %H:%M:%S GMT", time.gmtime())


def _canonicalized_headers(headers: dict[str, str]) -> str:
    ms_headers = {k.lower(): v.strip() for k, v in headers.items() if k.lower().startswith("x-ms-")}
    return "".join(f"{k}:{v}\n" for k, v in sorted(ms_headers.items()))


def _canonicalized_resource(path: str, query: dict[str, str]) -> str:
    # Azurite uses path-style addressing (http://host:port/<account>/<container>/...),
    # so the account name already appears once in `path`. Shared Key signing always
    # prepends "/<account>" again regardless of addressing style (confirmed against
    # Azurite's own [STRING TO SIGN] debug log: it expects the account name doubled,
    # "/devstoreaccount1/devstoreaccount1/<container>" - this is not a bug, it's how
    # emulator/path-style requests are meant to sign, as opposed to production Azure's
    # virtual-hosted-style URLs where the URI path alone never contains the account name).
    resource = f"/{ACCOUNT_NAME}/{ACCOUNT_NAME}{path}"
    if not query:
        return resource
    params = "".join(f"\n{k.lower()}:{v}" for k, v in sorted(query.items()))
    return resource + params


def _sign(verb: str, path: str, query: dict[str, str], headers: dict[str, str], content_length: int,
          content_type: str = "") -> str:
    string_to_sign = (
        f"{verb}\n"
        f"\n"  # Content-Encoding
        f"\n"  # Content-Language
        f"{content_length if content_length else ''}\n"
        f"\n"  # Content-MD5
        f"{content_type}\n"  # Content-Type
        f"\n"  # Date (we send x-ms-date instead)
        f"\n"  # If-Modified-Since
        f"\n"  # If-Match
        f"\n"  # If-None-Match
        f"\n"  # If-Unmodified-Since
        f"\n"  # Range
        f"{_canonicalized_headers(headers)}"
        f"{_canonicalized_resource(path, query)}"
    )
    key = base64.b64decode(ACCOUNT_KEY)
    signature = base64.b64encode(hmac.new(key, string_to_sign.encode("utf-8"), hashlib.sha256).digest())
    return signature.decode("ascii")


def _request(verb: str, host: str, port: int, path: str, query: dict[str, str] | None = None,
             body: bytes | None = None, extra_headers: dict[str, str] | None = None) -> tuple[int, bytes, dict]:
    query = query or {}
    headers = {
        "x-ms-date": _rfc1123_now(),
        "x-ms-version": API_VERSION,
    }
    headers.update(extra_headers or {})
    content_length = len(body) if body else 0
    # urllib silently adds "Content-Type: application/x-www-form-urlencoded" to any
    # request with a body unless one is set explicitly - it must be signed with
    # whatever value is ACTUALLY sent on the wire, so set it ourselves rather than
    # leaving the Content-Type field in the signature empty while urllib injects one.
    content_type = "application/octet-stream" if body is not None else ""
    if body is not None:
        headers["Content-Type"] = content_type
    signature = _sign(verb, path, query, headers, content_length, content_type)
    headers["Authorization"] = f"SharedKey {ACCOUNT_NAME}:{signature}"
    if body is not None:
        headers["Content-Length"] = str(content_length)

    qs = "&".join(f"{k}={v}" for k, v in sorted(query.items()))
    url = f"http://{host}:{port}/{ACCOUNT_NAME}{path}"
    if qs:
        url += f"?{qs}"

    req = urllib.request.Request(url, data=body, method=verb, headers=headers)
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, resp.read(), dict(resp.headers)
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read(), dict(exc.headers)


def cmd_put_container(args: argparse.Namespace) -> int:
    status, resp_body, _ = _request(
        "PUT", args.host, args.port, f"/{args.container}", query={"restype": "container"}
    )
    if status not in (201, 409):  # 409 Conflict = already exists, treated as success here
        sys.stderr.write(f"put-container failed: HTTP {status}: {resp_body!r}\n")
        return 1
    print(f"put-container: {args.container} ready (HTTP {status})")
    return 0


def cmd_put_blob(args: argparse.Namespace) -> int:
    with open(args.file, "rb") as f:
        data = f.read()
    status, resp_body, _ = _request(
        "PUT", args.host, args.port, f"/{args.container}/{args.blob}",
        body=data, extra_headers={"x-ms-blob-type": "BlockBlob"},
    )
    if status != 201:
        sys.stderr.write(f"put-blob failed: HTTP {status}: {resp_body!r}\n")
        return 1
    digest = hashlib.sha256(data).hexdigest()
    print(f"put-blob: {args.container}/{args.blob} ({len(data)} bytes, sha256={digest})")
    return 0


def cmd_get_blob(args: argparse.Namespace) -> int:
    status, resp_body, _ = _request("GET", args.host, args.port, f"/{args.container}/{args.blob}")
    if status != 200:
        sys.stderr.write(f"get-blob failed: HTTP {status}: {resp_body!r}\n")
        return 1
    if args.out:
        with open(args.out, "wb") as f:
            f.write(resp_body)
    digest = hashlib.sha256(resp_body).hexdigest()
    print(f"get-blob: {args.container}/{args.blob} ({len(resp_body)} bytes, sha256={digest})")
    return 0


def cmd_list_blobs(args: argparse.Namespace) -> int:
    status, resp_body, _ = _request(
        "GET", args.host, args.port, f"/{args.container}",
        query={"restype": "container", "comp": "list"},
    )
    if status != 200:
        sys.stderr.write(f"list-blobs failed: HTTP {status}: {resp_body!r}\n")
        return 1
    text = resp_body.decode("utf-8")
    import re
    names = re.findall(r"<Name>([^<]+)</Name>", text)
    for name in names:
        print(name)
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, required=True)
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("put-container")
    p.add_argument("--container", required=True)
    p.set_defaults(func=cmd_put_container)

    p = sub.add_parser("put-blob")
    p.add_argument("--container", required=True)
    p.add_argument("--blob", required=True)
    p.add_argument("--file", required=True)
    p.set_defaults(func=cmd_put_blob)

    p = sub.add_parser("get-blob")
    p.add_argument("--container", required=True)
    p.add_argument("--blob", required=True)
    p.add_argument("--out", default=None)
    p.set_defaults(func=cmd_get_blob)

    p = sub.add_parser("list-blobs")
    p.add_argument("--container", required=True)
    p.set_defaults(func=cmd_list_blobs)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
