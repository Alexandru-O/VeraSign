# VeraSign / MasterSTI Wallet — Fonts

Drop the following TTF files into this folder. Filenames must match
exactly — referenced by name in `MauiProgram.cs ConfigureFonts(...)` and
`Resources/Styles/Typography.xaml`.

## Required files (v2 design)

| Filename                  | Source                                                  |
|---------------------------|---------------------------------------------------------|
| `Geist-Regular.ttf`       | https://vercel.com/font (or github.com/vercel/geist-font) |
| `Geist-Medium.ttf`        | same                                                    |
| `Geist-SemiBold.ttf`      | same                                                    |
| `Geist-Bold.ttf`          | same                                                    |
| `GeistMono-Regular.ttf`   | same                                                    |
| `GeistMono-Medium.ttf`    | same                                                    |

Until the TTFs are dropped here, MAUI silently falls back to the platform
default sans/mono. UI layout still works — just not pixel-perfect.

Aliases the rest of the app references:

- `Geist`, `GeistMedium`, `GeistSemiBold`, `GeistBold`
- `GeistMono`, `GeistMonoMedium`

The Geist GitHub release ZIP unpacks with these exact filenames.
