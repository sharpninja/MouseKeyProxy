#!/usr/bin/env bash
set -euo pipefail

GADGET_NAME="${MKP_HID_GADGET_NAME:-mousekeyproxy}"
GADGET_ROOT="/sys/kernel/config/usb_gadget/${GADGET_NAME}"
UDC_NAME="${MKP_HID_UDC:-$(ls /sys/class/udc | head -n 1)}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "setup-configfs-gadget.sh must run as root" >&2
  exit 1
fi

if [[ -z "${UDC_NAME}" ]]; then
  echo "no USB device controller found under /sys/class/udc" >&2
  exit 1
fi

modprobe libcomposite
mkdir -p "${GADGET_ROOT}"

if [[ -f "${GADGET_ROOT}/UDC" ]]; then
  echo "" > "${GADGET_ROOT}/UDC" || true
fi

echo 0x1d6b > "${GADGET_ROOT}/idVendor"
echo 0x0104 > "${GADGET_ROOT}/idProduct"
echo 0x0100 > "${GADGET_ROOT}/bcdDevice"
echo 0x0200 > "${GADGET_ROOT}/bcdUSB"

mkdir -p "${GADGET_ROOT}/strings/0x409"
echo "MKP0001" > "${GADGET_ROOT}/strings/0x409/serialnumber"
echo "SharpNinja" > "${GADGET_ROOT}/strings/0x409/manufacturer"
echo "MouseKeyProxy Pi HID" > "${GADGET_ROOT}/strings/0x409/product"

mkdir -p "${GADGET_ROOT}/configs/c.1/strings/0x409"
echo "Keyboard and Mouse" > "${GADGET_ROOT}/configs/c.1/strings/0x409/configuration"
echo 250 > "${GADGET_ROOT}/configs/c.1/MaxPower"

mkdir -p "${GADGET_ROOT}/functions/hid.keyboard"
echo 1 > "${GADGET_ROOT}/functions/hid.keyboard/protocol"
echo 1 > "${GADGET_ROOT}/functions/hid.keyboard/subclass"
echo 8 > "${GADGET_ROOT}/functions/hid.keyboard/report_length"
printf '\x05\x01\x09\x06\xa1\x01\x05\x07\x19\xe0\x29\xe7\x15\x00\x25\x01\x75\x01\x95\x08\x81\x02\x95\x01\x75\x08\x81\x01\x95\x06\x75\x08\x15\x00\x25\x65\x05\x07\x19\x00\x29\x65\x81\x00\xc0' > "${GADGET_ROOT}/functions/hid.keyboard/report_desc"

mkdir -p "${GADGET_ROOT}/functions/hid.mouse"
echo 2 > "${GADGET_ROOT}/functions/hid.mouse/protocol"
echo 1 > "${GADGET_ROOT}/functions/hid.mouse/subclass"
echo 4 > "${GADGET_ROOT}/functions/hid.mouse/report_length"
printf '\x05\x01\x09\x02\xa1\x01\x09\x01\xa1\x00\x05\x09\x19\x01\x29\x03\x15\x00\x25\x01\x95\x03\x75\x01\x81\x02\x95\x01\x75\x05\x81\x01\x05\x01\x09\x30\x09\x31\x09\x38\x15\x81\x25\x7f\x75\x08\x95\x03\x81\x06\xc0\xc0' > "${GADGET_ROOT}/functions/hid.mouse/report_desc"

ln -s "${GADGET_ROOT}/functions/hid.keyboard" "${GADGET_ROOT}/configs/c.1/" 2>/dev/null || true
ln -s "${GADGET_ROOT}/functions/hid.mouse" "${GADGET_ROOT}/configs/c.1/" 2>/dev/null || true

echo "${UDC_NAME}" > "${GADGET_ROOT}/UDC"

chmod 0660 /dev/hidg0 /dev/hidg1 2>/dev/null || true
echo "MouseKeyProxy HID gadget bound to ${UDC_NAME}"
