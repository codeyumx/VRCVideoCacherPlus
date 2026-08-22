#!/usr/bin/env bash
# Build pipeline for VRCVideoCacher.
#
# Actions are opt-in flags. They always run in the order listed below regardless of the
# order you pass them, because the dependencies only make sense one way round — there is
# no point tagging a release before the thing has been built, or pushing a commit that
# was never linted. Any action that fails aborts the rest immediately, and dotnet is run
# with warnings promoted to errors throughout.
#
#   --bump      raise the patch component of <Version> in VRCVideoCacher.csproj
#   --lint      locale check, extension check, strict compile, full test suite
#   --build     publish yt-dlp-stub and VRCVideoCacher for Steam (Linux x64)
#   --artifacts build every release asset into dist/: the win-x64 and linux-x64 zips
#               and the Chrome .crx and Firefox .xpi (implied by --release)
#   --stop      stop the deployed app
#   --deploy    rsync the publish output into the Steam directory
#   --start     start the deployed app through Steam and tail its log
#   --restart   --stop and --start together
#   --commit    commit the working tree      (-m MESSAGE, defaults to "Release vX.Y.Z")
#               and tag it X.Y.Z when --bump ran
#   --push      push the current branch and any tag created by --bump
#   --release   create the GitHub release, attaching all four assets
#
#   --all       everything except the local-only actions (deploy, stop, start)
#   -n|--dry-run  print each action instead of running the ones that change the world
#
# Running it with no arguments at all builds and deploys, which is what it always did.
# Passing any flag turns that off, so nothing gets deployed unless you asked for it.
#
# Actions run in this order, which is why --stop sits before --deploy and --start after:
#
#   bump  lint  build  artifacts  stop  deploy  start  commit  push  release
#
# GitHub Actions does not run on this account, so .github/workflows/ci.yml is disabled
# and its work happens here instead: --lint is the build-and-test and browser-extension
# jobs, --artifacts is the publish job. Running the workflow locally under `act` was the
# alternative and is not worth it — it wants a container runtime and a runner image to
# do what is, in the end, two dotnet publishes.
#
# Everything machine-specific is an environment variable with the author's setup as the
# default, so this is overridable rather than only working on one machine:
#
#   VVC_TARGET_DIR   where to deploy         (default: the Steam common dir below)
#   VVC_CONTAINER    distrobox container     (default: arch; set empty to use the host dotnet)
#   VVC_TRIMMED      1 to publish trimmed    (default: 0, see note further down)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TARGET_DIR="${VVC_TARGET_DIR:-/run/media/system/Data/Games/Steam/steamapps/common/VRCVideoCacher}"
CONTAINER_NAME="${VVC_CONTAINER-arch}"
TRIMMED="${VVC_TRIMMED:-0}"
TMP_OUT="${SCRIPT_DIR}/output_steam_linux"
DIST_DIR="${SCRIPT_DIR}/dist"
STEAM_APP_ID=4296960
CSPROJ="${SCRIPT_DIR}/VRCVideoCacher/VRCVideoCacher.csproj"

DO_BUMP=false
DO_LINT=false
DO_BUILD=false
DO_ARTIFACTS=false
DO_STOP=false
DO_DEPLOY=false
DO_START=false
DO_COMMIT=false
DO_PUSH=false
DO_RELEASE=false
DRY_RUN=false
COMMIT_MESSAGE=""
ARG_COUNT=$#

while [ $# -gt 0 ]; do
    case "$1" in
        --bump)    DO_BUMP=true ;;
        --lint)    DO_LINT=true ;;
        --build)   DO_BUILD=true ;;
        --artifacts) DO_ARTIFACTS=true ;;
        --stop)    DO_STOP=true ;;
        --deploy)  DO_DEPLOY=true ;;
        --start)   DO_START=true ;;
        --restart) DO_STOP=true; DO_START=true ;;
        --commit)  DO_COMMIT=true ;;
        --push)    DO_PUSH=true ;;
        # A release is only ever the complete set of assets, so it builds them itself
        # rather than trusting whatever happens to be left in dist/ from an older run.
        --release) DO_RELEASE=true; DO_ARTIFACTS=true ;;
        --all)
            DO_BUMP=true; DO_LINT=true; DO_BUILD=true; DO_ARTIFACTS=true
            DO_COMMIT=true; DO_PUSH=true; DO_RELEASE=true
            ;;
        -n|--dry-run) DRY_RUN=true ;;
        -m|--message)
            [ $# -ge 2 ] || { echo "error: $1 needs a message" >&2; exit 2; }
            COMMIT_MESSAGE="$2"; shift
            ;;
        -m=*|--message=*) COMMIT_MESSAGE="${1#*=}" ;;
        -h|--help) sed -n '2,45p' "$0" | sed 's/^# \?//'; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
    shift
