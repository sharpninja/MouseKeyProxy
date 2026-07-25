<#
.SYNOPSIS
  Stage Armbian for Orange Pi Zero 2 and launch customized Rufus (rufus-mkp) to write the SD.

.DESCRIPTION
  HDMI enablement and MKP rootfs overlay injection are performed by the custom Rufus code
  (MkpPiHidStageRootFs / Armbian armbianEnv.txt patch), NOT by this script.

  This script only:
  - downloads/caches the Armbian Bookworm minimal image
  - optionally publishes linux-arm64 payloads into MKP_PI_STAGE_DIR
  - launches rufus-mkp with --mkp-pi-profile and the staged image

  Destructive SD write is owned by Rufus. Requires:
  - rufus-mkp checkout (sibling ../rufus-mkp or RUFUS_MKP_ROOT)
  - Network access to dl.armbian.com when downloading

.PARAMETER DiskNumber
  Windows Get-Disk number to overwrite (e.g. 2).

.PARAMETER BootDrive
  Existing drive letter used only to help locate the SD disk (default G).

.PARAMETER ImageUrl
  Armbian image URL (default Bookworm current minimal for orangepizero2).

.PARAMETER StageRoot
  Download/cache directory.

.PARAMETER SshPublicKey
  Authorized key for the mkp user.

.PARAMETER WifiSsid / WifiPsk / WifiCountry
  Optional NetworkManager Wi-Fi credentials staged for firstboot.

.PARAMETER PasswordHash
  Optional shadow password hash for mkp (chpasswd -e). Defaults to the historical lab hash from Pi userconf when present.

.PARAMETER SkipDownload
  Use an already-cached image file.

.PARAMETER SkipWrite
  Only inject overlay into currently mounted Armbian partitions (advanced).

.PARAMETER PublishService
  If set, publish linux-arm64 MouseKeyProxy.Service into the rootfs.
