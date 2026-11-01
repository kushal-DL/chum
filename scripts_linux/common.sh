#!/usr/bin/env bash
# ==============================================================================
# common.sh — Shared utility functions for Chum Linux scripts
# ==============================================================================

# ANSI Color definitions
COLOR_RESET="\033[0m"
COLOR_RED="\033[0;31m"
COLOR_GREEN="\033[0;32m"
COLOR_YELLOW="\033[0;33m"
COLOR_CYAN="\033[0;36m"
COLOR_WHITE="\033[1;37m"
COLOR_GRAY="\033[0;90m"

log_step() {
    printf "  ${COLOR_CYAN}%s${COLOR_RESET}\n" "$*"
}

log_ok() {
    printf "  ${COLOR_GREEN}[OK] %s${COLOR_RESET}\n" "$*"
}

log_warn() {
    printf "  ${COLOR_YELLOW}WARNING: %s${COLOR_RESET}\n" "$*" >&2
}

log_err() {
    printf "  ${COLOR_RED}ERROR: %s${COLOR_RESET}\n" "$*" >&2
}

log_skip() {
    printf "  ${COLOR_GRAY}[--] %s${COLOR_RESET}\n" "$*"
}

log_info() {
    printf "%s\n" "$*"
}

# Resolve LAN IPv4 address
get_lan_ip() {
    local lan_ip=""

    # Try ip route get (standard on modern Linux)
    if command -v ip >/dev/null 2>&1; then
        lan_ip=$(ip route get 1.1.1.1 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($i=="src") {print $(i+1); exit}}')
        if [[ -z "$lan_ip" ]]; then
            lan_ip=$(ip -4 addr show scope global 2>/dev/null | grep -oP '(?<=inet\s)\d+(\.\d+){3}' | grep -vE '^(127\.|169\.254\.)' | head -n 1)
        fi
    fi

    # Fallback to hostname -I
    if [[ -z "$lan_ip" ]] && command -v hostname >/dev/null 2>&1; then
        lan_ip=$(hostname -I 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($i !~ /^(127\.|169\.254\.)/) {print $i; exit}}')
    fi

    if [[ -z "$lan_ip" ]]; then
        lan_ip="127.0.0.1"
    fi

    printf "%s" "$lan_ip"
}

# Find process ID(s) listening on a TCP port
find_pid_by_port() {
    local port="$1"
    local pids=""

    # 1. Try ss
    if command -v ss >/dev/null 2>&1; then
        pids=$(ss -H -tulpn "sport = :${port}" 2>/dev/null | grep -oP 'pid=\K[0-9]+' | sort -u | tr '\n' ' ')
    fi

    # 2. Try lsof if ss didn't return pids
    if [[ -z "${pids// /}" ]] && command -v lsof >/dev/null 2>&1; then
        pids=$(lsof -tiTCP:"${port}" -sTCP:LISTEN 2>/dev/null | sort -u | tr '\n' ' ')
    fi

    # 3. Try fuser if still empty
    if [[ -z "${pids// /}" ]] && command -v fuser >/dev/null 2>&1; then
        pids=$(fuser "${port}/tcp" 2>/dev/null | tr '\n' ' ')
    fi

    # Clean leading/trailing spaces
    pids=$(echo "$pids" | xargs)
    printf "%s" "$pids"
}

# Gracefully terminate process(es) holding a port
kill_port_process() {
    local port="$1"
    local label="${2:-Service on port $port}"
    local timeout="${3:-3}"

    local pids
    pids=$(find_pid_by_port "$port")

    if [[ -n "$pids" ]]; then
        printf "Stopping %s (PID %s) on port %s ...\n" "$label" "$pids" "$port"
        for pid in $pids; do
            kill -15 "$pid" 2>/dev/null || true
        done

        local elapsed=0
        while [[ $elapsed -lt $timeout ]]; do
            sleep 0.5
            elapsed=$((elapsed + 1))
            pids=$(find_pid_by_port "$port")
            if [[ -z "$pids" ]]; then
                break
            fi
        done

        # If still running after timeout, force kill
        if [[ -n "$pids" ]]; then
            for pid in $pids; do
                kill -9 "$pid" 2>/dev/null || true
            done
            sleep 0.5
        fi

        pids=$(find_pid_by_port "$port")
        if [[ -z "$pids" ]]; then
            log_ok "$label stopped."
            return 0
        else
            log_err "Failed to stop $label (PIDs: $pids). Try running with sudo."
            return 1
        fi
    else
        printf "%s is not running (nothing listening on port %s).\n" "$label" "$port"
        return 0
    fi
}

# Check Python availability and minimum version
check_python() {
    local min_ver="${1:-3.10}"
    local py_cmd=""

    if command -v python3 >/dev/null 2>&1; then
        py_cmd="python3"
    elif command -v python >/dev/null 2>&1; then
        py_cmd="python"
    else
        log_err "Python 3 not found in PATH. Please install Python ${min_ver}+."
        return 1
    fi

    local ver_ok
    ver_ok=$($py_cmd -c "import sys; print(int(sys.version_info >= tuple(map(int, '${min_ver}'.split('.')))))" 2>/dev/null || echo "0")
    if [[ "$ver_ok" != "1" ]]; then
        local curr_ver
        curr_ver=$($py_cmd --version 2>&1)
        log_err "Python ${min_ver}+ required. Found: $curr_ver"
        return 1
    fi

    printf "%s" "$py_cmd"
    return 0
}

# Verify GGUF magic header in a binary file
test_gguf_magic() {
    local file_path="$1"
    if [[ ! -f "$file_path" ]]; then
        return 1
    fi

    local size
    size=$(wc -c < "$file_path" 2>/dev/null || stat -c%s "$file_path" 2>/dev/null || echo "0")
    if [[ "$size" -lt 4 ]]; then
        return 1
    fi

    local magic
    magic=$(head -c 4 "$file_path" 2>/dev/null)
    if [[ "$magic" == "GGUF" ]]; then
        return 0
    fi

    return 1
}
