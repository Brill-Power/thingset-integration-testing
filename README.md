# thingset-integration-testing

Cross-language integration tests for the [ThingSet](https://thingset.io) implementations maintained by Brill Power:

| Language | Library | Role here |
|---|---|---|
| C++ | [thingset++](https://github.com/Brill-Power/thingsetplusplus) | fixture server |
| C# | [ThingSet.Net](https://github.com/Brill-Power/thingset.net) | client |
| Python | [python-thingset](https://github.com/Brill-Power/python-thingset) | client |

Every client test suite runs against the same fixture server. Tests talk over TCP on port 9001 and receive UDP reports on port 9002.

## Layout

```text
fixtures/
  fixture.json        the contract: object IDs, paths, types, initial/update values,
                      function test cases, expected reports
  cpp-server/         thingset++ server exposing exactly that object tree
tests/
  dotnet/             NUnit suite using ThingSet.Net
  python/             pytest suite using python-thingset
```

`fixture.json` is the single source of truth. The test suites are data-driven from it, so they can't drift apart. Each suite also has a schema test that compares the server's discovered object tree against the file, so the file and the server can't drift apart either. To add coverage, add the object to `cpp-server/main.cpp` and `fixture.json` together; both suites pick it up.

`xReset` restores every value to its initial state. Tests call it before running, so they don't depend on each other.

The server prints `READY` once it's listening; the harnesses wait for that rather than sleeping. They also start it with `--exit-with-parent`, so a crashed test runner doesn't leave a server holding the ports.

## Running locally

Requirements: CMake ≥ 3.24 and a C++20 compiler (GCC ≥ 12 or recent Clang), the .NET SDK (8 or later), and [uv](https://docs.astral.sh/uv/).

```sh
# 1. Build the fixture server (fetches thingset++ via CMake FetchContent)
cmake -S fixtures/cpp-server -B build/cpp-server -DCMAKE_BUILD_TYPE=Release
cmake --build build/cpp-server -j

# 2. Run the client suites; each starts and stops the server itself
(cd tests/dotnet/ThingSet.Test.Integration && dotnet test)
(cd tests/python && uv run pytest)
```

Run the suites one at a time, because the server's ports are fixed. If you put the server somewhere else, set `THINGSET_FIXTURE_SERVER=/path/to/thingset-fixture-server`.

## Choosing library versions

By default everything is pinned, so a red build means something changed in this repo.

| Library | Pinned in | Use latest / local |
|---|---|---|
| thingset++ | `THINGSETPLUSPLUS_GIT_TAG` in `fixtures/cpp-server/CMakeLists.txt` | `-DTHINGSETPLUSPLUS_GIT_TAG=main`, or a local checkout with `-DFETCHCONTENT_SOURCE_DIR_THINGSETPLUSPLUS=/path/to/thingsetplusplus` |
| ThingSet.Net | `ThingSetNetVersion` in the `.csproj` | `dotnet test -p:ThingSetNetVersion=*` (latest NuGet), or `dotnet test -p:ThingSetNetSource=/path/to/thingset.net` (build from source) |
| python-thingset | `tests/python/pyproject.toml` + `uv.lock` | `uv run --upgrade-package python-thingset pytest`, or `uv run --with-editable /path/to/python-thingset pytest` |

Building ThingSet.Net from source needs the .NET 10 SDK, because it multi-targets `net8.0;net10.0`.

## CI

`.github/workflows/test.yml` runs two variants:

- **pinned**: on every push and PR. Should always be green.
- **latest**: nightly and on manual dispatch. Uses thingset++ `main`; ThingSet.Net from the latest NuGet release and from `main`; and the latest python-thingset. A failure here means upstream changed or broke something. Fix it there, or update the fixture and pins here.

## Known issues / workarounds

- **thingset++ before `f74b743`** (e.g. the May 2025 commit this repo previously used as a submodule): an update by numeric ID at the root replies with a bare `84` and doesn't apply the write. Updating by name worked. python-thingset updates by ID, so it was affected; ThingSet.Net only updates by name, so it wasn't.
- The fixture server's ports (9001/9002) are hard-coded in thingset++'s asio transport, and python-thingset's TCP client hard-codes 9001, so tests can't use random ports yet.

## Ideas for next steps

- Reverse the direction: a C# fixture server built on `ThingSet.Server` (on NuGet since 0.1.12), with the C++ client (`ThingSetClient` + asio transport) and Python as clients.
- A thingset++ client test suite (GoogleTest) against the same `fixture.json`.
- Coverage for records/record arrays, the `NodeID`/EUI node, and text mode.
