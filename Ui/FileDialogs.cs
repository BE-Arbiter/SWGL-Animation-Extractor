using System.Runtime.InteropServices;

namespace SWGLAnimationExtractor;

/// <summary>Native Windows open/save file dialogs (comdlg32), each run on its own STA thread.</summary>
public static class FileDialogs
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner, hInstance;
        public string lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter, nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir, lpstrTitle;
        public int Flags;
        public short nFileOffset, nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData, lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved, FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW")]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSaveFileNameW")]
    private static extern bool GetSaveFileName(ref OpenFileName ofn);

    private const int OFN_FILEMUSTEXIST = 0x1000, OFN_PATHMUSTEXIST = 0x800, OFN_NOCHANGEDIR = 0x8,
        OFN_OVERWRITEPROMPT = 0x2;

    /// <summary>Shows an Open dialog on a background thread; <paramref name="done"/> gets the chosen path or null.
    /// <paramref name="filter"/> is a display text such as "GLA files (*.gla)"; the pattern is taken from the parentheses.</summary>
    public static void Open(string filter, string title, string? initialPath, Action<string?> done) =>
        Run(() => Show(false, filter, title, initialPath, null, null), done);

    /// <summary>Shows a Save dialog (with overwrite prompt) on a background thread.</summary>
    public static void Save(string filter, string title, string? initialDir, string defaultName, string defExt,
        Action<string?> done) =>
        Run(() => Show(true, filter, title, initialDir, defaultName, defExt), done);

    private static void Run(Func<string?> show, Action<string?> done)
    {
        var t = new Thread(() => done(show()));
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
    }

    private static string BuildFilter(string filter)
    {
        int a = filter.IndexOf('('), b = filter.LastIndexOf(')');
        string pattern = a >= 0 && b > a ? filter.Substring(a + 1, b - a - 1) : "*.*";
        return $"{filter}\0{pattern}\0All files\0*.*\0\0";
    }

    private static string? Show(bool save, string filter, string title, string? initial, string? defaultName, string? defExt)
    {
        const int size = 1024;
        var buffer = Marshal.AllocHGlobal(size * 2);
        try
        {
            string initialDir = "";
            string prefill = defaultName ?? "";
            if (!string.IsNullOrWhiteSpace(initial))
            {
                if (save) { if (Directory.Exists(initial)) initialDir = initial; }
                else if (File.Exists(initial)) { initialDir = Path.GetDirectoryName(initial) ?? ""; prefill = Path.GetFileName(initial); }
                else if (Directory.Exists(initial)) initialDir = initial;
            }
            if (prefill.Length > size - 1) prefill = "";
            for (int i = 0; i < prefill.Length; i++) Marshal.WriteInt16(buffer, i * 2, prefill[i]);
            Marshal.WriteInt16(buffer, prefill.Length * 2, 0);

            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                lpstrFilter = BuildFilter(filter),
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = size,
                lpstrInitialDir = initialDir.Length > 0 ? initialDir : null,
                lpstrTitle = title,
                lpstrDefExt = defExt,
                Flags = OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | (save ? OFN_OVERWRITEPROMPT : OFN_FILEMUSTEXIST),
            };
            bool ok = save ? GetSaveFileName(ref ofn) : GetOpenFileName(ref ofn);
            return ok ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
