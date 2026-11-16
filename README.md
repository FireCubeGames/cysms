# CYSMS

Android screen mirroring and control for Windows, powered by [scrcpy](https://github.com/Genymobile/scrcpy).

- Simple mode: pick a device, press Connect. CYSMS chooses quality from the connection.
- Advanced mode: every scrcpy option, with profiles and a command preview.
- Wireless set-up, Wi-Fi network names, battery and Android version per device.

## Build

See [BUILD.md](BUILD.md). Short version: `dotnet build -c Release -o build` (needs the .NET 8 SDK; the app targets .NET Framework 4.8, which is part of Windows 10 1903+ and 11).

Every push runs the unit tests and builds a portable folder (see the **Build** workflow's artifacts). Pushing a tag such as `v5.5.0` also creates a release with the portable zip.

## Licence notes

scrcpy and adb are bundled under the Apache License 2.0; see `packaging/THIRD-PARTY-NOTICES.txt`.
