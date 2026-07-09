#!/usr/bin/env bash
# MouseKeyProxy USB composite gadget: HID keyboard/mouse + ONE mass-storage LUN
# presented to the host as a removable thumb drive.
#
# Linux configfs mass_storage cannot bind a directory; it needs a block file.
# This script keeps a VFAT image (default /var/lib/mousekeyproxy/thumb.img) and
# syncs a folder (default /mnt/mkp-deploy/share) into it before binding, so the
# host sees that folder as a single USB thumb drive.
#
# IMPORTANT: write report descriptors as binary, never as ASCII "\x05..." text.
# /bin/sh on Raspberry Pi OS is dash, whose printf does NOT interpret \xHH.
# This script uses base64-decoded binary descriptors (safe under bash/dash when
# invoked as `bash setup-configfs-gadget.sh`).
#
# Env:
#   MKP_THUMB_FOLDER     — host-visible folder (default /mnt/mkp-deploy/share)
#   MKP_FS_DISK_IMAGE    — VFAT image path (default /var/lib/mousekeyproxy/thumb.img)
#   MKP_THUMB_SIZE_MB    — image size when creating (default 256)
#   MKP_THUMB_LABEL      — volume label (default MKP-SHARE)
#   MKP_ENABLE_DISK=1    — bind lun.0 (default 1); 0 = HID-only
#   MKP_HID_GADGET_NAME  — configfs gadget name (default mkp_hid)
#   MKP_HID_UDC          — UDC name (default first under /sys/class/udc)
set -euo pipefail

GADGET_NAME="${MKP_HID_GADGET_NAME:-mkp_hid}"
GADGET_ROOT="/sys/kernel/config/usb_gadget/${GADGET_NAME}"
UDC_NAME="${MKP_HID_UDC:-$(ls /sys/class/udc 2>/dev/null | head -n 1)}"

KEYBOARD_DESC_B64='BQEJBqEBBQcZ4CnnFQAlAXUBlQiBApUBdQiBAZUFdQEFCBkBKQWRApUBdQORAZUGdQgVACVlBQcZACllgQDA'
MOUSE_DESC_B64='BQEJAqEBCQGhAAUJGQEpAxUAJQGVA3UBgQKVAXUFgQEFAQkwCTEJOBWBJX91CJUDgQbAwA=='
KEYBOARD_DESC_LEN=63
MOUSE_DESC_LEN=52

THUMB_FOLDER="${MKP_THUMB_FOLDER:-/mnt/mkp-deploy/share}"
DISK_IMAGE="${MKP_FS_DISK_IMAGE:-/var/lib/mousekeyproxy/thumb.img}"
# Default 384MiB so MouseKeyProxy-Client.msi (~100MiB) + headroom fits.
THUMB_SIZE_MB="${MKP_THUMB_SIZE_MB:-384}"
THUMB_LABEL="${MKP_THUMB_LABEL:-MKP-SHARE}"
ENABLE_DISK="${MKP_ENABLE_DISK:-1}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "setup-configfs-gadget.sh must run as root" >&2
  exit 1
fi

if [[ -z "${UDC_NAME}" ]]; then
  echo "no USB device controller found under /sys/class/udc" >&2
  exit 1
fi

modprobe libcomposite
modprobe loop 2>/dev/null || true

