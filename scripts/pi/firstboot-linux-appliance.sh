#!/usr/bin/env bash
# MouseKeyProxy first-boot appliance provisioning for generic Linux (Armbian, etc.).
# Safe to re-run: idempotent enablement of user, SSH, HID gadget, and service unit.
#
# Env (optional, typically from /etc/mousekeyproxy/board.env or status.env):
#   MKP_HOSTNAME, MKP_USER, MKP_PUBLIC_KEY, MKP_WIFI_SSID, MKP_WIFI_PSK, MKP_WIFI_COUNTRY
#   MKP_BOARD_ID (orange-pi-zero-2 | raspberry-pi-zero-2)
set -u
LOG_DIR=/var/log/mousekeyproxy
mkdir -p "${LOG_DIR}" /etc/mousekeyproxy /var/lib/mousekeyproxy /usr/local/sbin /opt/mousekeyproxy
LOG="${LOG_DIR}/firstboot.log"
exec >>"${LOG}" 2>&1
echo "=== MKP linux appliance firstboot $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="

# shellcheck disable=SC1091
[[ -f /etc/mousekeyproxy/board.env ]] && . /etc/mousekeyproxy/board.env
# shellcheck disable=SC1091
[[ -f /boot/mkp-wifi.env ]] && . /boot/mkp-wifi.env
# shellcheck disable=SC1091
[[ -f /boot/firmware/mkp-wifi.env ]] && . /boot/firmware/mkp-wifi.env

MKP_HOSTNAME="${MKP_HOSTNAME:-${MKP_HOSTNAME_DEFAULT:-mkp-hid-opi}}"
MKP_USER="${MKP_USER:-${MKP_USER_DEFAULT:-mkp}}"
MKP_PUBLIC_KEY="${MKP_PUBLIC_KEY:-}"
MKP_WIFI_SSID="${MKP_WIFI_SSID:-}"
MKP_WIFI_PSK="${MKP_WIFI_PSK:-}"
MKP_WIFI_COUNTRY="${MKP_WIFI_COUNTRY:-US}"
MKP_BOARD_ID="${MKP_BOARD_ID:-orange-pi-zero-2}"
MKP_HID_BACKEND="${MKP_HID_BACKEND:-physical-configfs}"

printf '%s\n' "${MKP_HOSTNAME}" >/etc/hostname 2>/dev/null || true
hostnamectl set-hostname "${MKP_HOSTNAME}" 2>/dev/null || true
if ! grep -qF "${MKP_HOSTNAME}" /etc/hosts 2>/dev/null; then
  printf '127.0.1.1 %s\n' "${MKP_HOSTNAME}" >>/etc/hosts 2>/dev/null || true
fi

# Disable interactive Armbian first-login wizard when present.
rm -f /root/.not_logged_in_yet 2>/dev/null || true

if ! id -u "${MKP_USER}" >/dev/null 2>&1; then
  useradd -m -s /bin/bash -G sudo,netdev "${MKP_USER}" 2>/dev/null \
    || useradd -m -s /bin/bash "${MKP_USER}" 2>/dev/null \
    || true
fi

if id -u "${MKP_USER}" >/dev/null 2>&1; then
  HOME_DIR=$(getent passwd "${MKP_USER}" | cut -d: -f6)
  [[ -n "${HOME_DIR}" ]] || HOME_DIR="/home/${MKP_USER}"
  install -d -m 0700 -o "${MKP_USER}" -g "${MKP_USER}" "${HOME_DIR}/.ssh" 2>/dev/null || true
  if [[ -n "${MKP_PUBLIC_KEY}" ]]; then
    grep -qxF "${MKP_PUBLIC_KEY}" "${HOME_DIR}/.ssh/authorized_keys" 2>/dev/null \
      || printf '%s\n' "${MKP_PUBLIC_KEY}" >>"${HOME_DIR}/.ssh/authorized_keys"
    chown "${MKP_USER}:${MKP_USER}" "${HOME_DIR}/.ssh/authorized_keys" 2>/dev/null || true
    chmod 0600 "${HOME_DIR}/.ssh/authorized_keys" 2>/dev/null || true
  fi
  # Passwordless sudo for appliance user (lab image).
  echo "${MKP_USER} ALL=(ALL) NOPASSWD:ALL" >"/etc/sudoers.d/010-${MKP_USER}-nopasswd"
  chmod 0440 "/etc/sudoers.d/010-${MKP_USER}-nopasswd" 2>/dev/null || true
fi