done

# Backwards compatibility: bare ./build.sh is a deploy. Keyed on there being no arguments
# at all rather than on "no action flags selected", so that a flag which selects no action
# can never fall through to here and deploy something nobody asked to deploy.
if [ "$ARG_COUNT" -eq 0 ]; then
    DO_BUILD=true
    DO_DEPLOY=true
fi

step() { echo; echo "=== $* ==="; }
fail() { echo "error: $*" >&2; exit 1; }

# Guards a command that changes something outside this working tree.
run() {
    if [ "$DRY_RUN" = true ]; then
        echo "[dry-run] $*"
        return 0
    fi
    "$@"
}

# Run dotnet inside the container when one is configured, otherwise straight on the host.
dotnet_run() {
    if [ -n "${CONTAINER_NAME}" ]; then
        distrobox enter "${CONTAINER_NAME}" -- dotnet "$@"
    else
        dotnet "$@"
    fi
}

read_version() {
    sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$CSPROJ" | head -n 1
}

VERSION="$(read_version)"
[ -n "$VERSION" ] || fail "could not read <Version> from ${CSPROJ}"

# yt-dlp-stub.exe rebuilds non-deterministically: identical content, different bytes. Left
# alone it lands in every release commit as meaningless churn. Recorded here so it can be
# restored before committing — but only when it was already clean, so a genuine change to
# the stub is never silently discarded.
STUB="VRCVideoCacher/yt-dlp-stub.exe"
STUB_WAS_CLEAN=false
git -C "$SCRIPT_DIR" diff --quiet -- "$STUB" 2>/dev/null && STUB_WAS_CLEAN=true

# A release is always the same four files. Checked as a set rather than trusted,
# because the .crx silently does not get built when npx is unavailable — which would
# otherwise produce a release quietly missing the Chrome download.
RELEASE_ASSETS=()
require_assets() {
    local missing=0
    local found
    RELEASE_ASSETS=()
    for pattern in \
        "VRCVideoCacher-win-x64.zip" \
        "VRCVideoCacher-linux-x64.zip" \
        "VRCVideoCacherPlusPlus-chrome-*.crx" \
        "VRCVideoCacherPlusPlus-firefox-*.xpi"
    do
        found=$(find "${DIST_DIR}" -maxdepth 1 -name "$pattern" -print -quit 2>/dev/null || true)
        if [ -z "$found" ]; then
            printf '  MISSING  %s\n' "$pattern" >&2
            missing=1
        else
            RELEASE_ASSETS+=("$found")
            printf '  %-6s %s\n' "$(du -h "$found" | cut -f1)" "$(basename "$found")"
        fi
    done
    [ "$missing" -eq 0 ] || fail "dist/ is missing release assets"
}

require_clean_tree() {
    git -C "$SCRIPT_DIR" diff --quiet && git -C "$SCRIPT_DIR" diff --cached --quiet \
        || fail "working tree is dirty; commit or stash first"
}

# --- bump -------------------------------------------------------------------------------
# Versioning is YEAR.MONTH.RELEASE, so rolling into a new month restarts the counter
# rather than carrying the old month's patch number forward.
if [ "$DO_BUMP" = true ]; then
    step "Bumping version (currently ${VERSION})"

    IFS='.' read -r OLD_YEAR OLD_MONTH OLD_PATCH <<< "$VERSION"
    NOW_YEAR="$(date +%Y)"
    NOW_MONTH="$(date +%-m)"

    if [ "$OLD_YEAR" = "$NOW_YEAR" ] && [ "$OLD_MONTH" = "$NOW_MONTH" ]; then
        NEW_VERSION="${NOW_YEAR}.${NOW_MONTH}.$((OLD_PATCH + 1))"
    else
        NEW_VERSION="${NOW_YEAR}.${NOW_MONTH}.0"
    fi

    if [ "$DRY_RUN" = true ]; then
        echo "[dry-run] ${VERSION} -> ${NEW_VERSION}"
    else
        # Anchored on the exact old value so this cannot touch a PackageReference Version.
        sed -i "s:<Version>${VERSION}</Version>:<Version>${NEW_VERSION}</Version>:" "$CSPROJ"
        [ "$(read_version)" = "$NEW_VERSION" ] || fail "version bump did not apply"
        echo "${VERSION} -> ${NEW_VERSION}"
    fi
    VERSION="$NEW_VERSION"
