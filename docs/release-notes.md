## miu edition v1.5.0

- Compact 940 × 620 logical-pixel layout; sidebar hides on narrow windows.
- Per-monitor V2 DPI support with .NET Framework 4.8, DPI-scaled text and button geometry.
- Simple text-only buttons and reduced vertical spacing; log font reduced to 9 pt.
- Removed the four-button footer; Save log now sits next to Clear log.
- Select logs with the mouse, copy from the context menu or Ctrl+C, select all with Ctrl+A.
- Restoration and credential masking behavior retained.

Extract the full ZIP and keep HermesRestore.exe.config beside the EXE. Windows 10/11 and .NET Framework 4.8 are required. Real composited-window checks were performed at the current 200% display scale; physical multi-monitor DPI transitions still require user validation. No real user data was restored during testing.
