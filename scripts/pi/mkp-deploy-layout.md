# MKP-DEPLOY FAT32 layout

FR-MKP-020 / TR-MKP-HID-007: the appliance image includes a **FAT32** partition
labeled `MKP-DEPLOY` so operators can manage deployed files with Explorer
(no custom Rufus file-staging loops).

## Partition

- Label: `MKP-DEPLOY`
- Filesystem: FAT32
- Mount on Pi: `/mnt/mkp-deploy` (systemd mount unit; see firstrun)
- Config DB remains separate: `/etc/mkp/config.db` (LiteDB)

## Directories (create on first mount if missing)

```text
MKP-DEPLOY/
  media/
    device/     # default CD/floppy images and ISO files on the appliance
    host/       # host-staged media inbox (agent upload or offline copy)
  share/        # folder share + SMB root (sandboxed)
  install/      # FR-MKP-024 client install kit (from Nuke PackClientMsi / StagePiInstallMedia)
                #   Install-MouseKeyProxy.ps1
                #   MouseKeyProxy.Bootstrap.exe (+ deps)
                #   device-bootstrap.json
                #   payloads/service, payloads/agent
                #   autorun.inf, README.txt
```

Stage locally:

```pwsh
.\build.ps1 --target StagePiInstallMedia
# copy output/pi-stage/install/* to Pi /mnt/mkp-deploy/install/

# Or full SD path (PublishPi + client kit + Rufus write):
# Unattended write + eject (default AutoWrite=true). Pin the reader with --RufusDevice N if needed.
.\build.ps1 BuildSdCard --RufusProfile default
# Interactive Rufus GUI only:
.\build.ps1 BuildSdCard --RufusProfile default --AutoWrite false
```

## USB host thumb drive (single LUN)

The live gadget exposes **one** removable drive to the USB host (not CD/floppy LUNs).

- Host folder on Pi: `/mnt/mkp-deploy/share` (`MKP_THUMB_FOLDER`)
- Backing VFAT image: `/var/lib/mousekeyproxy/thumb.img` (`MKP_FS_DISK_IMAGE`)
- Volume label on Windows: `MKP-SHARE`
- Refresh after changing the folder: `sudo bash /usr/local/sbin/mkp-hid-gadget-setup.sh`

Linux configfs cannot bind a directory; the setup script syncs the folder into the
image, then binds that image as `mass_storage.0/lun.0`.

## Operator workflow

1. Mount the SD card (or the USB thumb LUN when the Pi is plugged into a host).
2. Copy install payloads, ISOs, and share seed files into the folders above.
3. Eject cleanly; boot the Pi. Service resolves media under the mount path
   from LiteDB / seed defaults written by Rufus (enable flags + media names only).

## Firstrun responsibilities (scripts, not bulk copy in Rufus C)

- Ensure partition exists / is labeled (image build).
- Mount unit + mkdir tree.
- Write `/etc/mkp/seed.json` from Rufus dialog defaults.
- Install pwsh; set appliance user shell to pwsh.
- Provision HID + single-LUN thumb gadget (`setup-configfs-gadget.sh`).
