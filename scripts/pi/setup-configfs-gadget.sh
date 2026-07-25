#!/usr/bin/env bash
# MouseKeyProxy USB composite gadget: HID keyboard/mouse + optional mass-storage
# + optional lab USB Ethernet (RNDIS for Windows, ECM for Linux hosts).
#
# Linux configfs mass_storage cannot bind a directory; it needs a block file.
# This script keeps a VFAT image (default /var/lib/mousekeyproxy/thumb.img) and
# syncs a folder (default /mnt/mkp-deploy/share) into it before binding, so the
# host sees that folder as a single USB thumb drive.
#
# IMPORTANT: write report descriptors as binary, never as ASCII "\x05..." text.
# /bin/sh on many Debian images is dash, whose printf does NOT interpret \xHH.
# This script uses base64-decoded binary descriptors (safe under bash/dash when
# invoked as `bash setup-configfs-gadget.sh`).
#
# Board support:
#   Raspberry Pi Zero 2 W: load dwc2 (peripheral) + libcomposite; UDC is often
#   20980000.usb. Enable only dtoverlay=dwc2,dr_mode=peripheral in config.txt.
#   Orange Pi Zero 2 / Zero 2W: libcomposite + SoC UDC (often musb-hdrc.*). Do not
#   require dwc2. Use the board USB-C *data/OTG* port (not power-only) in gadget mode.
#
# Env:
#   MKP_THUMB_FOLDER     - host-visible folder (default /mnt/mkp-deploy/share)
#   MKP_FS_DISK_IMAGE    - VFAT image path (default /var/lib/mousekeyproxy/thumb.img)
#   MKP_THUMB_SIZE_MB    - image size when creating (default 384)
#   MKP_THUMB_LABEL      - volume label (default MKP-SHARE)
#   MKP_ENABLE_DISK=1    - bind lun.0 (default 1); 0 = no mass storage
#   MKP_ENABLE_USB_NET=1 - bind RNDIS+ECM for lab SSH/debug (default 1)
#   MKP_USB_NET_ADDR     - IPv4/CIDR on gadget net iface (default 192.168.7.2/24)
#   MKP_HID_GADGET_NAME  - configfs gadget name (default mkp_hid)
#   MKP_HID_UDC          - UDC name (default first under /sys/class/udc)
#   MKP_BOARD_ID         - optional: raspberry-pi-zero-2 | orange-pi-zero-2 | orange-pi-zero-2w
#
# Lab host (Windows RNDIS or Linux ECM): set a static address on the new adapter,
# e.g. 192.168.7.1/24, then: ssh mkp@192.168.7.2
set -euo pipefail

GADGET_NAME="${MKP_HID_GADGET_NAME:-mkp_hid}"
GADGET_ROOT="/sys/kernel/config/usb_gadget/${GADGET_NAME}"
UDC_NAME="${MKP_HID_UDC:-}"

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
ENABLE_USB_NET="${MKP_ENABLE_USB_NET:-1}"
USB_NET_ADDR="${MKP_USB_NET_ADDR:-192.168.7.2/24}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "setup-configfs-gadget.sh must run as root" >&2
  exit 1
fi

# Mount configfs when the distro did not already.
if ! mountpoint -q /sys/kernel/config 2>/dev/null; then
  mount -t configfs none /sys/kernel/config 2>/dev/null || true
fi

# Best-effort gadget modules for Raspberry Pi (dwc2) and Allwinner (musb/sunxi).
# Failures are ignored: the decisive check is a UDC under /sys/class/udc.
modprobe dwc2 2>/dev/null || true
modprobe musb_hdrc 2>/dev/null || true
modprobe libcomposite
modprobe usb_f_hid 2>/dev/null || true
modprobe usb_f_mass_storage 2>/dev/null || true
modprobe usb_f_rndis 2>/dev/null || true
modprobe usb_f_ecm 2>/dev/null || true
modprobe loop 2>/dev/null || true

