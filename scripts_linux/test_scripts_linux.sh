#!/usr/bin/env bash
# ==============================================================================
# test_scripts_linux.sh — Comprehensive Test Suite for Linux Terminal Scripts
# ==============================================================================
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

TESTS_RUN=0
TESTS_PASSED=0
TESTS_FAILED=0

COLOR_RESET="\033[0m"
COLOR_GREEN="\033[0;32m"
COLOR_RED="\033[0;31m"
COLOR_YELLOW="\033[0;33m"
COLOR_CYAN="\033[0;36m"
COLOR_WHITE="\033[1;37m"

report_pass() {
    local name="$1"
    TESTS_RUN=$((TESTS_RUN + 1))
    TESTS_PASSED=$((TESTS_PASSED + 1))
    printf "  ${COLOR_GREEN}PASS${COLOR_RESET} : %s\n" "$name"
}

report_fail() {
    local name="$1"
    local reason="${2:-}"
    TESTS_RUN=$((TESTS_RUN + 1))
    TESTS_FAILED=$((TESTS_FAILED + 1))
    printf "  ${COLOR_RED}FAIL${COLOR_RESET} : %s\n" "$name"
    if [[ -n "$reason" ]]; then
        printf "         ${COLOR_YELLOW}%s${COLOR_RESET}\n" "$reason"
    fi
}

start_suite() {
    printf "\n${COLOR_CYAN}=== Suite: %s ===${COLOR_RESET}\n" "$1"
}

# ==============================================================================
# Suite A: Syntax and Static File Permissions
# ==============================================================================
start_suite "Static Analysis & File Integrity (Suite A)"

ALL_SH_SCRIPTS=(
    "$REPO_ROOT/install.sh"
    "$SCRIPT_DIR/common.sh"
    "$SCRIPT_DIR/install-chum.sh"
    "$SCRIPT_DIR/uninstall-chum.sh"
    "$SCRIPT_DIR/publish-release.sh"
    "$SCRIPT_DIR/quick-deploy.sh"
    "$SCRIPT_DIR/start-asp-rag.sh"
    "$SCRIPT_DIR/start-fast-llm-api.sh"
    "$SCRIPT_DIR/start-internet-search.sh"
    "$SCRIPT_DIR/start-llm-api.sh"
    "$SCRIPT_DIR/start-whisper-api.sh"
    "$SCRIPT_DIR/stop-internet-search.sh"
    "$SCRIPT_DIR/stop-qwen.sh"
    "$SCRIPT_DIR/stop-whisper.sh"
    "$SCRIPT_DIR/whisper-finetune/run-finetune.sh"
)

# TC-CLI-01: Bash syntax check
syntax_errors=0
for s in "${ALL_SH_SCRIPTS[@]}"; do
    if bash -n "$s" 2>&1; then
        :
    else
        syntax_errors=$((syntax_errors + 1))
        report_fail "TC-CLI-01: Syntax error in $(basename "$s")"
    fi
done
if [[ $syntax_errors -eq 0 ]]; then
    report_pass "TC-CLI-01: All ${#ALL_SH_SCRIPTS[@]} scripts passed bash -n syntax validation"
fi

# TC-CLI-02: Executable bit verification
perm_errors=0
for s in "${ALL_SH_SCRIPTS[@]}"; do
    if [[ -x "$s" ]]; then
        :
    else
        perm_errors=$((perm_errors + 1))
        report_fail "TC-CLI-02: Executable bit missing on $(basename "$s")"
    fi
done
if [[ -x "$SCRIPT_DIR/start-llm.py" ]]; then
    :
else
    perm_errors=$((perm_errors + 1))
    report_fail "TC-CLI-02: Executable bit missing on start-llm.py"
fi
if [[ $perm_errors -eq 0 ]]; then
    report_pass "TC-CLI-02: All scripts have executable (+x) permissions"
fi

# TC-CLI-03: Help argument handling
help_errors=0
for s in "${ALL_SH_SCRIPTS[@]}"; do
    # Skip common.sh as it is a library
    if [[ "$(basename "$s")" == "common.sh" || "$(basename "$s")" == "install.sh" ]]; then
        continue
    fi
    out=$("$s" --help 2>&1)
    code=$?
    if [[ $code -eq 0 && ("$out" =~ "Usage" || "$out" =~ "Options") ]]; then
        :
    else
        help_errors=$((help_errors + 1))
        report_fail "TC-CLI-03: $(basename "$s") --help failed (exit: $code)"
    fi
done
if [[ $help_errors -eq 0 ]]; then
    report_pass "TC-CLI-03: All scripts respond cleanly to --help with usage documentation"
fi

# ==============================================================================
# Suite B: Process and Port Management (stop-*.sh)
# ==============================================================================
start_suite "Port & Process Management (Suite B)"