fi

# Tags in this repo are bare versions (2026.8.21), while release *titles* carry the v
# (v2026.8.21). Keep both conventions — tagging v2026.8.22 next to 2026.8.21 would split
# the history in two and break `releases/latest` ordering.
TAG="${VERSION}"
RELEASE_TITLE="v${VERSION}"

# --- lint -------------------------------------------------------------------------------
if [ "$DO_LINT" = true ]; then
    step "Checking locales"
    python3 "${SCRIPT_DIR}/scripts/lint-locales.py" || fail "locale check failed"

    step "Checking browser extension"
    "${SCRIPT_DIR}/BrowserExtension/build.sh" --check || fail "browser extension check failed"

    step "Compiling with warnings as errors"
    dotnet_run build "${SCRIPT_DIR}/VRCVideoCacher.sln" -c Release -warnaserror \
        || fail "build produced warnings or errors"

    step "Running tests"
    dotnet_run test "${SCRIPT_DIR}/VRCVideoCacher.Tests/VRCVideoCacher.Tests.csproj" -c Release \
        || fail "tests failed"
fi

# --- build ------------------------------------------------------------------------------
if [ "$DO_BUILD" = true ]; then
    step "Building yt-dlp-stub"
    dotnet_run publish "${SCRIPT_DIR}/yt-dlp-stub/yt-dlp-stub.csproj" -c Release -r win-x64 -warnaserror
    cp "${SCRIPT_DIR}/yt-dlp-stub/bin/Release/net10.0/win-x64/publish/yt-dlp-stub.exe" "${SCRIPT_DIR}/VRCVideoCacher/"

    # Deploying loose files rather than a single trimmed binary keeps local iteration fast
    # and makes stack traces readable. It does mean this path does not exercise trimming —
    # CI's publish job does that, and VVC_TRIMMED=1 reproduces it here when you need to.
    step "Building VRCVideoCacher for Steam (Linux x64, trimmed=${TRIMMED}, v${VERSION})"
    rm -rf "${TMP_OUT}"
    dotnet_run publish "$CSPROJ" \
        -c SteamRelease \
        -r linux-x64 \
        -o "${TMP_OUT}" \
        --self-contained true \
        -warnaserror \
        -p:PublishSingleFile=false \
        -p:PublishTrimmed="$([ "${TRIMMED}" = "1" ] && echo true || echo false)"
fi

# --- artifacts --------------------------------------------------------------------------
# The publish job from ci.yml, run here because Actions does not run on this account.
# Worth keeping distinct from --build: this is Release, trimmed and single-file per the
# csproj, which is the only configuration that exercises the trimmer. A Debug or loose
# build cannot reproduce the trimmer removing something only reflection reaches.
if [ "$DO_ARTIFACTS" = true ]; then
    command -v zip >/dev/null || fail "zip is not installed; cannot package artifacts"

    rm -rf "${DIST_DIR}"
    mkdir -p "${DIST_DIR}"

    step "Packaging browser extensions"
    # Also re-runs the shared-file drift check, so this cannot ship a chrome/ and a
    # firefox/ that have silently diverged.
    "${SCRIPT_DIR}/BrowserExtension/build.sh" || fail "packaging the browser extensions failed"

    for RID in win-x64 linux-x64; do
        step "Publishing ${RID} (Release, trimmed, v${VERSION})"
        OUT="${SCRIPT_DIR}/out/${RID}"
        rm -rf "${OUT}"
        dotnet_run publish "$CSPROJ" -c Release -r "${RID}" -o "${OUT}" -warnaserror \
            || fail "publishing ${RID} failed"

        ZIP="${DIST_DIR}/VRCVideoCacher-${RID}.zip"
        (cd "${OUT}" && zip -qr "${ZIP}" .) || fail "packaging ${RID} failed"
    done

    step "Release assets"
    require_assets