# Optional password hash file written offline by prepare script (mkpasswd / shadow line).
if [[ -f /etc/mousekeyproxy/mkp-user.password-hash ]]; then
  HASH=$(tr -d '\n' </etc/mousekeyproxy/mkp-user.password-hash)
  if [[ -n "${HASH}" ]] && id -u "${MKP_USER}" >/dev/null 2>&1; then
    echo "${MKP_USER}:${HASH}" | chpasswd -e 2>/dev/null || true
  fi
fi

systemctl enable ssh 2>/dev/null || systemctl enable sshd 2>/dev/null || true
systemctl restart ssh 2>/dev/null || systemctl restart sshd 2>/dev/null || true

# Wi-Fi via NetworkManager when credentials are present.
if [[ -n "${MKP_WIFI_SSID}" && -n "${MKP_WIFI_PSK}" ]]; then
  if command -v nmcli >/dev/null 2>&1; then
    nmcli radio wifi on 2>/dev/null || true
    CON_NAME="mkp-${MKP_WIFI_SSID}"
    nmcli -t -f NAME connection show 2>/dev/null | grep -qxF "${CON_NAME}" \
      || nmcli connection add type wifi ifname '*' con-name "${CON_NAME}" ssid "${MKP_WIFI_SSID}" \
           wifi-sec.key-mgmt wpa-psk wifi-sec.psk "${MKP_WIFI_PSK}" connection.autoconnect yes \
      || true
    nmcli connection up "${CON_NAME}" 2>/dev/null || true
  fi
fi

cat >/etc/mousekeyproxy/status.env <<MKP_STATUS_ENV
MKP_FEATURE_WIFI='enabled'
MKP_FEATURE_SSH='enabled'
MKP_HID_KEYBOARD='enabled'
MKP_HID_MOUSE='enabled'
MKP_HID_BACKEND='${MKP_HID_BACKEND}'
MKP_BOARD_ID='${MKP_BOARD_ID}'
MKP_HOSTNAME='${MKP_HOSTNAME}'
MKP_USER='${MKP_USER}'
MKP_WIFI_SSID='${MKP_WIFI_SSID}'
MKP_HID_KEYBOARD_DEVICE='/dev/hidg0'
MKP_HID_MOUSE_DEVICE='/dev/hidg1'
MKP_TOFU='1'
MKP_STATE_DIR='/var/lib/mousekeyproxy'
MKP_DASHBOARD_TTY='${MKP_DASHBOARD_TTY:-/dev/tty1}'
MKP_HDMI_ENABLED='${MKP_HDMI_ENABLED:-1}'
MKP_HDMI_DISP_MODE='${MKP_HDMI_DISP_MODE:-1080p60}'
MKP_STATUS_ENV

# Ensure a getty on tty1 so the HDMI console is usable for the service dashboard.
if command -v systemctl >/dev/null 2>&1; then
  systemctl enable getty@tty1.service 2>/dev/null || true
  systemctl start getty@tty1.service 2>/dev/null || true
fi

cat >/var/lib/mousekeyproxy/pairing.env <<'MKP_PAIRING_ENV'
MKP_PAIR_HOST='unpaired'
MKP_PAIR_REMOTE='unpaired'
MKP_PAIR_UPDATED_UTC='never'
MKP_PAIRING_ENV
: >/var/log/mousekeyproxy/events.log
printf '%s firstboot board=%s backend=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "${MKP_BOARD_ID}" "${MKP_HID_BACKEND}" \
  >>/var/log/mousekeyproxy/events.log

# Enable only (plus optional non-blocking start). Do NOT use blocking
# systemctl start/restart here: mkp-firstboot.service has
# Before=mkp-hid-gadget.service mousekeyproxy.service, so a blocking start
# of those units from this oneshot deadlocks multi-user (job waits forever).
if [[ -x /usr/local/sbin/mkp-hid-gadget-setup.sh ]]; then
  systemctl enable mkp-hid-gadget.service 2>/dev/null || true
  systemctl start --no-block mkp-hid-gadget.service 2>/dev/null || true
fi

if [[ -x /opt/mousekeyproxy/MouseKeyProxy.Service || -f /opt/mousekeyproxy/MouseKeyProxy.Service ]]; then
  systemctl enable mousekeyproxy.service 2>/dev/null || true
  systemctl start --no-block mousekeyproxy.service 2>/dev/null || true
fi

# One-shot marker so the unit can ConditionPathExists unless force re-run.
touch /var/lib/mousekeyproxy/firstboot.done
echo "=== MKP linux appliance firstboot done $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
