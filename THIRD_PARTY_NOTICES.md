# Third-party components

The project Apache-2.0 license covers project code, not third-party components or trademarks. Preserve the applicable license and notice files when redistributing binaries.

| Component | Version used for this release | Notice files |
|---|---|---|
| Microsoft.Web.WebView2 SDK | 1.0.4258.31 | `third-party/WebView2-LICENSE.txt`, `third-party/WebView2-NOTICE.txt` |
| Microsoft.NETCore.App runtime | 8.0.17 | `third-party/dotnet-runtime-LICENSE.txt`, `third-party/dotnet-runtime-THIRD-PARTY-NOTICES.txt` |
| Microsoft.WindowsDesktop.App runtime (WPF / WinForms) | 8.0.17 | `third-party/windowsdesktop-LICENSE.txt` |
| NSIS installer compiler / generated installer components | 3.12 | `third-party/NSIS-COPYING.txt` |

Notice files are reproduced from the actual packages and compiler distribution used locally. NSIS includes components under different licenses; consult its COPYING file. The compiler itself is not bundled in the application. For dependency updates, review and refresh the notices rather than assuming the project license applies.

The unmodified NSIS 3.12 source distribution and compiler are available from https://sourceforge.net/projects/nsis/files/NSIS%203/3.12/. No NSIS compiler source was modified for this project.

Edge WebView2 Runtime is a separate system prerequisite; its installed version and terms are managed by Microsoft. This release does not bundle Slidepad or its assets. Website favicons are obtained at runtime; no third-party site logos are packaged as project branding. The preview image was generated from the application with demonstration notes.
