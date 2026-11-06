# Copyright (c) 2026 Gareth Potter.
#
# SPDX-License-Identifier: Apache-2.0
"""Shared pytest fixtures: the fixture contract, the server process and a client."""

import json
import os
import signal
import socket
import subprocess
import threading
import time
from pathlib import Path

import pytest
from python_thingset.transport.tcp import ThingSetTCP

STARTUP_TIMEOUT_S = 10.0


def _repo_root() -> Path:
    for folder in Path(__file__).resolve().parents:
        if (folder / "fixtures" / "fixture.json").is_file():
            return folder
    raise RuntimeError(f"Could not find fixtures/fixture.json above {__file__}")


REPO_ROOT = _repo_root()
FIXTURE = json.loads((REPO_ROOT / "fixtures" / "fixture.json").read_text())


def _server_binary() -> Path:
    override = os.environ.get("THINGSET_FIXTURE_SERVER")
    path = Path(override) if override else REPO_ROOT / "build" / "cpp-server" / "thingset-fixture-server"
    if not path.is_file():
        raise FileNotFoundError(
            f"Fixture server not found at {path}. Build it with "
            "`cmake -S fixtures/cpp-server -B build/cpp-server && cmake --build build/cpp-server`, "
            "or set THINGSET_FIXTURE_SERVER."
        )
    return path


def _wait_for_ready(proc: subprocess.Popen) -> None:
    ready = threading.Event()

    def pump() -> None:
        # Keep draining stdout so the server never blocks on a full pipe.
        for line in proc.stdout:
            if line.strip() == "READY":
                ready.set()

    threading.Thread(target=pump, daemon=True).start()
    if not ready.wait(STARTUP_TIMEOUT_S):
        proc.kill()
        raise RuntimeError(f"Fixture server did not print READY within {STARTUP_TIMEOUT_S}s (exit code {proc.poll()})")

    # Belt and braces: make sure the port really accepts connections.
    deadline = time.monotonic() + STARTUP_TIMEOUT_S
    while True:
        try:
            socket.create_connection(("127.0.0.1", FIXTURE["tcpPort"]), timeout=0.5).close()
            return
        except OSError:
            if time.monotonic() > deadline:
                raise


@pytest.fixture(scope="session")
def fixture_contract() -> dict:
    return FIXTURE


@pytest.fixture(scope="session")
def server():
    # stdin is a pipe we never write to: if pytest dies, it closes and the server exits.
    proc = subprocess.Popen(
        [str(_server_binary()), "--exit-with-parent"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        text=True,
    )
    try:
        _wait_for_ready(proc)
        yield proc
    finally:
        if proc.poll() is None:
            proc.send_signal(signal.SIGINT)
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()


@pytest.fixture(scope="session")
def _connection(server):
    # One connection for the whole session: python-thingset's disconnect() waits
    # up to a second for its receive thread, which adds up over many tests.
    c = ThingSetTCP("127.0.0.1")
    try:
        yield c
    finally:
        # The receive thread isn't a daemon, so a missed disconnect hangs pytest.
        c.disconnect()


@pytest.fixture
def client(_connection):
    """A connected client, with server state reset to the fixture's initial values."""
    reset = next(f for f in FIXTURE["functions"] if f["path"] == "xReset")
    _connection.exec(reset["id"], [])
    return _connection