if [[ -z "${UDC_NAME}" ]]; then
  UDC_NAME="$(ls /sys/class/udc 2>/dev/null | head -n 1 || true)"
fi

if [[ -z "${UDC_NAME}" ]]; then
  echo "no USB device controller found under /sys/class/udc" >&2
  echo "Hints: Orange Pi Zero 2W needs the USB-C *data/OTG* port in peripheral mode;" >&2
  echo "Raspberry Pi Zero 2 W needs dtoverlay=dwc2,dr_mode=peripheral." >&2
  exit 1
fi

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

# Tear down a previous instance so report_desc / LUNs / net can be rewritten.
if [[ -d "${GADGET_ROOT}" ]]; then
  if [[ -f "${GADGET_ROOT}/UDC" ]]; then
    echo "" > "${GADGET_ROOT}/UDC" || true
  fi
  sleep 0.3
  rm -f "${GADGET_ROOT}/configs/c.1/hid.keyboard" "${GADGET_ROOT}/configs/c.1/hid.mouse" 2>/dev/null || true
  rm -f "${GADGET_ROOT}/configs/c.1/mass_storage.0" 2>/dev/null || true
  rm -f "${GADGET_ROOT}/configs/c.1/rndis.usb0" "${GADGET_ROOT}/configs/c.1/ecm.usb0" 2>/dev/null || true
  rm -f "${GADGET_ROOT}/os_desc/c.1" 2>/dev/null || true
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
  rmdir "${GADGET_ROOT}/functions/rndis.usb0/os_desc/interface.rndis" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/functions/rndis.usb0/os_desc" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/functions/rndis.usb0" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/functions/ecm.usb0" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/configs/c.1/strings/0x409" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/configs/c.1" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/functions/hid.keyboard" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/functions/hid.mouse" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/os_desc" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/strings/0x409" 2>/dev/null || true
  rmdir "${GADGET_ROOT}" 2>/dev/null || true
fi

mkdir -p "${GADGET_ROOT}"

echo 0x1d6b > "${GADGET_ROOT}/idVendor"
# Multifunction composite (HID + storage + net); same class as common g_multi examples.
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
printf '%s' "MouseKeyProxy HID+Net Appliance" > "${GADGET_ROOT}/strings/0x409/product"

mkdir -p "${GADGET_ROOT}/configs/c.1/strings/0x409"
printf '%s' "HID + Thumb + USB Ethernet" > "${GADGET_ROOT}/configs/c.1/strings/0x409/configuration"
# Self-powered: board has its own PSU on the power Type-C.
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
  echo "MKP_ENABLE_DISK=0: no mass storage LUN"
fi