#>
param(
    [int]$DiskNumber = -1,
    [char]$BootDrive = 'G',
    [string]$ImageUrl = 'https://dl.armbian.com/orangepizero2/Bookworm_current_minimal',
    [string]$StageRoot = (Join-Path $env:LOCALAPPDATA 'MouseKeyProxy\pi-stage'),
    [string]$SshPublicKey = 'ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAINyMp7/vTLrN41AwFEkbJGiP95yY1Al/DriZk1BQDW8t mousekeyproxy-pi-hid',
    [string]$WifiSsid = 'BYRD3.1',
    [string]$WifiPsk = '',
    [string]$WifiCountry = 'US',
    [string]$PasswordHash = '$6$8eNJB0bpIgUI2c.5$AZ.O0R6BvE8nXrIommbAUYAqB3mi2C33SeLoZz6osK7uf6PBWMSv3gqjY3KypilkTHpSUMVvFzQp7pHMq/xLe1',
    [string]$Hostname = 'mkp-hid-opi',
    [string]$WslDistro = 'Ubuntu-24.04',
    [switch]$SkipDownload,
    [switch]$SkipWrite,
    [switch]$PublishService
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
New-Item -ItemType Directory -Path $StageRoot -Force | Out-Null

function Write-Step([string]$Message) {
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-TargetDiskNumber {
    param([int]$Requested, [char]$Drive)
    if ($Requested -ge 0) { return $Requested }
    $letter = $Drive.ToString().TrimEnd(':').ToUpperInvariant()
    $part = Get-Partition | Where-Object { $_.DriveLetter -and $_.DriveLetter.ToString().ToUpperInvariant() -eq $letter } | Select-Object -First 1
    if (-not $part) {
        throw "Could not resolve disk from drive ${letter}:. Pass -DiskNumber explicitly."
    }
    $disk = Get-Disk -Number $part.DiskNumber
    if ($disk.IsSystem -or $disk.IsBoot) {
        throw "Refusing to target system/boot disk $($disk.Number)."
    }
    if ($disk.BusType -notin @('USB', 'SD', 'MMC', 'SCSI')) {
        Write-Warning "Disk $($disk.Number) BusType=$($disk.BusType); continuing only because it hosts ${letter}:"
    }
    return [int]$disk.Number
}

function Get-ImagePath {
    param([string]$Root, [string]$Url)
    $name = 'armbian-orangepizero2-bookworm-minimal.img'
    # Preserve extension from redirect path when possible.
    try {
        $leaf = [IO.Path]::GetFileName(([Uri]$Url).AbsolutePath)
        if ($leaf -and $leaf -notmatch 'Bookworm_current_minimal') {
            $name = $leaf
        }
    } catch {}
    return (Join-Path $Root $name)
}

$diskNumber = Resolve-TargetDiskNumber -Requested $DiskNumber -Drive $BootDrive
$disk = Get-Disk -Number $diskNumber
Write-Step "Target disk #$diskNumber model='$($disk.FriendlyName)' size=$([math]::Round($disk.Size/1GB,2))GB BusType=$($disk.BusType)"
if ($disk.IsSystem -or $disk.IsBoot -or $disk.IsOffline) {
    throw "Refusing disk $diskNumber (system/boot/offline)."
}
if ($disk.Size -lt 2GB) {
    throw "Disk $diskNumber is too small ($($disk.Size) bytes)."
}

$imagePath = Get-ImagePath -Root $StageRoot -Url $ImageUrl
$rawDownload = Join-Path $StageRoot 'armbian-orangepizero2-bookworm-minimal.download'

if (-not $SkipDownload) {
    Write-Step "Downloading $ImageUrl"
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $ImageUrl -OutFile $rawDownload -UseBasicParsing
    $bytes = [System.IO.File]::ReadAllBytes($rawDownload)
    $isXz = ($bytes.Length -ge 6 -and $bytes[0] -eq 0xFD -and $bytes[1] -eq 0x37 -and $bytes[2] -eq 0x7A)
    $isGz = ($bytes.Length -ge 2 -and $bytes[0] -eq 0x1F -and $bytes[1] -eq 0x8B)
    if ($isXz) {
        $imagePath = [IO.Path]::ChangeExtension($imagePath, '.img')
        Write-Step "Decompressing xz -> $imagePath"
        # Prefer WSL xz/dd for large files
        $winRaw = ($rawDownload -replace '\\', '/') -replace '^([A-Za-z]):', { '/mnt/' + $args[0].Groups[1].Value.ToLowerInvariant() }
        # Fix path conversion
        $drive = $rawDownload.Substring(0, 1).ToLowerInvariant()
        $rest = $rawDownload.Substring(2) -replace '\\', '/'
        $wslRaw = "/mnt/$drive$rest"
        $driveI = $imagePath.Substring(0, 1).ToLowerInvariant()
        $restI = $imagePath.Substring(2) -replace '\\', '/'
        $wslImg = "/mnt/$driveI$restI"
        wsl -d $WslDistro -u root -- bash -lc "xz -dc '$wslRaw' > '$wslImg'"
        if (-not (Test-Path -LiteralPath $imagePath)) { throw "xz decompress failed: $imagePath missing" }
    } elseif ($isGz) {
        $imagePath = [IO.Path]::ChangeExtension($imagePath, '.img')
        Write-Step "Decompressing gzip -> $imagePath"
        $drive = $rawDownload.Substring(0, 1).ToLowerInvariant()
        $rest = $rawDownload.Substring(2) -replace '\\', '/'
        $wslRaw = "/mnt/$drive$rest"
        $driveI = $imagePath.Substring(0, 1).ToLowerInvariant()
        $restI = $imagePath.Substring(2) -replace '\\', '/'
        $wslImg = "/mnt/$driveI$restI"
        wsl -d $WslDistro -u root -- bash -lc "gzip -dc '$wslRaw' > '$wslImg'"
    } else {
        # Treat as raw .img (or pre-named)
        $imagePath = Join-Path $StageRoot 'armbian-orangepizero2-bookworm-minimal.img'
        Move-Item -LiteralPath $rawDownload -Destination $imagePath -Force
    }
    Write-Step "Image ready: $imagePath ($((Get-Item -LiteralPath $imagePath).Length) bytes)"
} else {
    if (-not (Test-Path -LiteralPath $imagePath)) {
        # try .img next to download cache
        $candidate = Join-Path $StageRoot 'armbian-orangepizero2-bookworm-minimal.img'
        if (Test-Path -LiteralPath $candidate) { $imagePath = $candidate }
        else { throw "No cached image at $imagePath (and SkipDownload set)" }
    }
}

$svcSource = Join-Path $repoRoot 'output\service\linux-arm64'
if ($PublishService) {
    Write-Step 'Publishing MouseKeyProxy.Service for linux-arm64'
    & (Join-Path $PSScriptRoot 'publish-pi-hid.ps1') -Rid linux-arm64 -Service
}

if (-not $SkipWrite) {
    Write-Step "Writing image to PHYSICALDRIVE$diskNumber (ALL DATA WILL BE ERASED)"
    # Unmount volumes so Windows releases handles
    Get-Partition -DiskNumber $diskNumber | Where-Object DriveLetter | ForEach-Object {
        $dl = "$($_.DriveLetter):"
        Write-Host "  Dismounting $dl"
        try { mountvol $dl /P 2>$null } catch {}
        try { Set-Partition -DiskNumber $diskNumber -PartitionNumber $_.PartitionNumber -NewDriveLetter $null -ErrorAction SilentlyContinue } catch {}
    }

    $driveImg = $imagePath.Substring(0, 1).ToLowerInvariant()
    $restImg = $imagePath.Substring(2) -replace '\\', '/'
    $wslImage = "/mnt/$driveImg$restImg"

    # Mount bare disk into WSL, dd, then unmount
    $script = @"
set -euo pipefail
IMG='$wslImage'
echo "Image: `$IMG (`$(stat -c%s "`$IMG") bytes)"
# Prefer wsl --mount from Windows side; here we look for an unused large disk after mount.
lsblk -o NAME,SIZE,MODEL,TYPE
"@
    # Use Windows-side wsl --mount then dd inside
    Write-Step 'Mounting physical disk into WSL (bare)'
    gsudo wsl --unmount "\\.\PHYSICALDRIVE$diskNumber" 2>$null | Out-Null
    gsudo wsl --mount "\\.\PHYSICALDRIVE$diskNumber" --bare
    try {
        wsl -d $WslDistro -u root -- bash -lc @"
set -euo pipefail
IMG='$wslImage'
# Find the newly mounted bare disk: largest disk that is not the WSL VHD root (~1T often) is ambiguous.
# Prefer disks without partitions that match SD size-ish, or the only removable-like node.
mapfile -t CANDS < <(lsblk -dn -o NAME,SIZE,TYPE | awk '`$3=="disk"{print `$1,`$2}')
echo "Candidates:"; lsblk -o NAME,SIZE,TYPE,MOUNTPOINTS
# After --bare mount, WSL exposes the disk as /dev/sdX with no mountpoints.
TARGET=''
for dev in /dev/sd[a-z]; do
  [[ -b `$dev ]] || continue
  # skip if has mounted children used by wsl system
  if lsblk -n -o MOUNTPOINTS `$dev | grep -qE '/|swap'; then
    continue
  fi
  # skip loop
  name=`$(basename `$dev)
  size=`$(lsblk -dn -b -o SIZE `$dev)
  # SD is ~32GB in this lab; accept 4GB..128GB empty disks
  if [[ `$size -ge 4000000000 && `$size -le 140000000000 ]]; then
    # Prefer disk with zero or only non-mounted partitions
    if ! lsblk -n -o MOUNTPOINTS `$dev | grep -q '/'; then
      TARGET=`$dev
    fi
  fi
done
if [[ -z "`$TARGET" ]]; then
  echo 'ERROR: could not identify SD disk inside WSL' >&2
  lsblk -o NAME,SIZE,TYPE,MOUNTPOINTS >&2
  exit 9
fi
echo "Writing to `$TARGET"
dd if="`$IMG" of="`$TARGET" bs=4M status=progress conv=fsync
sync
echo 'Write complete'
"@
    }
    finally {
        gsudo wsl --unmount "\\.\PHYSICALDRIVE$diskNumber" 2>$null | Out-Null
    }

    Write-Step 'Re-scanning disk after image write'
    Start-Sleep -Seconds 3
    gsudo pwsh -NoProfile -Command @"
`$ErrorActionPreference='Continue'
Update-HostStorageCache | Out-Null
Set-Disk -Number $diskNumber -IsOffline `$false -ErrorAction SilentlyContinue
Set-Disk -Number $diskNumber -IsReadOnly `$false -ErrorAction SilentlyContinue
Get-Partition -DiskNumber $diskNumber -ErrorAction SilentlyContinue | ForEach-Object {
  if (-not `$_.DriveLetter) {
    try { Add-PartitionAccessPath -DiskNumber $diskNumber -PartitionNumber `$_.PartitionNumber -AssignDriveLetter -ErrorAction SilentlyContinue } catch {}
  }
}
Get-Partition -DiskNumber $diskNumber | Format-Table PartitionNumber,DriveLetter,Size,Type -AutoSize
"@
}

Write-Step 'Injecting MouseKeyProxy overlay via WSL mount'
# Mount both partitions by number into WSL
gsudo wsl --unmount "\\.\PHYSICALDRIVE$diskNumber" 2>$null | Out-Null
gsudo wsl --mount "\\.\PHYSICALDRIVE$diskNumber" --bare
try {
    $boardEnvWin = Join-Path $repoRoot 'scripts\pi\boards\orange-pi-zero-2.env'
    $gadgetWin = Join-Path $repoRoot 'scripts\pi\setup-configfs-gadget.sh'
    $firstbootWin = Join-Path $repoRoot 'scripts\pi\firstboot-linux-appliance.sh'
    $unitGadgetWin = Join-Path $repoRoot 'assets\systemd\mkp-hid-gadget.service'
    $unitSvcWin = Join-Path $repoRoot 'assets\systemd\mousekeyproxy.service'
    $unitFbWin = Join-Path $repoRoot 'assets\systemd\mkp-firstboot.service'

    function ConvertTo-WslPath([string]$WinPath) {
        $d = $WinPath.Substring(0, 1).ToLowerInvariant()
        $r = $WinPath.Substring(2) -replace '\\', '/'
        return "/mnt/$d$r"
    }

    $wslBoard = ConvertTo-WslPath $boardEnvWin
    $wslGadget = ConvertTo-WslPath $gadgetWin
    $wslFirst = ConvertTo-WslPath $firstbootWin
    $wslUg = ConvertTo-WslPath $unitGadgetWin
    $wslUs = ConvertTo-WslPath $unitSvcWin
    $wslUf = ConvertTo-WslPath $unitFbWin
    $wslSvc = if (Test-Path $svcSource) { ConvertTo-WslPath $svcSource } else { '' }

    $wifiSsidEsc = $WifiSsid.Replace("'", "'\''")
    $wifiPskEsc = $WifiPsk.Replace("'", "'\''")
    $sshEsc = $SshPublicKey.Replace("'", "'\''")
    $hashEsc = $PasswordHash.Replace("'", "'\''")
    $hostEsc = $Hostname.Replace("'", "'\''")

    wsl -d $WslDistro -u root -- bash -lc @"
set -euo pipefail
# Identify SD disk again
TARGET=''
for dev in /dev/sd[a-z]; do
  [[ -b `$dev ]] || continue
  if lsblk -n -o MOUNTPOINTS `$dev | grep -qE '^/$|/mnt/wsl'; then continue; fi
  size=`$(lsblk -dn -b -o SIZE `$dev)
  if [[ `$size -ge 4000000000 && `$size -le 140000000000 ]]; then
    if ! lsblk -n -o MOUNTPOINTS `$dev | grep -q '/'; then TARGET=`$dev; fi
  fi
done
[[ -n "`$TARGET" ]] || { echo 'SD disk not found for overlay'; lsblk; exit 9; }
echo "Overlay target `$TARGET"
BOOT_PART=''
ROOT_PART=''
# Prefer partition 1 FAT boot, partition 2 ext4 root (Armbian layout varies)
while read -r name fstype; do
  dev="/dev/`$name"
  case "`$fstype" in
    vfat|fat|fat32) BOOT_PART=`$dev ;;
    ext4) ROOT_PART=`$dev ;;
  esac
done < <(lsblk -ln -o NAME,FSTYPE `$TARGET | tail -n +2)

# Fallback: p1 boot p2 root
[[ -n "`$BOOT_PART" ]] || BOOT_PART=`${TARGET}1
[[ -n "`$ROOT_PART" ]] || ROOT_PART=`${TARGET}2
# handle /dev/sda1 style vs /dev/mmcblk0p1
if [[ ! -b "`$BOOT_PART" && -b "`${TARGET}p1" ]]; then BOOT_PART=`${TARGET}p1; ROOT_PART=`${TARGET}p2; fi

echo "BOOT=`$BOOT_PART ROOT=`$ROOT_PART"
mkdir -p /mnt/mkp-boot /mnt/mkp-root
mount `$BOOT_PART /mnt/mkp-boot
mount `$ROOT_PART /mnt/mkp-root

install -d -m 0755 /mnt/mkp-root/etc/mousekeyproxy /mnt/mkp-root/usr/local/sbin \
  /mnt/mkp-root/etc/systemd/system /mnt/mkp-root/opt/mousekeyproxy \
  /mnt/mkp-root/var/lib/mousekeyproxy /mnt/mkp-root/var/log/mousekeyproxy

cp -f '$wslBoard' /mnt/mkp-root/etc/mousekeyproxy/board.env
# Force hostname default for this flash
sed -i 's/^MKP_HOSTNAME_DEFAULT=.*/MKP_HOSTNAME_DEFAULT=$hostEsc/' /mnt/mkp-root/etc/mousekeyproxy/board.env || true
printf '%s\n' 'MKP_HOSTNAME=$hostEsc' 'MKP_USER=mkp' 'MKP_BOARD_ID=orange-pi-zero-2' \
  "MKP_PUBLIC_KEY='$sshEsc'" >> /mnt/mkp-root/etc/mousekeyproxy/board.env

printf '%s\n' '$hashEsc' > /mnt/mkp-root/etc/mousekeyproxy/mkp-user.password-hash
chmod 0600 /mnt/mkp-root/etc/mousekeyproxy/mkp-user.password-hash

cp -f '$wslGadget' /mnt/mkp-root/usr/local/sbin/mkp-hid-gadget-setup.sh
cp -f '$wslFirst' /mnt/mkp-root/usr/local/sbin/mkp-firstboot-linux-appliance.sh
chmod 0755 /mnt/mkp-root/usr/local/sbin/mkp-hid-gadget-setup.sh \
  /mnt/mkp-root/usr/local/sbin/mkp-firstboot-linux-appliance.sh

cp -f '$wslUg' /mnt/mkp-root/etc/systemd/system/mkp-hid-gadget.service
cp -f '$wslUs' /mnt/mkp-root/etc/systemd/system/mousekeyproxy.service
cp -f '$wslUf' /mnt/mkp-root/etc/systemd/system/mkp-firstboot.service

# Wi-Fi env on boot partition (readable before root unlock issues)
cat > /mnt/mkp-boot/mkp-wifi.env <<EOF
MKP_WIFI_SSID='$wifiSsidEsc'
MKP_WIFI_PSK='$wifiPskEsc'
MKP_WIFI_COUNTRY='$WifiCountry'
EOF
cp -f /mnt/mkp-boot/mkp-wifi.env /mnt/mkp-root/etc/mousekeyproxy/mkp-wifi.env 2>/dev/null || true

# ---------------------------------------------------------------------------
# HDMI: force display console + 1080p60 for Orange Pi Zero 2 (H616).
# Armbian uses armbianEnv.txt; some vendor images use orangepiEnv.txt.
# Keys: console=both (serial+HDMI), disp_mode=1080p60, verbosity for early boot.
# ---------------------------------------------------------------------------
enable_hdmi_env() {
  local f=`$1`
  [[ -f "`$f" ]] || return 0
  echo "Configuring HDMI in `$f"
  # Remove existing keys we manage, then append desired values.
  sed -i \
    -e '/^console=/d' \
    -e '/^disp_mode=/d' \
    -e '/^display_hdmi=/d' \
    -e '/^hdmi_force_hotplug=/d' \
    -e '/^extraargs=/d' \
    -e '/^verbosity=/d' \
    "`$f" || true
  {
    echo 'verbosity=1'
    echo 'console=both'
    echo 'disp_mode=1080p60'
    echo 'display_hdmi=yes'
    # Keep serial + HDMI; do not blank early.
    echo 'extraargs=consoleblank=0 video=HDMI-A-1:1920x1080@60'
  } >>"`$f"
  echo '--- HDMI env after patch ---'
  cat "`$f" || true
}
enable_hdmi_env /mnt/mkp-boot/armbianEnv.txt
enable_hdmi_env /mnt/mkp-boot/orangepiEnv.txt
# If neither exists yet, create armbianEnv.txt with HDMI defaults.
if [[ ! -f /mnt/mkp-boot/armbianEnv.txt && ! -f /mnt/mkp-boot/orangepiEnv.txt ]]; then
  cat > /mnt/mkp-boot/armbianEnv.txt <<'HDMI_ENV'
verbosity=1
console=both
disp_mode=1080p60
display_hdmi=yes
extraargs=consoleblank=0 video=HDMI-A-1:1920x1080@60
HDMI_ENV
  echo 'Created /boot/armbianEnv.txt with HDMI defaults'
fi
# Mirror into rootfs /boot when it is a separate mount of the same tree is not needed;
# also stage a copy under root /boot if present (some images bind boot here).
if [[ -d /mnt/mkp-root/boot ]]; then
  for f in armbianEnv.txt orangepiEnv.txt; do
    if [[ -f /mnt/mkp-boot/`$f ]]; then
      cp -f /mnt/mkp-boot/`$f /mnt/mkp-root/boot/`$f
    fi
  done
fi
# Ensure getty on tty1 is enabled in the image (HDMI local console / dashboard).
mkdir -p /mnt/mkp-root/etc/systemd/system/getty.target.wants
ln -sfn /lib/systemd/system/getty@.service /mnt/mkp-root/etc/systemd/system/getty.target.wants/getty@tty1.service 2>/dev/null || \
  ln -sfn /usr/lib/systemd/system/getty@.service /mnt/mkp-root/etc/systemd/system/getty.target.wants/getty@tty1.service 2>/dev/null || true


# Enable units with systemctl --root when available
if command -v systemctl >/dev/null 2>&1; then
  systemctl --root=/mnt/mkp-root enable mkp-firstboot.service mkp-hid-gadget.service mousekeyproxy.service 2>/dev/null || true
fi
# Fallback wants/ symlinks
mkdir -p /mnt/mkp-root/etc/systemd/system/multi-user.target.wants
ln -sfn /etc/systemd/system/mkp-firstboot.service /mnt/mkp-root/etc/systemd/system/multi-user.target.wants/mkp-firstboot.service
ln -sfn /etc/systemd/system/mkp-hid-gadget.service /mnt/mkp-root/etc/systemd/system/multi-user.target.wants/mkp-hid-gadget.service
ln -sfn /etc/systemd/system/mousekeyproxy.service /mnt/mkp-root/etc/systemd/system/multi-user.target.wants/mousekeyproxy.service

# Optional service publish tree
if [[ -n '$wslSvc' && -d '$wslSvc' ]]; then
  rsync -a --delete '$wslSvc'/ /mnt/mkp-root/opt/mousekeyproxy/ || cp -a '$wslSvc'/. /mnt/mkp-root/opt/mousekeyproxy/
  chmod +x /mnt/mkp-root/opt/mousekeyproxy/MouseKeyProxy.Service 2>/dev/null || true
fi

# SSH hardened defaults: allow pubkey
if [[ -f /mnt/mkp-root/etc/ssh/sshd_config ]]; then
  sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication yes/' /mnt/mkp-root/etc/ssh/sshd_config || true
  sed -i 's/^#\?PubkeyAuthentication.*/PubkeyAuthentication yes/' /mnt/mkp-root/etc/ssh/sshd_config || true
fi

# Marker on boot partition for humans
cat > /mnt/mkp-boot/mkp-orange-pi-zero-2.txt <<EOF
MouseKeyProxy Orange Pi Zero 2 appliance overlay
board=orange-pi-zero-2
hostname=$hostEsc
user=mkp
hid=/dev/hidg0,/dev/hidg1
hdmi=enabled
disp_mode=1080p60
console=both
dashboard_tty=/dev/tty1
base=armbian-bookworm-minimal
preparedUtc=`$(date -u +%Y-%m-%dT%H:%M:%SZ)
EOF

sync
umount /mnt/mkp-boot || umount -l /mnt/mkp-boot
umount /mnt/mkp-root || umount -l /mnt/mkp-root
echo 'Overlay injection complete'
"@
}
finally {
    gsudo wsl --unmount "\\.\PHYSICALDRIVE$diskNumber" 2>$null | Out-Null
}

Write-Step 'Done. Eject the SD, insert into the Orange Pi Zero 2, power on, then SSH as mkp@$Hostname (or root first-boot recovery).'
Write-Host @"

Next on the board (after network is up):
  sudo systemctl status mkp-firstboot.service mkp-hid-gadget.service mousekeyproxy.service --no-pager
  ls -l /dev/hidg0 /dev/hidg1
  ls /sys/class/udc

If Wi-Fi PSK was empty, set it on the board:
  sudo nmcli dev wifi connect '$WifiSsid' password '<psk>'
"@
