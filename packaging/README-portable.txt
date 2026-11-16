CYSMS (portable)
================
Android screen mirroring and control for Windows, powered by scrcpy.

How to run
  1. Unzip this folder anywhere (keep the "scrcpy" folder next to CYSMS.exe).
  2. Double-click CYSMS.exe. A short tour explains the rest.

Notes
  - Windows: Windows 11 (64-bit or ARM) or Windows 10 version 1903 or later (64-bit).
    Not supported: Windows 10 on ARM, 32-bit Windows, Windows 8.1 and 7.
  - Android: 5.0 or later. Sound on the PC needs Android 11+, the camera as a source
    needs Android 12+, a separate virtual display needs Android 10+.
    Help > Compatibility in the app has the full list.
  - The first time, Windows SmartScreen may say "Windows protected your PC" because the
    file is not code-signed. Choose More info > Run anyway. The Microsoft Store version
    does not show this.
  - Your devices and settings are kept in %LOCALAPPDATA%\CYSMS. Deleting the app folder
    does not remove them.
  - CYSMS does not collect or send any data.
  - scrcpy and adb are included under the Apache License 2.0. See THIRD-PARTY-NOTICES.txt.