# TC-STOP-01: stop-internet-search.sh
out=$("$SCRIPT_DIR/stop-internet-search.sh" 8092 2>&1)
if [[ $? -eq 0 && "$out" =~ "not running" ]]; then
    report_pass "TC-STOP-01A: stop-internet-search.sh reports not running when port is idle"
else
    report_fail "TC-STOP-01A: stop-internet-search.sh failed on idle port" "$out"
fi

# Start mock listener on 8092
python3 -m http.server 8092 >/dev/null 2>&1 &
MOCK_PID=$!
sleep 0.8
if kill -0 "$MOCK_PID" 2>/dev/null; then
    out=$("$SCRIPT_DIR/stop-internet-search.sh" 8092 2>&1)
    sleep 0.5
    if ! kill -0 "$MOCK_PID" 2>/dev/null; then
        report_pass "TC-STOP-01B: stop-internet-search.sh detected and terminated active process on port"
    else
        kill -9 "$MOCK_PID" 2>/dev/null || true
        report_fail "TC-STOP-01B: Process was not killed by stop script" "$out"
    fi
else
    report_fail "TC-STOP-01B: Mock server failed to start on port 8092"
fi

# TC-STOP-02: stop-qwen.sh
out=$("$SCRIPT_DIR/stop-qwen.sh" 8091 2>&1)
if [[ $? -eq 0 && "$out" =~ "not running" ]]; then
    report_pass "TC-STOP-02A: stop-qwen.sh reports not running when port is idle"
else
    report_fail "TC-STOP-02A: stop-qwen.sh failed on idle port" "$out"
fi

python3 -m http.server 8091 >/dev/null 2>&1 &
MOCK_PID=$!
sleep 0.8
if kill -0 "$MOCK_PID" 2>/dev/null; then
    out=$("$SCRIPT_DIR/stop-qwen.sh" 8091 2>&1)
    sleep 0.5
    if ! kill -0 "$MOCK_PID" 2>/dev/null; then
        report_pass "TC-STOP-02B: stop-qwen.sh detected and terminated active process on port"
    else
        kill -9 "$MOCK_PID" 2>/dev/null || true
        report_fail "TC-STOP-02B: Process was not killed by stop-qwen.sh" "$out"
    fi
else
    report_fail "TC-STOP-02B: Mock server failed to start on port 8091"
fi

# TC-STOP-03: stop-whisper.sh
out=$("$SCRIPT_DIR/stop-whisper.sh" 8090 2>&1)
if [[ $? -eq 0 && "$out" =~ "not running" ]]; then
    report_pass "TC-STOP-03A: stop-whisper.sh reports not running when port is idle"
else
    report_fail "TC-STOP-03A: stop-whisper.sh failed on idle port" "$out"
fi

python3 -m http.server 8090 >/dev/null 2>&1 &
MOCK_PID=$!
sleep 0.8
if kill -0 "$MOCK_PID" 2>/dev/null; then
    out=$("$SCRIPT_DIR/stop-whisper.sh" 8090 2>&1)
    sleep 0.5
    if ! kill -0 "$MOCK_PID" 2>/dev/null; then
        report_pass "TC-STOP-03B: stop-whisper.sh detected and terminated active process on port"
    else
        kill -9 "$MOCK_PID" 2>/dev/null || true
        report_fail "TC-STOP-03B: Process was not killed by stop-whisper.sh" "$out"
    fi
else
    report_fail "TC-STOP-03B: Mock server failed to start on port 8090"
fi

# ==============================================================================
# Suite C: Service Start Scripts (start-*.sh)
# ==============================================================================
start_suite "Start Scripts Configuration & Dry Run (Suite C)"

# TC-START-01: start-asp-rag.sh
# Check missing ASP wheel error when not in dry-run
out=$("$SCRIPT_DIR/start-asp-rag.sh" --asp-wheel-path "/nonexistent/asp.whl" 2>&1 || true)
if [[ "$out" =~ "ASP wheel not found" || "$out" =~ "ERROR" ]]; then
    report_pass "TC-START-01A: start-asp-rag.sh correctly validates missing wheel package"
else
    report_fail "TC-START-01A: start-asp-rag.sh did not fail on missing wheel" "$out"
fi

# Check dry run with dummy wheel
TMP_WHL=$(mktemp /tmp/asp-test-XXXX.whl)
touch "$TMP_WHL"
out=$("$SCRIPT_DIR/start-asp-rag.sh" --port 8083 --asp-wheel-path "$TMP_WHL" --dry-run 2>&1)
code=$?
rm -f "$TMP_WHL"
if [[ $code -eq 0 && "$out" =~ "ASP RAG bridge configuration verified" ]]; then
    report_pass "TC-START-01B: start-asp-rag.sh dry-run passed with custom port and wheel"