# ---------------------------------------------------------------------------
# Ensure VFAT image exists and mirrors THUMB_FOLDER (one-way: folder -> image).
# Host writes go into the image; re-run this script (or a sync unit) to refresh
# from the folder. Kernel mass_storage cannot bind a directory directly.
# ---------------------------------------------------------------------------
prepare_thumb_image() {
  local mnt img_dir
  mkdir -p "${THUMB_FOLDER}"
  img_dir="$(dirname "${DISK_IMAGE}")"
  mkdir -p "${img_dir}"

  # Recreate image if missing or smaller than requested (e.g. grow for MSI).
  need_bytes=$((THUMB_SIZE_MB * 1024 * 1024))
  have_bytes=0
  if [[ -f "${DISK_IMAGE}" ]]; then
    have_bytes=$(stat -c%s "${DISK_IMAGE}" 2>/dev/null || echo 0)
  fi
  if [[ ! -f "${DISK_IMAGE}" || "${have_bytes}" -lt "${need_bytes}" ]]; then
    echo "Creating ${THUMB_SIZE_MB}MiB VFAT thumb image at ${DISK_IMAGE}"
    rm -f "${DISK_IMAGE}"
    truncate -s "${THUMB_SIZE_MB}M" "${DISK_IMAGE}"
    if command -v mkfs.vfat >/dev/null 2>&1; then
      mkfs.vfat -F 32 -n "${THUMB_LABEL}" "${DISK_IMAGE}" >/dev/null
    else
      echo "mkfs.vfat not found; install dosfstools" >&2
      exit 3
    fi
  fi

  # Seed a README if the share folder is empty so the volume is not blank.
  if [[ -z "$(find "${THUMB_FOLDER}" -mindepth 1 -maxdepth 1 2>/dev/null | head -n 1)" ]]; then
    cat > "${THUMB_FOLDER}/README.txt" <<'EOF'
MouseKeyProxy USB share
=======================
Files placed in this folder on the Pi (default /mnt/mkp-deploy/share)
are exposed to the USB host as a single removable drive.

Re-run: sudo /usr/local/sbin/mkp-hid-gadget-setup.sh
(or reboot) after changing folder contents so the image is refreshed.
EOF
  fi

  mnt="$(mktemp -d /tmp/mkp-thumb.XXXXXX)"
  if ! mount -o loop,rw "${DISK_IMAGE}" "${mnt}"; then
    echo "Could not loop-mount ${DISK_IMAGE}" >&2
    rmdir "${mnt}" 2>/dev/null || true
    exit 4
  fi
  # Mirror folder into the image (FAT-safe: no special attrs required).
  # --delete keeps the volume matching the folder; drop if you prefer merge-only.
  if command -v rsync >/dev/null 2>&1; then
    rsync -a --delete \
      --exclude 'System Volume Information' \
      --exclude '\$RECYCLE.BIN' \
      --exclude 'lost+found' \
      "${THUMB_FOLDER}/" "${mnt}/" || true
  else
    find "${mnt}" -mindepth 1 -maxdepth 1 -exec rm -rf {} + 2>/dev/null || true
    cp -a "${THUMB_FOLDER}/." "${mnt}/" 2>/dev/null || true
  fi
  sync
  umount "${mnt}" || umount -l "${mnt}" || true
  rmdir "${mnt}" 2>/dev/null || true
  echo "Thumb image ready: ${DISK_IMAGE} <= ${THUMB_FOLDER}"
}

# Tear down a previous instance so report_desc / LUNs can be rewritten.
if [[ -d "${GADGET_ROOT}" ]]; then
  if [[ -f "${GADGET_ROOT}/UDC" ]]; then
    echo "" > "${GADGET_ROOT}/UDC" || true
  fi
  sleep 0.3
  rm -f "${GADGET_ROOT}/configs/c.1/hid.keyboard" "${GADGET_ROOT}/configs/c.1/hid.mouse" 2>/dev/null || true
  rm -f "${GADGET_ROOT}/configs/c.1/mass_storage.0" 2>/dev/null || true
  if [[ -d "${GADGET_ROOT}/functions/mass_storage.0" ]]; then
    for lun in lun.0 lun.1 lun.2 lun.3; do
      if [[ -d "${GADGET_ROOT}/functions/mass_storage.0/${lun}" ]]; then
        echo "" > "${GADGET_ROOT}/functions/mass_storage.0/${lun}/file" 2>/dev/null || true
      fi
    done
    rmdir "${GADGET_ROOT}/functions/mass_storage.0/lun.1" 2>/dev/null || true
    rmdir "${GADGET_ROOT}/functions/mass_storage.0/lun.2" 2>/dev/null || true
    rmdir "${GADGET_ROOT}/functions/mass_storage.0/lun.3" 2>/dev/null || true
    rmdir "${GADGET_ROOT}/functions/mass_storage.0" 2>/dev/null || true
  fi
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
  SER="$(tr -cd 'A-Za-z0-9' </proc/device-tree/serial-number 2>/dev/null || true)"
