// 1:1 port of design/prototype/tokens.css.
// Single source of truth for color, typography, radius and shadow values
// shared between MasterSTI.Web (Blazor) and MasterSTI.Wallet (MAUI).
//
// Brand: VeraSign (UI). Codebase: MasterSTI (do not rename).

namespace MasterSTI.Shared.Design;

/// <summary>
/// Strongly-typed v2 design tokens. Light + dark sets exposed as nested classes.
/// </summary>
public static class Tokens
{
    // ---------------------------------------------------------------------
    // Light palette (default)
    // ---------------------------------------------------------------------
    public static class Light
    {
        public const string Bg         = "#FAFAF9";
        public const string BgElev     = "#FFFFFF";
        public const string BgSunken   = "#F4F4F2";
        public const string BgInverse  = "#0A0A0A";

        public const string Border        = "#E5E4E0";
        public const string BorderStrong  = "#D4D2CC";
        public const string BorderSubtle  = "#EFEEEA";

        public const string Fg         = "#0A0A0A";
        public const string FgMuted    = "#525252";
        public const string FgSubtle   = "#8A8A85";
        public const string FgInverse  = "#FAFAF9";

        public const string Accent      = "#1A1A1A";
        public const string AccentFg    = "#FFFFFF";
        public const string AccentHover = "#2A2A2A";

        public const string Success    = "#1F7A52";  public const string SuccessBg  = "#E8F2EC";
        public const string Warning    = "#8B5A0A";  public const string WarningBg  = "#F7F0DD";
        public const string Danger     = "#8B2A2A";  public const string DangerBg   = "#F4E5E5";
        public const string Info       = "#1F4A7A";  public const string InfoBg     = "#E5EBF2";
    }

    // ---------------------------------------------------------------------
    // Dark palette
    // ---------------------------------------------------------------------
    public static class Dark
    {
        public const string Bg         = "#0A0A0A";
        public const string BgElev     = "#141413";
        public const string BgSunken   = "#060605";
        public const string BgInverse  = "#FAFAF9";

        public const string Border        = "#262624";
        public const string BorderStrong  = "#36352F";
        public const string BorderSubtle  = "#1A1A18";

        public const string Fg         = "#FAFAF9";
        public const string FgMuted    = "#A8A89E";
        public const string FgSubtle   = "#6E6E68";
        public const string FgInverse  = "#0A0A0A";

        public const string Accent      = "#FAFAF9";
        public const string AccentFg    = "#0A0A0A";
        public const string AccentHover = "#E5E5E0";

        public const string Success    = "#6BCFA0";  public const string SuccessBg  = "#142A22";
        public const string Warning    = "#E5B260";  public const string WarningBg  = "#2B2114";
        public const string Danger     = "#E58484";  public const string DangerBg   = "#2B1818";
        public const string Info       = "#84B5E5";  public const string InfoBg     = "#182338";
    }

    // ---------------------------------------------------------------------
    // Default light-theme aliases (kept for legacy code that imported flat names)
    // ---------------------------------------------------------------------
    public const string Bg          = Light.Bg;
    public const string BgElev      = Light.BgElev;
    public const string BgSunken    = Light.BgSunken;
    public const string BgInverse   = Light.BgInverse;
    public const string Fg          = Light.Fg;
    public const string FgMuted     = Light.FgMuted;
    public const string FgSubtle    = Light.FgSubtle;
    public const string FgInverse   = Light.FgInverse;
    public const string Border      = Light.Border;
    public const string BorderStrong = Light.BorderStrong;
    public const string BorderSubtle = Light.BorderSubtle;
    public const string Accent      = Light.Accent;
    public const string AccentFg    = Light.AccentFg;
    public const string AccentHover = Light.AccentHover;

    public const string Success    = Light.Success;
    public const string SuccessBg  = Light.SuccessBg;
    public const string Warning    = Light.Warning;
    public const string WarningBg  = Light.WarningBg;
    public const string Danger     = Light.Danger;
    public const string DangerBg   = Light.DangerBg;
    public const string Info       = Light.Info;
    public const string InfoBg     = Light.InfoBg;

