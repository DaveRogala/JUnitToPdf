using System.Reflection;
using PdfSharpCore.Fonts;

namespace JUnitToPdf;

sealed class NickelCityFontResolver : IFontResolver
{
    // Maps face key (file stem) → raw font bytes.
    private readonly Dictionary<string, byte[]> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _family;

    // Maps (isBold, isItalic) → face key — built once at construction for determinism.
    private readonly Dictionary<(bool bold, bool italic), string?> _faceMap = new();

    public NickelCityFontResolver(string family = "Nickel City")
    {
        _family = family;
        LoadEmbeddedFonts();
        BuildFaceMap();
    }

    public bool HasFonts => _fonts.Count > 0;

    public string DefaultFontName => _family;

    // Loads all .otf/.ttf files that were embedded with LogicalName=<filename>.<ext>.
    private void LoadEmbeddedFonts()
    {
        var assembly = typeof(NickelCityFontResolver).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                     || n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException($"Resource stream was null for '{resourceName}'.");

                var bytes = new byte[stream.Length];
                _ = stream.Read(bytes, 0, bytes.Length);

                var key = Path.GetFileNameWithoutExtension(resourceName);
                _fonts[key] = bytes;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"warning: could not load embedded font '{resourceName}': {ex.Message}");
            }
        }
    }

    public byte[] GetFont(string faceName)
    {
        // Our embedded custom fonts.
        if (_fonts.TryGetValue(faceName, out var bytes))
            return bytes;

        // Platform font resolver on Linux returns the font file path as the face name.
        // Try to load it directly from disk if it looks like a path.
        if (Path.IsPathRooted(faceName) && File.Exists(faceName))
        {
            try { return File.ReadAllBytes(faceName); }
            catch { /* fall through */ }
        }

        // Last resort: use any loaded custom font so the document still renders.
        if (_fonts.Count > 0)
            return _fonts.Values.First();

        throw new InvalidOperationException(
            $"Font face '{faceName}' could not be resolved and no fallback fonts are loaded.");
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        if (_fonts.Count == 0)
            return PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);

        if (!string.Equals(familyName, _family, StringComparison.OrdinalIgnoreCase))
        {
            // Delegate non-Nickel-City families (e.g. "Helvetica" for bullets) to the
            // platform resolver. If it fails (minimal Linux image, no fontconfig), fall
            // back to our regular face so the document still renders rather than crashing.
            var platformInfo = PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);
            if (platformInfo is not null)
                return platformInfo;

            var fallback = _faceMap.TryGetValue((false, false), out var ff) ? ff
                         : _fonts.Keys.FirstOrDefault();
            if (fallback is not null)
                return new FontResolverInfo(fallback);
        }

        var pick = _faceMap.TryGetValue((isBold, isItalic), out var face) ? face : null;
        pick ??= _faceMap.Values.FirstOrDefault(v => v is not null);
        pick ??= _fonts.Keys.FirstOrDefault();

        if (pick is null)
            return PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);

        return new FontResolverInfo(pick);
    }

    // Builds a deterministic (bold, italic) → face key map.
    // Keys are sorted before matching so the selection is stable across runs.
    private void BuildFaceMap()
    {
        static bool Has(string key, string token) =>
            key.Contains(token, StringComparison.OrdinalIgnoreCase);

        var keys = _fonts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        // Prefer the cleaner "NickelCity-*" naming; fall back to pattern matching.
        // Exclude Condensed variants from regular/bold so headings stay in normal width.
        string? boldFace =
            keys.FirstOrDefault(k => k.Equals("NickelCity-Bold", StringComparison.OrdinalIgnoreCase))
            ?? keys.FirstOrDefault(k => Has(k, "Bold") && !Has(k, "Italic") && !Has(k, "SemiLight") && !Has(k, "Cond"));

        string? regularFace =
            keys.FirstOrDefault(k => k.Equals("NickelCity-Book", StringComparison.OrdinalIgnoreCase))
            ?? keys.FirstOrDefault(k => Has(k, "Book") && !Has(k, "Italic") && !Has(k, "Cond"))
            ?? keys.FirstOrDefault(k => Has(k, "Regular"))
            ?? keys.FirstOrDefault(k => Has(k, "Medium") && !Has(k, "Cond"))
            ?? keys.FirstOrDefault(k => Has(k, "Roman"));

        string? italicFace =
            keys.FirstOrDefault(k => Has(k, "Italic") && !Has(k, "Bold"));

        string? boldItalicFace =
            keys.FirstOrDefault(k => Has(k, "Bold") && Has(k, "Italic"));

        _faceMap[(false, false)] = regularFace    ?? boldFace;
        _faceMap[(true,  false)] = boldFace        ?? regularFace;
        _faceMap[(false, true)]  = italicFace      ?? regularFace ?? boldFace;
        _faceMap[(true,  true)]  = boldItalicFace  ?? boldFace    ?? regularFace;
    }
}