# --- Lab USB Ethernet: RNDIS (Windows) + ECM (Linux) ---
# Windows needs Microsoft OS descriptors on the RNDIS function. Host should use
# static 192.168.7.1/24; this board uses MKP_USB_NET_ADDR (default 192.168.7.2/24).
HAVE_RNDIS=0
HAVE_ECM=0
if [[ "${ENABLE_USB_NET}" == "1" ]]; then
  if [[ -d /sys/class/udc ]] && modprobe usb_f_rndis 2>/dev/null; then
    :
  fi
  if mkdir -p "${GADGET_ROOT}/functions/rndis.usb0" 2>/dev/null; then
    # Locally administered MACs (fixed for lab predictability).
    printf '%s' "02:22:33:44:55:66" > "${GADGET_ROOT}/functions/rndis.usb0/dev_addr" 2>/dev/null || true
    printf '%s' "02:22:33:44:55:67" > "${GADGET_ROOT}/functions/rndis.usb0/host_addr" 2>/dev/null || true

    # Microsoft OS descriptors so Windows loads RNDIS automatically.
    mkdir -p "${GADGET_ROOT}/os_desc"
    echo 1 > "${GADGET_ROOT}/os_desc/use" 2>/dev/null || true
    echo 0xcd > "${GADGET_ROOT}/os_desc/b_vendor_code" 2>/dev/null || true
    printf '%s' "MSFT100" > "${GADGET_ROOT}/os_desc/qw_sign" 2>/dev/null || true
    mkdir -p "${GADGET_ROOT}/functions/rndis.usb0/os_desc/interface.rndis" 2>/dev/null || true
    printf '%s' "RNDIS" > "${GADGET_ROOT}/functions/rndis.usb0/os_desc/interface.rndis/compatible_id" 2>/dev/null || true
    printf '%s' "5162001" > "${GADGET_ROOT}/functions/rndis.usb0/os_desc/interface.rndis/sub_compatible_id" 2>/dev/null || true
    ln -sfn "${GADGET_ROOT}/configs/c.1" "${GADGET_ROOT}/os_desc/" 2>/dev/null || true

    ln -sfn "${GADGET_ROOT}/functions/rndis.usb0" "${GADGET_ROOT}/configs/c.1/"
    HAVE_RNDIS=1
    echo "Bound RNDIS (Windows lab USB net)"
  else
    echo "WARNING: usb_f_rndis / rndis.usb0 not available; Windows USB net may not work" >&2
  fi

  if mkdir -p "${GADGET_ROOT}/functions/ecm.usb0" 2>/dev/null; then
    printf '%s' "02:22:33:44:55:68" > "${GADGET_ROOT}/functions/ecm.usb0/dev_addr" 2>/dev/null || true
    printf '%s' "02:22:33:44:55:69" > "${GADGET_ROOT}/functions/ecm.usb0/host_addr" 2>/dev/null || true
    ln -sfn "${GADGET_ROOT}/functions/ecm.usb0" "${GADGET_ROOT}/configs/c.1/"
    HAVE_ECM=1
    echo "Bound ECM (Linux host USB net)"
  else
    echo "WARNING: usb_f_ecm / ecm.usb0 not available" >&2
  fi

  if [[ "${HAVE_RNDIS}" -eq 0 && "${HAVE_ECM}" -eq 0 ]]; then
    echo "WARNING: MKP_ENABLE_USB_NET=1 but neither RNDIS nor ECM could be created" >&2
  fi
else
  echo "MKP_ENABLE_USB_NET=0: no USB Ethernet (HID/storage only)"
fi

# ---------------------------------------------------------------------------
# UDC bind with progressive fallback.
# Orange Pi musb (and some dwc2 composites) often fail RNDIS with err -19 /
# "Device or resource busy". HID + mass_storage still works; drop net and retry.
# ---------------------------------------------------------------------------
bind_gadget_to_udc() {
  if echo "${UDC_NAME}" > "${GADGET_ROOT}/UDC" 2>/tmp/mkp-udc-bind.err; then
    return 0
  fi
  return 1
}

clear_udc_slot() {
  if [[ -f "${GADGET_ROOT}/UDC" ]]; then
    echo "" > "${GADGET_ROOT}/UDC" 2>/dev/null || true
  fi
  sleep 0.3
}

strip_rndis() {
  rm -f "${GADGET_ROOT}/configs/c.1/rndis.usb0" 2>/dev/null || true
  rm -f "${GADGET_ROOT}/os_desc/c.1" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/functions/rndis.usb0/os_desc/interface.rndis" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/functions/rndis.usb0/os_desc" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/functions/rndis.usb0" 2>/dev/null || true
  HAVE_RNDIS=0
}

strip_ecm() {
  rm -f "${GADGET_ROOT}/configs/c.1/ecm.usb0" 2>/dev/null || true
  rmdir "${GADGET_ROOT}/functions/ecm.usb0" 2>/dev/null || true
  HAVE_ECM=0
}

udc_is_bound() {
  local cur
  cur="$(tr -d ' \n\r' < "${GADGET_ROOT}/UDC" 2>/dev/null || true)"
  [[ -n "${cur}" ]]
}