    // ---------------------------------------------------------------------
    // Legacy v1 names — retargeted to v2 monochrome ramp so old call-sites
    // compile and produce sensible monochrome output. New code should use
    // semantic aliases above (Bg, Fg, Accent, ...).
    // ---------------------------------------------------------------------
    public const string RoBlue50  = "#F4F4F2";
    public const string RoBlue100 = "#E5E4E0";
    public const string RoBlue200 = "#D4D2CC";
    public const string RoBlue300 = "#8A8A85";
    public const string RoBlue400 = "#525252";
    public const string RoBlue500 = "#1A1A1A";
    public const string RoBlue600 = "#0A0A0A";
    public const string RoBlue700 = "#0A0A0A";
    public const string RoBlue800 = "#060605";
    public const string RoBlue900 = "#060605";

    public const string Gold50  = "#F7F0DD";
    public const string Gold100 = "#F7F0DD";
    public const string Gold300 = "#E5B260";
    public const string Gold400 = "#1A1A1A";
    public const string Gold500 = "#0A0A0A";
    public const string Gold600 = "#0A0A0A";

    public const string N0   = "#FFFFFF";
    public const string N50  = "#FAFAF9";
    public const string N100 = "#F4F4F2";
    public const string N200 = "#EFEEEA";
    public const string N300 = "#E5E4E0";
    public const string N400 = "#8A8A85";
    public const string N500 = "#525252";
    public const string N600 = "#525252";
    public const string N700 = "#0A0A0A";
    public const string N800 = "#0A0A0A";
    public const string N900 = "#0A0A0A";

    public const string Success50  = Light.SuccessBg;
    public const string Success500 = Light.Success;
    public const string Success700 = Light.Success;
    public const string Warn50  = Light.WarningBg;
    public const string Warn500 = Light.Warning;
    public const string Warn700 = Light.Warning;
    public const string Danger50  = Light.DangerBg;
    public const string Danger500 = Light.Danger;
    public const string Danger700 = Light.Danger;
    public const string Info50  = Light.InfoBg;
    public const string Info500 = Light.Info;

    public const string Fg1 = Light.Fg;
    public const string Fg2 = Light.FgMuted;
    public const string Fg3 = Light.FgMuted;
    public const string Fg4 = Light.FgSubtle;

    // ---------------------------------------------------------------------
    // Typography
    // ---------------------------------------------------------------------
    public static class Fonts
    {
        public const string Sans  = "Geist";
        public const string Mono  = "GeistMono";
        public const string Serif = Sans;

        public const string SansStack  = "'Geist', -apple-system, system-ui, 'Segoe UI', sans-serif";
        public const string MonoStack  = "'Geist Mono', ui-monospace, monospace";
        public const string SerifStack = SansStack;
    }

    // ---------------------------------------------------------------------
    // Radii (pixels)
    // ---------------------------------------------------------------------
    public static class Radius
    {
        public const double Xs   = 4;
        public const double Sm   = 4;
        public const double Md   = 8;
        public const double Lg   = 12;
        public const double Xl   = 16;
        public const double Pill = 999;
    }

    // ---------------------------------------------------------------------
    // Shadows
    // ---------------------------------------------------------------------
    public static class Shadow
    {
        public const string Xs    = "0 1px 2px rgba(0,0,0,0.04)";
        public const string Sm    = "0 1px 3px rgba(0,0,0,0.05), 0 1px 2px rgba(0,0,0,0.03)";
        public const string Md    = "0 4px 12px rgba(0,0,0,0.06), 0 2px 4px rgba(0,0,0,0.04)";
        public const string Lg    = "0 12px 32px rgba(0,0,0,0.08), 0 4px 8px rgba(0,0,0,0.05)";
        public const string Focus = "0 0 0 3px rgba(10,10,10,0.10)";
    }

    // ---------------------------------------------------------------------
    // Motion
    // ---------------------------------------------------------------------
    public static class Motion
    {
        public const string Ease   = "cubic-bezier(0.2, 0.8, 0.2, 1)";
        public const int    Fast   = 120;
        public const int    Med    = 200;
        public const int    DurMs  = 200;
    }
}