else
    report_fail "TC-START-01B: start-asp-rag.sh dry-run failed" "$out"
fi

# TC-START-02: start-internet-search.sh
out=$("$SCRIPT_DIR/start-internet-search.sh" --port 8082 --dry-run 2>&1)
code=$?
if [[ $code -eq 0 && "$out" =~ "Google AI Search bridge verified" ]]; then
    report_pass "TC-START-02A: start-internet-search.sh dry-run passed in standard mode"
else
    report_fail "TC-START-02A: start-internet-search.sh dry-run failed" "$out"
fi

out=$("$SCRIPT_DIR/start-internet-search.sh" --port 8082 --network-access --cdp --dry-run 2>&1)
code=$?
if [[ $code -eq 0 && "$out" =~ "CDP mode" && "$out" =~ "Network access enabled" ]]; then
    report_pass "TC-START-02B: start-internet-search.sh dry-run handled --cdp and --network-access flags"
else
    report_fail "TC-START-02B: start-internet-search.sh CDP/network flags test failed" "$out"
fi

# TC-START-03: start-fast-llm-api.sh
# Magic header test: invalid file
TMP_CORRUPT=$(mktemp /tmp/bad-model-XXXX.gguf)
echo "NOT_GGUF" > "$TMP_CORRUPT"
source "$SCRIPT_DIR/common.sh"
if ! test_gguf_magic "$TMP_CORRUPT"; then
    report_pass "TC-START-03A: test_gguf_magic correctly rejects non-GGUF binary header"
else
    report_fail "TC-START-03A: test_gguf_magic accepted corrupted header"
fi

# Magic header test: valid file
TMP_VALID=$(mktemp /tmp/good-model-XXXX.gguf)
printf "GGUF\x01\x00\x00\x00" > "$TMP_VALID"
if test_gguf_magic "$TMP_VALID"; then
    report_pass "TC-START-03B: test_gguf_magic correctly validates GGUF magic bytes"
else
    report_fail "TC-START-03B: test_gguf_magic rejected valid GGUF header"
fi
rm -f "$TMP_CORRUPT" "$TMP_VALID"

out=$("$SCRIPT_DIR/start-fast-llm-api.sh" --port 8083 --dry-run 2>&1)
code=$?
if [[ $code -eq 0 && "$out" =~ "Fast LLM API" && "$out" =~ "DRY-RUN" ]]; then
    report_pass "TC-START-03C: start-fast-llm-api.sh dry-run passed and generated proper launch flags"
else
    report_fail "TC-START-03C: start-fast-llm-api.sh dry-run failed" "$out"
fi

# TC-START-04: start-llm-api.sh
out=$("$SCRIPT_DIR/start-llm-api.sh" --port 8081 --thinking --no-auth --dry-run 2>&1)
code=$?
if [[ $code -eq 0 && "$out" =~ "Chum LLM + Vision API" && "$out" =~ "Thinking : ON" && "$out" =~ "DRY-RUN" ]]; then
    report_pass "TC-START-04: start-llm-api.sh dry-run passed with vision and thinking mode enabled"
else
    report_fail "TC-START-04: start-llm-api.sh dry-run failed" "$out"
fi

# TC-START-05: start-whisper-api.sh
out=$("$SCRIPT_DIR/start-whisper-api.sh" --port 8080 --threads 6 --language es --dry-run 2>&1)
code=$?
if [[ $code -eq 0 && "$out" =~ "Chum Whisper API" && "$out" =~ "Threads  : 6" && "$out" =~ "Language : es" ]]; then
    report_pass "TC-START-05: start-whisper-api.sh dry-run passed with custom thread and language configuration"
else
    report_fail "TC-START-05: start-whisper-api.sh dry-run failed" "$out"
fi

# ==============================================================================
# Suite D: Lifecycle & Deployment Scripts
# ==============================================================================
start_suite "Installation & Deployment Lifecycle (Suite D)"

# TC-DEP-01: quick-deploy.sh
out=$("$SCRIPT_DIR/quick-deploy.sh" --install-dir "/nonexistent/chum/App" 2>&1 || true)
if [[ "$out" =~ "Chum is not installed at" ]]; then
    report_pass "TC-DEP-01A: quick-deploy.sh safely exits when install dir is missing"
else
    report_fail "TC-DEP-01A: quick-deploy.sh missing install dir test failed" "$out"
fi

# TC-DEP-02: publish-release.sh
out=$("$SCRIPT_DIR/publish-release.sh" --dry-run 2>&1)
code=$?
if [[ $code -eq 0 && "$out" =~ "Version" && "$out" =~ "Target archives" ]]; then
    report_pass "TC-DEP-02: publish-release.sh dry-run passed and extracted project version"
else
    report_fail "TC-DEP-02: publish-release.sh dry-run failed" "$out"
fi