fi

# --- stop -------------------------------------------------------------------------------
if [ "$DO_STOP" = true ]; then
    step "Stopping VRCVideoCacher"
    # Match the deployed binary by full path. A bare `pkill -f VRCVideoCacher` also matches
    # this script, an editor with the project open, or a shell sitting in the source tree.
    if [ "$DRY_RUN" = true ]; then
        # Not just `run pkill`: the wait-and-verify below would then poll a process that
        # was never signalled and report a failure to stop something nobody stopped.
        echo "[dry-run] pkill -f ^${TARGET_DIR}/VRCVideoCacher"
    elif pgrep -f "^${TARGET_DIR}/VRCVideoCacher" >/dev/null; then
        # SIGTERM first so the shutdown token fires and the loops unwind; SIGKILL only if
        # it is still there afterwards.
        pkill -f "^${TARGET_DIR}/VRCVideoCacher" || true
        for _ in $(seq 1 10); do
            pgrep -f "^${TARGET_DIR}/VRCVideoCacher" >/dev/null || break
            sleep 1
        done
        if pgrep -f "^${TARGET_DIR}/VRCVideoCacher" >/dev/null; then
            echo "Still running after 10s, sending SIGKILL."
            pkill -9 -f "^${TARGET_DIR}/VRCVideoCacher" || true
            sleep 1
        fi
        pgrep -f "^${TARGET_DIR}/VRCVideoCacher" >/dev/null \
            && fail "could not stop VRCVideoCacher" || echo "Stopped."
    else
        echo "Not running."
    fi
fi

# --- deploy -----------------------------------------------------------------------------
if [ "$DO_DEPLOY" = true ]; then
    [ -d "${TMP_OUT}" ] || fail "nothing to deploy: ${TMP_OUT} does not exist (run --build)"

    step "Deploying to ${TARGET_DIR}"
    run mkdir -p "${TARGET_DIR}"
    run rsync -av --delete --exclude='CachedAssets' --exclude='logs' "${TMP_OUT}/" "${TARGET_DIR}/"
    echo "=== Deployment Complete ==="
fi

# --- start ------------------------------------------------------------------------------
if [ "$DO_START" = true ]; then
    step "Starting VRCVideoCacher"
    if [ "$DRY_RUN" = true ]; then
        echo "[dry-run] steam steam://rungameid/${STEAM_APP_ID}"
    else
        (nohup steam "steam://rungameid/${STEAM_APP_ID}" >/dev/null 2>&1 \
            || nohup xdg-open "steam://rungameid/${STEAM_APP_ID}" >/dev/null 2>&1 &)

        # Steam takes its time, and how long varies. Poll rather than sleeping a fixed
        # five seconds and declaring failure — that reported a crash on every slow launch.
        for _ in $(seq 1 30); do
            pgrep -f "^${TARGET_DIR}/VRCVideoCacher" >/dev/null && break
            sleep 1
        done

        PIDS=$(pgrep -f "^${TARGET_DIR}/VRCVideoCacher" || true)
        if [ -z "${PIDS}" ]; then
            CRASH_REPORT="${XDG_CONFIG_HOME:-${HOME}/.config}/VRCVideoCacher/CRASH_REPORT.txt"
            if [ -f "${CRASH_REPORT}" ]; then
                step "CRASH_REPORT.txt (check the timestamp — it may predate this run)"
                cat "${CRASH_REPORT}"
            fi
            fail "VRCVideoCacher did not start within 30s"
        fi

        echo "Running (PIDs: ${PIDS})"
        sleep 3
        LOG_DIR="${XDG_CONFIG_HOME:-${HOME}/.config}/VRCVideoCacher/Logs"
        LOG_FILE=$(ls -t "${LOG_DIR}"/VRCVideoCacher*.log 2>/dev/null | head -n 1 || true)
        if [ -n "${LOG_FILE}" ] && [ -f "${LOG_FILE}" ]; then
            step "Last 25 log lines (${LOG_FILE})"
            tail -n 25 "${LOG_FILE}"
        fi
    fi
fi

