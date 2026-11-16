# Building CYSMS 5 (native)

Source layout
- src/            C# (.NET Framework 4.8, Windows Forms): Core.cs (logic, unit-tested), Tools.cs (adb/scrcpy processes),
                  Ui.cs (theme + dialogs), MainForm.cs (window), Program.cs (entry), app.manifest, CYSMS.ico
- tests/          Unit tests for Core.cs (run on any OS: `dotnet run` inside tests/)
- msix/           Store packaging: AppxManifest.template.xml, Assets/ (logos), make-msix.sh
- CYSMS.csproj    Project file

Build the exe (Windows or Linux, .NET 8 SDK)
    dotnet build -c Release -o build
  -> build/CYSMS.exe (+ CYSMS.exe.config). Needs .NET Framework 4.8, which is part of Windows 10 1903+ / 11.

Assemble the app folder (dist/CYSMS)
    CYSMS.exe, CYSMS.exe.config, THIRD-PARTY-NOTICES.txt
    scrcpy/  = contents of the official scrcpy-win64-vX.Y.Z.zip (minus .bat/.vbs helpers) + LICENSE-scrcpy.txt
  Verify the zip against SHA256SUMS.txt on the scrcpy release page.

Build the MSIX (needs makemsix from https://github.com/microsoft/msix-packaging, built with --pack;
on Windows you can use makeappx.exe from the Windows SDK instead)
    MAKEMSIX=/path/to/makemsix ./msix/make-msix.sh "<Identity Name>" "<CN=Publisher>" "<Publisher display name>" 5.0.0.0
  Identity Name and Publisher come from Partner Center > your app > Product identity.
  Bump the version (e.g. 5.0.1.0) for every new Store submission.

Updating scrcpy: replace dist/CYSMS/scrcpy/ with the new release, update the version and SHA-256 in
THIRD-PARTY-NOTICES.txt, rebuild the MSIX with a higher version, submit.