# TC-DEP-03: install-chum.sh isolated mock test
TMP_INSTALL_DIR=$(mktemp -d /tmp/chum-test-install-XXXX)
TMP_DATA_DIR=$(mktemp -d /tmp/chum-test-data-XXXX)
TMP_BIN_DIR=$(mktemp -d /tmp/chum-test-bins-XXXX)

# Create mock prebuilt binaries
mkdir -p "$TMP_BIN_DIR/App" "$TMP_BIN_DIR/Service"
echo "#!/bin/sh" > "$TMP_BIN_DIR/App/Chum.App"
echo "#!/bin/sh" > "$TMP_BIN_DIR/Service/ChumHostSvc"
chmod +x "$TMP_BIN_DIR/App/Chum.App" "$TMP_BIN_DIR/Service/ChumHostSvc"

out=$("$SCRIPT_DIR/install-chum.sh" \
    --install-dir "$TMP_INSTALL_DIR" \
    --data-dir "$TMP_DATA_DIR" \
    --source prebuilt \
    --bin-path "$TMP_BIN_DIR" \
    --non-interactive 2>&1)
code=$?

if [[ $code -eq 0 && -f "$TMP_INSTALL_DIR/App/Chum.App" && -f "$TMP_INSTALL_DIR/App/config.json" && -f "$TMP_INSTALL_DIR/Service/ChumHostSvc" ]]; then
    report_pass "TC-DEP-03A: install-chum.sh successfully installs files and creates config.json"
else
    report_fail "TC-DEP-03A: install-chum.sh installation failed (exit: $code)" "$out"
fi

# TC-DEP-04: uninstall-chum.sh isolated test
out=$("$SCRIPT_DIR/uninstall-chum.sh" \
    --install-dir "$TMP_INSTALL_DIR" \
    --data-dir "$TMP_DATA_DIR" 2>&1)
code=$?

if [[ $code -eq 0 && ! -d "$TMP_INSTALL_DIR" && -d "$TMP_DATA_DIR" ]]; then
    report_pass "TC-DEP-04A: uninstall-chum.sh removes program files while preserving data directory"
else
    report_fail "TC-DEP-04A: uninstall-chum.sh failed data preservation test" "$out"
fi

# Clean up data dir with --remove-data
out=$("$SCRIPT_DIR/uninstall-chum.sh" \
    --install-dir "$TMP_INSTALL_DIR" \
    --data-dir "$TMP_DATA_DIR" \
    --remove-data 2>&1)
if [[ ! -d "$TMP_DATA_DIR" ]]; then
    report_pass "TC-DEP-04B: uninstall-chum.sh removes data directory when --remove-data is passed"
else
    report_fail "TC-DEP-04B: uninstall-chum.sh did not remove data dir with --remove-data"
fi

rm -rf "$TMP_BIN_DIR" "$TMP_INSTALL_DIR" "$TMP_DATA_DIR"

# TC-DEP-05: install.sh root launcher test
out=$("$REPO_ROOT/install.sh" --help 2>&1)
code=$?
if [[ $code -eq 0 && "$out" =~ "Chum Installer (Linux)" && "$out" =~ "--install-dir" ]]; then
    report_pass "TC-DEP-05: Root install.sh cleanly delegates options to install-chum.sh"
else
    report_fail "TC-DEP-05: Root install.sh test failed" "$out"
fi

# ==============================================================================
# Suite E: Whisper Fine-Tuning
# ==============================================================================
start_suite "Whisper Fine-Tuning (Suite E)"

out=$("$SCRIPT_DIR/whisper-finetune/run-finetune.sh" --dry-run 2>&1)
code=$?
if [[ $code -eq 0 && "$out" =~ "Technical Domain Fine-Tune" && "$out" =~ "Dependency check completed" ]]; then
    report_pass "TC-TUNE-01: whisper-finetune/run-finetune.sh dry-run checked dependencies and environment"
else
    report_fail "TC-TUNE-01: whisper-finetune/run-finetune.sh dry-run failed" "$out"
fi

# ==============================================================================
# Test Summary
# ==============================================================================
printf "\n${COLOR_WHITE}=========================================${COLOR_RESET}\n"
printf "${COLOR_WHITE}           TEST RUN SUMMARY             ${COLOR_RESET}\n"
printf "${COLOR_WHITE}=========================================${COLOR_RESET}\n"
printf "  Total Tests Run : %s\n" "$TESTS_RUN"
printf "  Passed          : ${COLOR_GREEN}%s${COLOR_RESET}\n" "$TESTS_PASSED"
printf "  Failed          : ${COLOR_RED}%s${COLOR_RESET}\n" "$TESTS_FAILED"
printf "${COLOR_WHITE}=========================================${COLOR_RESET}\n\n"

if [[ $TESTS_FAILED -eq 0 ]]; then
    exit 0
else
    exit 1
fi
