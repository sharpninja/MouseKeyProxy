#!/usr/bin/env bash
# MouseKeyProxy USB HID gadget (keyboard /dev/hidg0 + mouse /dev/hidg1).
#
# IMPORTANT: write report descriptors as binary, never as ASCII "\x05..." text.
# /bin/sh on Raspberry Pi OS is dash, whose printf does NOT interpret \xHH
# (it writes the four characters '\','x','0','5'). That yields a 252-byte
# garbage keyboard descriptor; the USB host may partially configure the
# gadget but never polls the interrupt IN endpoint, so writes to /dev/hidg*
# fail with EAGAIN / ESHUTDOWN.
#
# This script uses base64-decoded binary descriptors so it is safe under
# both bash and dash (when invoked as `bash setup-configfs-gadget.sh`).
set -euo pipefail

GADGET_NAME="${MKP_HID_GADGET_NAME:-mkp_hid}"
GADGET_ROOT="/sys/kernel/config/usb_gadget/${GADGET_NAME}"
UDC_NAME="${MKP_HID_UDC:-$(ls /sys/class/udc | head -n 1)}"

# Boot keyboard report descriptor (63 bytes) + relative mouse (52 bytes).
# base64 keeps the payload shell-portable (no dash \xHH pitfall).
KEYBOARD_DESC_B64='BQEJBqEBBQcZ4CnnFQAlAXUBlQiBApUBdQiBAZUFdQEFCBkBKQWRApUBdQORAZUGdQgVACVlBQcZACllgQDA'
MOUSE_DESC_B64='BQEJAqEBCQGhAAUJGQEpAxUAJQGVA3UBgQKVAXUFgQEFAQkwCTEJOBWBJX91CJUDgQbAwA=='
KEYBOARD_DESC_LEN=63
MOUSE_DESC_LEN=52

if [[ "${EUID}" -ne 0 ]]; then
  echo "setup-configfs-gadget.sh must run as root" >&2
  exit 1
fi

if [[ -z "${UDC_NAME}" ]]; then
  echo "no USB device controller found under /sys/class/udc" >&2
  exit 1
fi

modprobe libcomposite

# Tear down a previous instance so report_desc can be rewritten.
if [[ -d "${GADGET_ROOT}" ]]; then
  if [[ -f "${GADGET_ROOT}/UDC" ]]; then
    echo "" > "${GADGET_ROOT}/UDC" || true
  fi
  sleep 0.2
  rm -f "${GADGET_ROOT}/configs/c.1/hid.keyboard" "${GADGET_ROOT}/configs/c.1/hid.mouse" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/configs/c.1/strings/0x409" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/configs/c.1" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/functions/hid.keyboard" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/functions/hid.mouse" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/strings/0x409" 2>/dev/null || true
  rmdir "${GADGET_ROOT}" 2>/dev/null || true
fi

mkdir -p "${GADGET_ROOT}"

echo 0x1d6b > "${GADGET_ROOT}/idVendor"
echo 0x0104 > "${GADGET_ROOT}/idProduct"
echo 0x0100 > "${GADGET_ROOT}/bcdDevice"
echo 0x0200 > "${GADGET_ROOT}/bcdUSB"

mkdir -p "${GADGET_ROOT}/strings/0x409"
if [[ -r /proc/device-tree/serial-number ]]; then
  tr -d '\0' </proc/device-tree/serial-number > "${GADGET_ROOT}/strings/0x409/serialnumber"
else
  echo "MKP0001" > "${GADGET_ROOT}/strings/0x409/serialnumber"
fi
echo "MouseKeyProxy" > "${GADGET_ROOT}/strings/0x409/manufacturer"
echo "MouseKeyProxy Pi HID" > "${GADGET_ROOT}/strings/0x409/product"

mkdir -p "${GADGET_ROOT}/configs/c.1/strings/0x409"
echo "Keyboard and Mouse" > "${GADGET_ROOT}/configs/c.1/strings/0x409/configuration"
echo 250 > "${GADGET_ROOT}/configs/c.1/MaxPower"

mkdir -p "${GADGET_ROOT}/functions/hid.keyboard"
echo 1 > "${GADGET_ROOT}/functions/hid.keyboard/protocol"
echo 1 > "${GADGET_ROOT}/functions/hid.keyboard/subclass"
echo 8 > "${GADGET_ROOT}/functions/hid.keyboard/report_length"
printf '%s' "${KEYBOARD_DESC_B64}" | base64 -d > "${GADGET_ROOT}/functions/hid.keyboard/report_desc"

mkdir -p "${GADGET_ROOT}/functions/hid.mouse"
echo 2 > "${GADGET_ROOT}/functions/hid.mouse/protocol"
echo 1 > "${GADGET_ROOT}/functions/hid.mouse/subclass"
echo 4 > "${GADGET_ROOT}/functions/hid.mouse/report_length"
printf '%s' "${MOUSE_DESC_B64}" | base64 -d > "${GADGET_ROOT}/functions/hid.mouse/report_desc"

kb_len=$(wc -c < "${GADGET_ROOT}/functions/hid.keyboard/report_desc")
ms_len=$(wc -c < "${GADGET_ROOT}/functions/hid.mouse/report_desc")
kb_first=$(od -An -tx1 -N1 "${GADGET_ROOT}/functions/hid.keyboard/report_desc" | tr -d ' \n')
if [[ "${kb_len}" -ne "${KEYBOARD_DESC_LEN}" || "${ms_len}" -ne "${MOUSE_DESC_LEN}" || "${kb_first}" != "05" ]]; then
  echo "HID report_desc invalid: kb=${kb_len} (want ${KEYBOARD_DESC_LEN}) ms=${ms_len} (want ${MOUSE_DESC_LEN}) first=${kb_first} (want 05)" >&2
  echo "If first is 5c (ASCII '\\'), a shell wrote literal \\xHH text instead of binary." >&2
  exit 2
fi

ln -sfn "${GADGET_ROOT}/functions/hid.keyboard" "${GADGET_ROOT}/configs/c.1/"
ln -sfn "${GADGET_ROOT}/functions/hid.mouse" "${GADGET_ROOT}/configs/c.1/"

echo "${UDC_NAME}" > "${GADGET_ROOT}/UDC"

chmod 0660 /dev/hidg0 /dev/hidg1 2>/dev/null || true
echo "MouseKeyProxy HID gadget bound to ${UDC_NAME} (kb=${kb_len} ms=${ms_len} binary ok)"
