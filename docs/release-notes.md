## miu edition v1.5.1

Fix startup flicker caused by post-visibility layout changes in v1.5.0.

- Settle screen fit, sidebar visibility and button sizes before first display rather than in Shown handlers.
- Coalesce runtime resize and DPI layout updates, prevent recursive updates and skip unchanged sizes.
- Buffer layout containers and composite native child controls as a complete window.
- Preserve compact layout, DPI support, log selection/copy and restoration behavior.
- Add test-ui.ps1 and startup/resize regression tests.

Local checks at 200% DPI found 25 child size changes after visibility in v1.5.0 and zero in v1.5.1. Startup/resize regressions, 10 core tests, log-copy checks and actual composited-window button checks at two sizes passed (normal, hover, pressed, disabled and focus; 360 corner samples). No real user backup was restored; physical monitor DPI transitions remain untested.

Extract the complete ZIP and retain HermesRestore.exe.config beside the EXE. Requires Windows 10/11 and .NET Framework 4.8.