# --- commit -----------------------------------------------------------------------------
if [ "$DO_COMMIT" = true ]; then
    step "Committing"
    if [ "$STUB_WAS_CLEAN" = true ] && ! git -C "$SCRIPT_DIR" diff --quiet -- "$STUB"; then
        echo "Discarding rebuilt ${STUB} (byte churn, no content change)."
        run git -C "$SCRIPT_DIR" checkout -- "$STUB"
    fi

    if git -C "$SCRIPT_DIR" diff --quiet && git -C "$SCRIPT_DIR" diff --cached --quiet; then
        echo "Nothing to commit."
    else
        MESSAGE="${COMMIT_MESSAGE:-Release ${RELEASE_TITLE}}"
        run git -C "$SCRIPT_DIR" add -A
        run git -C "$SCRIPT_DIR" commit -m "$MESSAGE" || fail "commit failed"
    fi

    if [ "$DO_BUMP" = true ]; then
        if git -C "$SCRIPT_DIR" rev-parse -q --verify "refs/tags/${TAG}" >/dev/null; then
            echo "Tag ${TAG} already exists, leaving it alone."
        else
            run git -C "$SCRIPT_DIR" tag -a "$TAG" -m "$TAG" || fail "could not create tag ${TAG}"
        fi
    fi
fi

# --- push -------------------------------------------------------------------------------
if [ "$DO_PUSH" = true ]; then
    step "Pushing"
    # Uncommitted work would be silently left behind by a push that appears to succeed.
    [ "$DRY_RUN" = true ] || require_clean_tree
    BRANCH="$(git -C "$SCRIPT_DIR" rev-parse --abbrev-ref HEAD)"
    run git -C "$SCRIPT_DIR" push origin "$BRANCH" || fail "push failed"
    if git -C "$SCRIPT_DIR" rev-parse -q --verify "refs/tags/${TAG}" >/dev/null; then
        run git -C "$SCRIPT_DIR" push origin "$TAG" || fail "pushing tag ${TAG} failed"
    fi
fi

# --- release ----------------------------------------------------------------------------
if [ "$DO_RELEASE" = true ]; then
    step "Creating GitHub release ${TAG}"
    command -v gh >/dev/null || fail "gh is not installed; cannot create a release"

    # `gh release create` creates a draft, uploads, then publishes. Interrupted partway —
    # a 100MB upload is not quick — it leaves a draft behind, and a plain "already exists"
    # check then blocks every retry with nothing to do about it. Resume instead.
    RESUME_DRAFT=false
    if gh release view "$TAG" >/dev/null 2>&1; then
        if [ "$(gh release view "$TAG" --json isDraft -q .isDraft)" = "true" ]; then
            echo "Found an unfinished draft for ${TAG}; resuming it."
            RESUME_DRAFT=true
        else
            fail "release ${TAG} already exists and is published"
        fi
    fi

    # The tag has to be on the remote first or gh will create one from whatever the
    # default branch happens to point at.
    if [ "$DRY_RUN" = false ] && ! git -C "$SCRIPT_DIR" ls-remote --exit-code --tags origin "$TAG" >/dev/null 2>&1; then
        fail "tag ${TAG} is not on origin; run --push first"
    fi

    # Attaching the assets is the whole point now that no CI job uploads them.
    # Exactly the four named assets. A glob over dist/ would also sweep up the
    # extension .zip byproducts the .crx and .xpi are made from, and a release listing
    # six downloads where two are duplicates of the other two is just confusing.
    require_assets

    if [ "$RESUME_DRAFT" = true ]; then
        # --clobber so a half-finished asset from the interrupted run is replaced.
        run gh release upload "$TAG" --clobber "${RELEASE_ASSETS[@]}" \
            || fail "uploading the release assets failed"
        run gh release edit "$TAG" --draft=false \
            || fail "publishing the draft failed"
    else
        run gh release create "$TAG" \
            --title "$RELEASE_TITLE" \
            --generate-notes \
            "${RELEASE_ASSETS[@]}" \
            || fail "creating the release failed"
    fi

    # gh exits before GitHub finishes processing large uploads, and an asset stuck in
    # "starting" is a broken download link on a published release.
    if [ "$DRY_RUN" = false ]; then
        UPLOADED=$(gh release view "$TAG" --json assets -q '[.assets[]|select(.state=="uploaded")]|length')
        [ "$UPLOADED" = "${#RELEASE_ASSETS[@]}" ] \
            || fail "only ${UPLOADED}/${#RELEASE_ASSETS[@]} assets finished uploading"
        echo "Published ${TAG} with ${UPLOADED} assets."
    fi
fi