if ! bind_gadget_to_udc; then
  err="$(tr -d '\r' </tmp/mkp-udc-bind.err 2>/dev/null || true)"
  echo "WARNING: full composite UDC bind failed on ${UDC_NAME} (${err:-unknown}); falling back" >&2

  if [[ "${HAVE_RNDIS}" -eq 1 ]]; then
    echo "WARNING: dropping RNDIS and retrying UDC bind (common musb -19/EBUSY with rndis)" >&2
    clear_udc_slot
    strip_rndis
    if bind_gadget_to_udc; then
      echo "WARNING: bound without RNDIS (HID+storage and/or ECM retained)" >&2
    fi
  fi
fi

if ! udc_is_bound; then
  if [[ "${HAVE_RNDIS}" -eq 1 || "${HAVE_ECM}" -eq 1 ]]; then
    echo "WARNING: dropping remaining USB net; falling back to HID+storage only" >&2
    clear_udc_slot
    [[ "${HAVE_RNDIS}" -eq 1 ]] && strip_rndis
    [[ "${HAVE_ECM}" -eq 1 ]] && strip_ecm
    if ! bind_gadget_to_udc; then
      err="$(tr -d '\r' </tmp/mkp-udc-bind.err 2>/dev/null || true)"
      echo "ERROR: UDC bind failed for HID+storage on ${UDC_NAME} (${err:-unknown})" >&2
      exit 5
    fi
    echo "WARNING: bound HID+storage only after USB net UDC bind failure" >&2
  else
    err="$(tr -d '\r' </tmp/mkp-udc-bind.err 2>/dev/null || true)"
    echo "ERROR: UDC bind failed on ${UDC_NAME} (${err:-unknown})" >&2
    exit 5
  fi
fi

chmod 0660 /dev/hidg0 /dev/hidg1 2>/dev/null || true

# Bring up gadget net interfaces with a static lab address for SSH debug.
configure_usb_net_iface() {
  local name="$1"
  ip link set "${name}" up 2>/dev/null || return 1
  ip addr flush dev "${name}" 2>/dev/null || true
  if ip addr add "${USB_NET_ADDR}" dev "${name}" 2>/dev/null; then
    echo "USB net ${name} address ${USB_NET_ADDR} (set host to .1/24 on the RNDIS/ECM adapter)"
    return 0
  fi
  # Already configured is fine.
  if ip -4 addr show dev "${name}" 2>/dev/null | grep -q "${USB_NET_ADDR%%/*}"; then
    echo "USB net ${name} already has ${USB_NET_ADDR}"
    return 0
  fi
  return 1
}

if [[ "${ENABLE_USB_NET}" == "1" && ( "${HAVE_RNDIS}" -eq 1 || "${HAVE_ECM}" -eq 1 ) ]]; then
  sleep 0.5
  configured=0
  for _try in $(seq 1 40); do
    for name in usb0 usb1; do
      if [[ -d "/sys/class/net/${name}" ]]; then
        if configure_usb_net_iface "${name}"; then
          configured=1
        fi
      fi
    done
    # Some kernels name ECM as enx + host MAC without colons.
    for path in /sys/class/net/enx*; do
      [[ -e "${path}" ]] || continue
      name="$(basename "${path}")"
      if configure_usb_net_iface "${name}"; then
        configured=1
      fi
    done
    if [[ "${configured}" -eq 1 ]]; then
      break
    fi
    sleep 0.25
  done
  if [[ "${configured}" -eq 0 ]]; then
    echo "WARNING: gadget bound but no usb0/usb1/enx* yet; wait for host RNDIS install, then: ip addr add ${USB_NET_ADDR} dev usb0" >&2
  fi
fi

echo "MouseKeyProxy composite gadget bound to ${UDC_NAME} (kb=${kb_len} ms=${ms_len}; disk=${ENABLE_DISK}; rndis=${HAVE_RNDIS}; ecm=${HAVE_ECM})"
