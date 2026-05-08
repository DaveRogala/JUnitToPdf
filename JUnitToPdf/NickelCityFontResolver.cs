using PdfSharpCore.Fonts;

namespace JUnitToPdf;

sealed class NickelCityFontResolver : IFontResolver
{
    // Maps font face name (file stem) → raw bytes.
    private readonly Dictionary<string, byte[]> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _family;

    // Maps (isBold, isItalic) → preferred face name so selection is deterministic.
    private readonly Dictionary<(bool bold, bool italic), string?> _faceMap = new();

    public NickelCityFontResolver(string fontFolder, string family = "Nickel City")
    {
        _family = family;

        if (!Directory.Exists(fontFolder))
            return;

        foreach (var file in Directory.EnumerateFiles(fontFolder, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            var key = Path.GetFileNameWithoutExtension(file);
            try
            {
                _fonts[key] = File.ReadAllBytes(file);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"warning: could not load font file '{file}': {ex.Message}");
            }
        }

        BuildFaceMap();
    }

    public bool HasFonts => _fonts.Count > 0;

    public string DefaultFontName => _family;

    public byte[] GetFont(string faceName)
    {
        if (_fonts.TryGetValue(faceName, out var bytes))
            return bytes;

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
            return PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);

        var pick = _faceMap.TryGetValue((isBold, isItalic), out var face) ? face : null;
        pick ??= _faceMap.Values.FirstOrDefault(v => v is not null);
        pick ??= _fonts.Keys.FirstOrDefault();

        if (pick is null)
            return PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);

        return new FontResolverInfo(pick);
    }

    // Build a deterministic (bold, italic) → face name map from the loaded font stems.
    private void BuildFaceMap()
    {
        static bool Contains(string key, string token) =>
            key.Contains(token, StringComparison.OrdinalIgnoreCase);

        string? boldFace   = _fonts.Keys.FirstOrDefault(k => Contains(k, "Bold") && !Contains(k, "Italic") && !Contains(k, "SemiLight"));
        string? regularFace = _fonts.Keys.FirstOrDefault(k => Contains(k, "Book") && !Contains(k, "Italic"))
                           ?? _fonts.Keys.FirstOrDefault(k => Contains(k, "Regular"))
                           ?? _fonts.Keys.FirstOrDefault(k => Contains(k, "Medium"))
                           ?? _fonts.Keys.FirstOrDefault(k => Contains(k, "Roman"));
        string? italicFace  = _fonts.Keys.FirstOrDefault(k => Contains(k, "Italic") && !Contains(k, "Bold"));
        string? boldItalicFace = _fonts.Keys.FirstOrDefault(k => Contains(k, "Bold") && Contains(k, "Italic"));

        _faceMap[(false, false)] = regularFace ?? boldFace;
        _faceMap[(true,  false)] = boldFace    ?? regularFace;
        _faceMap[(false, true)]  = italicFace  ?? regularFace ?? boldFace;
        _faceMap[(true,  true)]  = boldItalicFace ?? boldFace ?? regularFace;
    }
}
