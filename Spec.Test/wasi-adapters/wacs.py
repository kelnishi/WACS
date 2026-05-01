"""WACS adapter for the upstream wasi-testsuite Python harness.

Run an installed `wacs` global tool against the testsuite, e.g.:

    python3 Spec.Test/wasi/run-tests \
        --runtime-adapter Spec.Test/wasi-adapters/wacs.py \
        Spec.Test/wasi/tests/c/testsuite/wasm32-wasip1

Set the WACS environment variable to override the binary location, e.g.:

    WACS="dotnet run --project Wacs.Console -- run --wasi" \
        python3 Spec.Test/wasi/run-tests ...

Mirrors the contract documented at
WebAssembly/wasi-testsuite/doc/adapters.md.
"""

import os
import subprocess
from typing import Dict, List, Optional, Tuple


WACS = os.environ.get("WACS", "wacs")


def get_name() -> str:
    return "wacs"


def get_version() -> str:
    try:
        out = subprocess.run(
            WACS.split() + ["--version"],
            capture_output=True, text=True, check=False,
        )
        return (out.stdout.strip() or out.stderr.strip()) or "unknown"
    except FileNotFoundError:
        return "unknown"


def compute_argv(
    test_path: str,
    args_env_dirs: Tuple[List[str], Dict[str, str], List[Tuple[str, str]]],
    *_unused,
) -> List[str]:
    args, env, dirs = args_env_dirs

    cmd = WACS.split() + ["run", "--wasi"]
    for k, v in (env or {}).items():
        cmd += ["-e", f"{k}={v}"]
    for host, _guest in (dirs or []):
        # WACS preopens use the bare host directory; the guest path is
        # derived inside Wacs.WASIp1.PreopenedDirectory.
        cmd += ["-d", str(host)]
    cmd.append(test_path)
    cmd += args or []
    return cmd