else
  SER=""
fi
[[ -n "${SER}" ]] || SER="MKP0001"
printf '%s' "${SER}" > "${GADGET_ROOT}/strings/0x409/serialnumber"
printf '%s' "MouseKeyProxy" > "${GADGET_ROOT}/strings/0x409/manufacturer"
printf '%s' "MouseKeyProxy Pi Appliance" > "${GADGET_ROOT}/strings/0x409/product"

mkdir -p "${GADGET_ROOT}/configs/c.1/strings/0x409"
printf '%s' "HID + Thumb Drive" > "${GADGET_ROOT}/configs/c.1/strings/0x409/configuration"
# Self-powered: Pi has its own PSU.
echo 0xC0 > "${GADGET_ROOT}/configs/c.1/bmAttributes"
echo 2 > "${GADGET_ROOT}/configs/c.1/MaxPower"

# --- HID keyboard ---
mkdir -p "${GADGET_ROOT}/functions/hid.keyboard"
echo 1 > "${GADGET_ROOT}/functions/hid.keyboard/protocol"
echo 1 > "${GADGET_ROOT}/functions/hid.keyboard/subclass"
echo 8 > "${GADGET_ROOT}/functions/hid.keyboard/report_length"
printf '%s' "${KEYBOARD_DESC_B64}" | base64 -d > "${GADGET_ROOT}/functions/hid.keyboard/report_desc"

# --- HID mouse ---
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

# Link HID always
ln -sfn "${GADGET_ROOT}/functions/hid.keyboard" "${GADGET_ROOT}/configs/c.1/"
ln -sfn "${GADGET_ROOT}/functions/hid.mouse" "${GADGET_ROOT}/configs/c.1/"

# --- Single mass_storage LUN (thumb drive) ---
if [[ "${ENABLE_DISK}" == "1" ]]; then
  prepare_thumb_image

  MS="${GADGET_ROOT}/functions/mass_storage.0"
  mkdir -p "${MS}"
  echo 0 > "${MS}/stall" 2>/dev/null || true

  # Only lun.0 — do not create lun.1/lun.2 (those were empty "No Media" drives on Windows).
  mkdir -p "${MS}/lun.0"
  echo 1 > "${MS}/lun.0/removable"
  echo 0 > "${MS}/lun.0/cdrom"
  echo 0 > "${MS}/lun.0/ro"
  echo 0 > "${MS}/lun.0/nofua" 2>/dev/null || true
  # Optional inquiry string helps Explorer label the device
  printf '%s' "MKP Share" > "${MS}/lun.0/inquiry_string" 2>/dev/null || true
  printf '%s' "${DISK_IMAGE}" > "${MS}/lun.0/file"

  ln -sfn "${MS}" "${GADGET_ROOT}/configs/c.1/"
  echo "Bound single thumb LUN: ${DISK_IMAGE} (folder ${THUMB_FOLDER})"
else
  echo "MKP_ENABLE_DISK=0: HID-only gadget (no mass storage)"
fi

echo "${UDC_NAME}" > "${GADGET_ROOT}/UDC"

chmod 0660 /dev/hidg0 /dev/hidg1 2>/dev/null || true
echo "MouseKeyProxy composite gadget bound to ${UDC_NAME} (kb=${kb_len} ms=${ms_len} binary ok; single thumb LUN)"
