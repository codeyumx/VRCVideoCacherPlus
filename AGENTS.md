# AGENTS.md

Fork of [codeyumx/VRCVideoCacherPlus](https://github.com/codeyumx/VRCVideoCacherPlus)
(itself a fork of EllyVR/VRCVideoCacher). .NET 10, Avalonia 12, EmbedIO, EF Core + SQLite,
Serilog. `upstream` remote points at codeyumx — keep merges from it possible.

## Build, check, release

There is no `dotnet` on the host; it lives in a distrobox container.

```bash
distrobox enter arch -- dotnet build VRCVideoCacher.sln -warnaserror
```

`build.sh` is the pipeline. Actions run in this order no matter what order they are
passed, any failure aborts the rest, and every dotnet call carries `-warnaserror`:

```
bump  lint  build  artifacts  stop  deploy  start  commit  push  release
```

```bash
./build.sh --lint                          # locales, extension, strict compile, tests
./build.sh --build --stop --deploy --start # or --restart for the stop/start pair
./build.sh --all --dry-run                 # rehearse the whole release
```

Always `--lint` before committing.

**GitHub Actions does not run on this account.** `.github/workflows/ci.yml` is
disabled (`workflow_dispatch` only) because every push produced a failed run and a
notification. Its work happens locally instead — `--lint` is the build-and-test and
browser-extension jobs, `--artifacts` is the publish job. Do not re-enable the
triggers. Running the workflow under `act` was considered and rejected: it wants a
container runtime and a runner image to do what amounts to two `dotnet publish` calls.

**A release is always exactly four assets**: `VRCVideoCacher-win-x64.zip`,
`VRCVideoCacher-linux-x64.zip`, the Chrome `.crx` and the Firefox `.xpi`. `--release`
implies `--artifacts` so it builds them rather than trusting what is in `dist/`, and
both check the set is complete — the `.crx` is silently skipped when `npx` is missing,
which would otherwise ship a release with no Chrome download. The extension `.zip`
byproducts stay in `dist/` and are deliberately not attached.

`--artifacts` is also the only step that publishes Release/trimmed/single-file, so it
is the only one that exercises the trimmer. A Debug build cannot reproduce the trimmer
dropping something that only reflection reaches.

`dist/` is shared between `build.sh` and `BrowserExtension/build.sh`; neither may
`rm -rf` it, only its own outputs.

**Never stop, deploy to, or restart the app while the user has it running** unless
they asked for it in this session. Compiling and linting are always fine.

## Traps this codebase keeps setting

**The config file is shared with upstream VRCVideoCacher.** There is no separate
`PlusConfig.json` and no nested `Plus` block. Running plain VRCVideoCacher strips
keys it does not know, so every Plus setting must survive a round-trip through a
config that lost it — default sensibly, never assume presence.

**Localization fails silently.** English is the fallback, so a missing key renders
English and an empty one renders nothing. Add every new key to all eight
`Languages/*.loc.json`, then run `scripts/lint-locales.py`. Keys are often built at
runtime (`"SkipReasonTooLong|{0:F0}|{1}"`), so grep before assuming one is dead.

**`System.Text.Json` is source-generated.** A new serialized type needs a
`[JsonSerializable]` entry in `Utils/AppJsonContext.cs` or it fails at runtime, not
at compile time. No Newtonsoft anywhere.

**Exit code 0 does not mean success.** `ss -K` prints its header to stdout and
`SOCK_DESTROY answers: Operation not permitted` to stderr, then exits 0 — that is how
severing claimed success for years while doing nothing. Classify from the actual
output, and report "not permitted" as itself rather than as failure or success.

**`.NET` will not follow an HTTPS→HTTP redirect.** PyPyDance downgrades, so the
redirect has to be followed manually with a bounded hop count.

**Background loops must observe `Program.ShutdownToken`.** `Main` signals shutdown
and then force-exits; a loop that did not link its token is killed mid-step. Link,
do not replace: `CancellationTokenSource.CreateLinkedTokenSource(Program.ShutdownToken)`.

**Never block the UI thread.** Refresh loops use `PeriodicTimer` + `Task.Run` and
marshal only the finished result back. `DispatcherTimer` doing real work freezes the
window, and so does a synchronous `WaitForExit` on an elevation prompt.

**Use `ProcessStartInfo.ArgumentList`, never `Arguments`.** Install paths routinely
contain spaces (a Steam library on an external drive), and string interpolation
splits them into two arguments.

**Anything running elevated is a one-shot helper.** No UI, no config writes, no
background work; re-validate every argument (parse addresses as `IPAddress`) because
it runs as root. See `LaunchArgs.IsPrivilegedHelper`.

**`TimeSpan` normalizes instead of throwing.** `99:99` silently becomes 1h40m —
validate parsed fields explicitly.

**`yt-dlp-stub.exe` rebuilds non-deterministically.** It shows up dirty after any
build with identical content. `git checkout` it rather than committing the churn.

**Do not chain a check onto a build with `&&`.** `dotnet build && grep ...` tests the
grep, not the build. Use an explicit `if ... then ... else ... fi`.

**Sandbox live testing.** `XDG_CONFIG_HOME=/tmp/vvc-sandbox` keeps the user's real
config and cache database untouched.

## Telling the user something

Pick the quietest thing that does the job. In rough order of intrusiveness:

| Want | Use |
| --- | --- |
| Diagnostics, background progress | `Log.Debug` / `Log.Information` — Logs tab |
| Something degraded but recoverable | `Log.Warning` |
| A short result next to the thing it concerns | tab-local `StatusMessage` + `StatusMessageColor` |
| Persistent app-wide state | `MainWindowViewModel.StatusText` (status bar) |
| The user has to decide | `PopupWindow.CreateConfirm` |
| Broadcast news | MOTD banner |

**`Log.Error` is not just a log line — it opens a modal.** `UiLogSink` shows a
`PopupWindow` for anything at Error or above (when `ErrorPopups` is on), with
identical messages suppressed for a minute. Reserve it for "the thing you asked for
did not happen". Anything routine that fails — a CDN retry, a missing optional tool —
is `Warning` or `Debug`, or you are throwing a dialog at somebody in VR.

**Status bar** is `MainWindowViewModel.StatusText` (left) and `CacheStatusText`
(right), bound in `MainWindow.axaml`. It is for standing state — "server running",
cache size — not transient results. It survives a tab switch, so anything written
there stays until something else overwrites it.

**Tab-local status** is the `StatusMessage` / `StatusMessageColor` pair that
`ActiveConnectionsViewModel`, `SettingsViewModel` and `HistoryViewModel` each carry,
usually behind a small `SetStatus(message, colorHex)`. This is the default for
per-action feedback. Existing colours: `#81C784` success, `#FFB74D` pending or
unsaved, and red for failure.

**Dialogs** are `Views/PopupWindow`. Message only:
`await new PopupWindow(text).ShowDialog(owner)`. Confirmation:

```csharp
var dialog = PopupWindow.CreateConfirm(message, Localizer.Get("Yes"), Localizer.Get("No"));
dialog.Title = Localizer.Get("ConfirmClearCacheTitle");
await dialog.ShowDialog(owner);
if (dialog.Confirmed) { ... }
```

`SetFolderHint(label, path)` adds a clickable path, for "put the file here" errors.
There is no text-prompt dialog — add a field to the relevant view instead of
inventing one.

Every one of these takes a `Localizer.Get` key, never a literal. Owner windows can be
null during startup: `App.MainWindow` is null until the UI exists, and force-unwrapping
it in a log sink once took the whole application down over a log line.

## Style

Match upstream — this fork should read like the same project.

- File-scoped namespaces (enforced as a warning by `.editorconfig`), 4 spaces, `var`
  where the type is obvious.
- Serilog structured logging with named holes and a trailing period:
  `Log.Information("Added new default rule '{RuleName}'.", rule.Name)`. Never
  interpolate into the template. `Log.Debug` for diagnostics, `Warning` for degraded
  behaviour, `Error` only when something the user asked for did not happen.
- Comments explain *why*, especially the non-obvious constraint or the bug that made
  the code look like this. Do not narrate what the line already says.
- User-facing strings are always localization keys, never literals.
- Tests are xUnit, named as a sentence describing the guarantee
  (`NoticeTextNamesTheFileTheOldSettingsAreActuallyIn`), and async rather than
  blocking on a task (xUnit1031).

### UX

- Never surprise the user with a privileged prompt. Elevation is opt-in per call
  site: an explicit button may prompt, an automatic setting toggle may not.
- Report honestly. Degraded outcomes get their own state and their own message —
  "needs privileges", "not supported here" and "failed" are three different things
  and the UI says which.
- Destructive or outward-facing actions confirm first, and say exactly what will
  happen ("Delete all {0} cached videos? This cannot be undone.").
- The app runs alongside VR. Nothing may block, freeze the window, or steal focus.
