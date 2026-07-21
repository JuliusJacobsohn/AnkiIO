namespace AnkiIO.GermanEnglishShowcase;

internal static class Illustrations
{
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ankiio_apple.svg"] = Frame(
            "A red apple",
            "#ef4444",
            """
            <path d="M158 83c-3-18 7-31 24-40" fill="none" stroke="#513426" stroke-width="10" stroke-linecap="round"/>
            <path d="M179 50c19-10 37-4 46 10-18 9-34 8-46-10Z" fill="#32a852"/>
            <path d="M159 79c-43-20-82 17-74 72 7 48 41 73 74 55 33 18 68-7 75-55 8-55-32-92-75-72Z" fill="#ef4444"/>
            <path d="M111 110c10-18 28-24 45-18" fill="none" stroke="#fecaca" stroke-width="9" stroke-linecap="round" opacity=".8"/>
            """),
        ["ankiio_bread_roll.svg"] = Frame(
            "A bread roll",
            "#d97706",
            """
            <path d="M73 151c0-46 38-82 87-82s87 36 87 82c0 30-25 47-87 47s-87-17-87-47Z" fill="#d99a43"/>
            <path d="M86 143c13-35 40-58 74-58 35 0 62 23 75 58" fill="none" stroke="#f8d38d" stroke-width="13" stroke-linecap="round"/>
            <path d="m122 91-17 59M166 82l-8 69M207 94l11 54" stroke="#8a4b19" stroke-width="7" stroke-linecap="round" opacity=".65"/>
            """),
        ["ankiio_coffee.svg"] = Frame(
            "A cup of coffee",
            "#8b5e3c",
            """
            <path d="M117 68c-17-21 17-23 0-44M162 68c-17-21 17-23 0-44M205 68c-17-21 17-23 0-44" fill="none" stroke="#c7d2fe" stroke-width="7" stroke-linecap="round"/>
            <path d="M87 86h134v61c0 36-27 58-67 58s-67-22-67-58V86Z" fill="#fff7ed" stroke="#8b5e3c" stroke-width="8"/>
            <ellipse cx="154" cy="87" rx="66" ry="17" fill="#5b3524"/>
            <path d="M221 105h16c27 0 30 49 2 56h-20" fill="none" stroke="#8b5e3c" stroke-width="12"/>
            """),
        ["ankiio_key.svg"] = Frame(
            "A golden key",
            "#f59e0b",
            """
            <circle cx="108" cy="116" r="42" fill="none" stroke="#f59e0b" stroke-width="18"/>
            <circle cx="108" cy="116" r="12" fill="#f59e0b"/>
            <path d="m141 116 103 0M208 116v28M234 116v20" fill="none" stroke="#f59e0b" stroke-width="18" stroke-linecap="round" stroke-linejoin="round"/>
            <path d="M80 89c15-15 37-19 55-8" fill="none" stroke="#fde68a" stroke-width="7" stroke-linecap="round"/>
            """),
        ["ankiio_door.svg"] = Frame(
            "A blue door",
            "#2563eb",
            """
            <path d="M91 201h138" stroke="#334155" stroke-width="10" stroke-linecap="round"/>
            <path d="M108 38h104v163H108Z" fill="#2563eb" stroke="#1e3a8a" stroke-width="8"/>
            <path d="M125 58h70v53h-70ZM125 127h70v52h-70Z" fill="#60a5fa" stroke="#1d4ed8" stroke-width="5"/>
            <circle cx="187" cy="119" r="8" fill="#fbbf24"/>
            """),
        ["ankiio_bicycle.svg"] = Frame(
            "A bicycle",
            "#10b981",
            """
            <circle cx="87" cy="159" r="43" fill="none" stroke="#334155" stroke-width="8"/>
            <circle cx="233" cy="159" r="43" fill="none" stroke="#334155" stroke-width="8"/>
            <path d="m87 159 51-72 36 72H87Zm51-72h49l46 72m-95-72-14-25m-17 0h34m64 16h32" fill="none" stroke="#10b981" stroke-width="9" stroke-linecap="round" stroke-linejoin="round"/>
            <circle cx="174" cy="159" r="9" fill="#fbbf24"/>
            """),
        ["ankiio_station.svg"] = Frame(
            "A train station",
            "#6366f1",
            """
            <path d="m63 88 97-55 97 55" fill="#818cf8" stroke="#3730a3" stroke-width="8" stroke-linejoin="round"/>
            <path d="M79 87h162v109H79Z" fill="#eef2ff" stroke="#3730a3" stroke-width="8"/>
            <circle cx="160" cy="91" r="23" fill="#fff" stroke="#6366f1" stroke-width="6"/>
            <path d="M160 91V77m0 14 12 7" stroke="#3730a3" stroke-width="5" stroke-linecap="round"/>
            <path d="M105 196v-62h110v62M55 201h210" fill="none" stroke="#3730a3" stroke-width="9" stroke-linecap="round"/>
            """),
        ["ankiio_train.svg"] = Frame(
            "A green train",
            "#059669",
            """
            <path d="M92 55c0-18 15-29 68-29s68 11 68 29v104c0 23-19 42-42 42h-52c-23 0-42-19-42-42V55Z" fill="#10b981" stroke="#065f46" stroke-width="8"/>
            <path d="M108 64h104v55H108Z" fill="#dbeafe" stroke="#065f46" stroke-width="6"/>
            <circle cx="124" cy="154" r="12" fill="#fef3c7"/>
            <circle cx="196" cy="154" r="12" fill="#fef3c7"/>
            <path d="m111 209 20-25m78 25-20-25M75 216h170" stroke="#334155" stroke-width="9" stroke-linecap="round"/>
            """),
        ["ankiio_cafe.svg"] = Frame(
            "A cozy cafe",
            "#c026d3",
            """
            <path d="M70 78h180l-17-43H87L70 78Z" fill="#f0abfc" stroke="#86198f" stroke-width="7"/>
            <path d="M82 78v119h156V78" fill="#fdf4ff" stroke="#86198f" stroke-width="7"/>
            <path d="M101 105h54v47h-54Zm80 0h37v92h-37Z" fill="#c4b5fd" stroke="#86198f" stroke-width="6"/>
            <path d="M52 201h216" stroke="#334155" stroke-width="9" stroke-linecap="round"/>
            <path d="M111 173h46m-23-21v45" stroke="#a16207" stroke-width="7" stroke-linecap="round"/>
            <circle cx="209" cy="151" r="5" fill="#fbbf24"/>
            """),
        ["ankiio_map_pin.svg"] = Frame(
            "A location pin on a map",
            "#e11d48",
            """
            <path d="M43 174c39-31 67 34 111-2 39-31 60 20 123-15" fill="none" stroke="#94a3b8" stroke-width="8" stroke-dasharray="12 13" stroke-linecap="round"/>
            <path d="M160 31c-40 0-68 30-68 67 0 50 68 103 68 103s68-53 68-103c0-37-28-67-68-67Z" fill="#e11d48" stroke="#9f1239" stroke-width="8"/>
            <circle cx="160" cy="98" r="25" fill="#fff1f2"/>
            """),
    };

    private static string Frame(string title, string accent, string artwork) =>
        $"""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 320 230" role="img" aria-label="{title}">
          <title>{title}</title>
          <defs>
            <linearGradient id="background" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0" stop-color="#f8fafc"/>
              <stop offset="1" stop-color="#e2e8f0"/>
            </linearGradient>
          </defs>
          <rect x="4" y="4" width="312" height="222" rx="32" fill="url(#background)" stroke="{accent}" stroke-width="8"/>
          <circle cx="274" cy="49" r="25" fill="{accent}" opacity=".12"/>
          {artwork}
        </svg>
        """;
}
