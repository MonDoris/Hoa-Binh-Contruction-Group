// File: Infrastructure/Documents/WordStyleSheet.cs
using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace HbELearning.Infrastructure.Documents;

/// <summary>
/// Định dạng CHỮ của Word sau khi đã gộp đủ các tầng. Mọi thuộc tính đều để null được, và null mang nghĩa
/// "tầng này không nói gì" chứ không phải "tắt" — nhờ vậy việc gộp tầng mới đúng: chỉ thứ thật sự được khai
/// báo mới đè lên tầng dưới. Nếu dùng bool thường thì một style đặt in đậm sẽ bị chính đoạn văn không nhắc
/// gì đến in đậm xoá mất.
/// </summary>
public sealed class WordRunFormat
{
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Strike { get; set; }
    public bool? AllCaps { get; set; }

    /// <summary>Kiểu gạch chân theo cách gọi của Word: single, double, dotted, dash, wave... "none" là tắt.</summary>
    public string? Underline { get; set; }

    /// <summary>Mã màu 6 ký tự hệ 16, không kèm dấu thăng.</summary>
    public string? Color { get; set; }
    public string? Background { get; set; }

    public double? SizePt { get; set; }
    public string? FontName { get; set; }

    /// <summary>superscript | subscript | baseline</summary>
    public string? VerticalAlign { get; set; }

    public WordRunFormat Clone() => (WordRunFormat)MemberwiseClone();

    /// <summary>Trả về bản sao của chính nó sau khi cho <paramref name="child"/> đè lên.</summary>
    public WordRunFormat Override(WordRunFormat? child)
    {
        if (child is null) return this;

        var result = Clone();
        if (child.Bold is not null) result.Bold = child.Bold;
        if (child.Italic is not null) result.Italic = child.Italic;
        if (child.Strike is not null) result.Strike = child.Strike;
        if (child.AllCaps is not null) result.AllCaps = child.AllCaps;
        if (child.Underline is not null) result.Underline = child.Underline;
        if (child.Color is not null) result.Color = child.Color;
        if (child.Background is not null) result.Background = child.Background;
        if (child.SizePt is not null) result.SizePt = child.SizePt;
        if (child.FontName is not null) result.FontName = child.FontName;
        if (child.VerticalAlign is not null) result.VerticalAlign = child.VerticalAlign;
        return result;
    }
}

/// <summary>Định dạng ĐOẠN VĂN của Word sau khi gộp tầng. Cùng quy ước null như <see cref="WordRunFormat"/>.</summary>
public sealed class WordParagraphFormat
{
    public string? Alignment { get; set; }
    public double? IndentLeftPt { get; set; }
    public double? IndentRightPt { get; set; }

    /// <summary>Thụt dòng đầu; số âm nghĩa là kiểu "treo" (dòng đầu nhô ra ngoài lề).</summary>
    public double? FirstLineIndentPt { get; set; }

    public double? SpaceBeforePt { get; set; }
    public double? SpaceAfterPt { get; set; }

    /// <summary>Giá trị đưa thẳng vào line-height: một bội số ("1.5") hoặc một độ dài ("14pt").</summary>
    public string? LineHeight { get; set; }

    public string? Background { get; set; }
    public string? BorderCss { get; set; }
    public int? OutlineLevel { get; set; }
    public int? NumberingId { get; set; }
    public int? NumberingLevel { get; set; }

    /// <summary>Bật thì Word bỏ giãn cách giữa các đoạn CÙNG một style — thực tế là giữa các mục của cùng
    /// một danh sách. Bỏ qua cờ này thì danh sách nhập vào rời rạc hẳn so với bản gốc.</summary>
    public bool? ContextualSpacing { get; set; }

    public WordParagraphFormat Clone() => (WordParagraphFormat)MemberwiseClone();

    public WordParagraphFormat Override(WordParagraphFormat? child)
    {
        if (child is null) return this;

        var result = Clone();
        if (child.Alignment is not null) result.Alignment = child.Alignment;
        if (child.IndentLeftPt is not null) result.IndentLeftPt = child.IndentLeftPt;
        if (child.IndentRightPt is not null) result.IndentRightPt = child.IndentRightPt;
        if (child.FirstLineIndentPt is not null) result.FirstLineIndentPt = child.FirstLineIndentPt;
        if (child.SpaceBeforePt is not null) result.SpaceBeforePt = child.SpaceBeforePt;
        if (child.SpaceAfterPt is not null) result.SpaceAfterPt = child.SpaceAfterPt;
        if (child.LineHeight is not null) result.LineHeight = child.LineHeight;
        if (child.Background is not null) result.Background = child.Background;
        if (child.BorderCss is not null) result.BorderCss = child.BorderCss;
        if (child.OutlineLevel is not null) result.OutlineLevel = child.OutlineLevel;
        if (child.NumberingId is not null) result.NumberingId = child.NumberingId;
        if (child.NumberingLevel is not null) result.NumberingLevel = child.NumberingLevel;
        if (child.ContextualSpacing is not null) result.ContextualSpacing = child.ContextualSpacing;
        return result;
    }
}

/// <summary>
/// Đọc thuộc tính từ cây XML của Word. Cố ý tra theo TÊN CỤC BỘ của phần tử/thuộc tính thay vì dùng lớp
/// sinh sẵn của OpenXml SDK ở những chỗ tên lớp hay đổi giữa các phiên bản SDK (đường viền, thụt lề, giãn
/// dòng) — tra theo tên thì file .docx viết ra sao đọc y như vậy, không phụ thuộc phiên bản thư viện.
/// </summary>
public static class WordValue
{
    public const double TwipPerPoint = 20;
    public const double EmuPerPixel = 9525;

    /// <summary>Giá trị thuộc tính đầu tiên tìm thấy theo danh sách tên. Word có nhiều cặp tên đồng nghĩa
    /// (left/start, right/end) tuỳ bản sinh ra file, nên phải thử lần lượt.</summary>
    public static string? Attr(OpenXmlElement? element, params string[] localNameList)
    {
        if (element is null) return null;

        foreach (var name in localNameList)
            foreach (var attribute in element.GetAttributes())
                if (attribute.LocalName == name && !string.IsNullOrEmpty(attribute.Value))
                    return attribute.Value;

        return null;
    }

    public static OpenXmlElement? Child(OpenXmlElement? element, string localName) =>
        element?.Elements().FirstOrDefault(x => x.LocalName == localName);

    public static double? Number(string? value) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    public static int? Integer(string? value) =>
        int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    /// <summary>Twip (1/20 point) sang point — đơn vị đo của gần như mọi khoảng cách trong Word.</summary>
    public static double? PointFromTwip(string? value) => Number(value) is double twip ? twip / TwipPerPoint : null;

    /// <summary>Cờ bật/tắt của Word: có thẻ mà không ghi val thì mặc định là BẬT; "0"/"false"/"off" là tắt.</summary>
    public static bool? OnOff(OpenXmlElement? element)
    {
        if (element is null) return null;

        var value = Attr(element, "val");
        if (value is null) return true;
        return value is not ("0" or "false" or "off");
    }

    /// <summary>"auto" và chuỗi rỗng nghĩa là "để hệ thống tự quyết", không phải một màu thật.</summary>
    public static bool IsRealColor(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Equals("auto", StringComparison.OrdinalIgnoreCase)
        && value.Length is 3 or 6
        && value.All(Uri.IsHexDigit);

    /// <summary>Màu bôi nền chữ trong Word ghi bằng TÊN chứ không phải mã màu, phải tự đổi sang mã.</summary>
    public static string? ColorFromHighlightName(string? name) => name?.ToLowerInvariant() switch
    {
        "black" => "000000",
        "blue" => "0000FF",
        "cyan" => "00FFFF",
        "darkblue" => "000080",
        "darkcyan" => "008080",
        "darkgray" => "808080",
        "darkgreen" => "008000",
        "darkmagenta" => "800080",
        "darkred" => "800000",
        "darkyellow" => "808000",
        "green" => "00FF00",
        "lightgray" => "C0C0C0",
        "magenta" => "FF00FF",
        "red" => "FF0000",
        "white" => "FFFFFF",
        "yellow" => "FFFF00",
        _ => null
    };

    /// <summary>Một cạnh viền của Word sang CSS. Độ dày Word ghi bằng 1/8 point; quy về pixel và tối thiểu
    /// 1px vì viền mỏng hơn một pixel thì trình duyệt làm tròn thành mất hẳn.</summary>
    public static string? BorderCss(OpenXmlElement? borderElement)
    {
        if (borderElement is null) return null;

        var style = Attr(borderElement, "val") ?? "single";
        var cssStyle = style.ToLowerInvariant() switch
        {
            "nil" or "none" => null,
            "double" or "doublewave" or "triple" => "double",
            "dashed" or "dashsmallgap" or "dashdotstroked" => "dashed",
            "dotted" or "dotdash" or "dotdotdash" => "dotted",
            _ => "solid"
        };
        if (cssStyle is null) return null;

        var eighthPoint = Number(Attr(borderElement, "sz")) ?? 4;
        var pixel = Math.Max(1, Math.Round(eighthPoint / 8 * 96 / 72));

        var color = Attr(borderElement, "color");
        var cssColor = IsRealColor(color) ? "#" + color : "currentColor";

        return pixel.ToString("0", CultureInfo.InvariantCulture) + "px " + cssStyle + " " + cssColor;
    }

    /// <summary>Gộp 4 cạnh của một khối viền (pBdr / tcBorders / tblBorders) thành các khai báo CSS.</summary>
    public static string? BoxBorderCss(OpenXmlElement? bordersElement)
    {
        if (bordersElement is null) return null;

        var partList = new List<string>();
        AppendEdge("top", "top");
        AppendEdge("left", "left", "start");
        AppendEdge("bottom", "bottom");
        AppendEdge("right", "right", "end");

        return partList.Count > 0 ? string.Join(';', partList) : null;

        void AppendEdge(string cssSide, params string[] wordNameList)
        {
            var edge = bordersElement.Elements().FirstOrDefault(x => wordNameList.Contains(x.LocalName));
            if (BorderCss(edge) is string css) partList.Add("border-" + cssSide + ":" + css);
        }
    }

    public static string Pt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "pt";
}

/// <summary>
/// Bảng style của tài liệu (styles.xml) đã được làm phẳng sẵn để tra nhanh.
///
/// Đây là thứ quyết định file Word nhập vào có giống bản gốc hay không: trong tài liệu thật, phần lớn định
/// dạng KHÔNG nằm ở từng chữ mà nằm ở style — người soạn đặt "Heading 2" màu xanh cỡ 14 một lần rồi dùng
/// lại khắp nơi. Chỉ đọc định dạng gán trực tiếp lên từng đoạn chữ thì tài liệu nhập vào sẽ ra một khối chữ
/// đen trơn dù bản gốc nhiều màu.
/// </summary>
public sealed class WordStyleSheet
{
    /// <summary>Chặn vòng lặp basedOn trỏ lòng vòng — file .docx hỏng hoặc do công cụ khác sinh ra vẫn gặp.</summary>
    private const int MaxStyleDepth = 12;

    private readonly Dictionary<string, Style> _styleById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WordRunFormat> _runCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WordParagraphFormat> _paragraphCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Định dạng mặc định của cả tài liệu (docDefaults) — nền của mọi tầng gộp phía trên.</summary>
    public WordRunFormat DefaultRun { get; private set; } = new();
    public WordParagraphFormat DefaultParagraph { get; private set; } = new();

    public static WordStyleSheet Load(MainDocumentPart mainPart)
    {
        var sheet = new WordStyleSheet();

        var styles = mainPart.StyleDefinitionsPart?.Styles;
        if (styles is null) return sheet;

        var docDefaults = WordValue.Child(styles, "docDefaults");
        sheet.DefaultRun = ReadRunFormat(WordValue.Child(WordValue.Child(docDefaults, "rPrDefault"), "rPr"));
        sheet.DefaultParagraph = ReadParagraphFormat(WordValue.Child(WordValue.Child(docDefaults, "pPrDefault"), "pPr"));

        foreach (var style in styles.Elements<Style>())
        {
            var id = style.StyleId?.Value;
            if (!string.IsNullOrEmpty(id)) sheet._styleById[id] = style;
        }

        return sheet;
    }

    // ==================================================================
    //  TRA STYLE
    // ==================================================================

    /// <summary>Style và toàn bộ tổ tiên basedOn của nó, xếp từ style cụ thể nhất đi ngược lên gốc.</summary>
    public IEnumerable<Style> ChainOf(string? styleId)
    {
        var current = styleId;

        for (var depth = 0; depth < MaxStyleDepth; depth++)
        {
            if (string.IsNullOrEmpty(current) || !_styleById.TryGetValue(current, out var style)) yield break;

            yield return style;
            current = style.BasedOn?.Val?.Value;
        }
    }

    /// <summary>Định dạng chữ do một style quy định, đã gộp cả chuỗi basedOn (gốc trước, cụ thể sau).</summary>
    public WordRunFormat RunFormatOfStyle(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId)) return new WordRunFormat();
        if (_runCache.TryGetValue(styleId, out var cached)) return cached;

        var result = new WordRunFormat();
        // Đảo chuỗi lại: style gốc phải áp trước để style cụ thể còn đè lên được.
        foreach (var style in ChainOf(styleId).Reverse())
            result = result.Override(ReadRunFormat(WordValue.Child(style, "rPr")));

        _runCache[styleId] = result;
        return result;
    }

    public WordParagraphFormat ParagraphFormatOfStyle(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId)) return new WordParagraphFormat();
        if (_paragraphCache.TryGetValue(styleId, out var cached)) return cached;

        var result = new WordParagraphFormat();
        foreach (var style in ChainOf(styleId).Reverse())
            result = result.Override(ReadParagraphFormat(WordValue.Child(style, "pPr")));

        _paragraphCache[styleId] = result;
        return result;
    }

    /// <summary>Một phần tử con của w:tblPr trong định nghĩa style bảng (đường viền, lề trong ô...), lấy ở
    /// style cụ thể nhất có khai báo nó. Nhờ vậy bảng dùng style dựng sẵn của Word ("Table Grid") vẫn có
    /// đường kẻ, dù trong file không hề ghi đường kẻ ở từng bảng.</summary>
    public OpenXmlElement? TableStyleChildOf(string? styleId, string localName)
    {
        foreach (var style in ChainOf(styleId))
        {
            var found = WordValue.Child(WordValue.Child(style, "tblPr"), localName);
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>Cấp tiêu đề 1..9 của đoạn văn, null nếu không phải tiêu đề.
    ///
    /// Tra theo cả mã style lẫn TÊN style vì bản Word ngôn ngữ khác đặt mã style riêng nhưng tên chuẩn hoá
    /// bên trong vẫn là "heading 1". Không tra được nữa thì lùi về outlineLvl — thứ Word ghi cho mọi đoạn
    /// nằm trong dàn ý, kể cả khi người soạn tự đặt style riêng.</summary>
    public int? HeadingLevelOf(Paragraph paragraph)
    {
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

        foreach (var style in ChainOf(styleId))
        {
            if (LevelFromStyleText(style.StyleId?.Value) is int fromId) return fromId;
            if (LevelFromStyleText(style.StyleName?.Val?.Value) is int fromName) return fromName;
        }

        var outline = WordValue.Integer(
            WordValue.Attr(WordValue.Child(paragraph.ParagraphProperties, "outlineLvl"), "val"));
        if (outline is >= 0 and <= 8) return outline + 1;

        return null;

        static int? LevelFromStyleText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var compact = text.Replace(" ", string.Empty);
            if (compact.Equals("Title", StringComparison.OrdinalIgnoreCase)) return 1;

            if (!compact.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)) return null;
            return int.TryParse(compact[7..], out var level) && level is >= 1 and <= 9 ? level : null;
        }
    }

    // ==================================================================
    //  ĐỌC ĐỊNH DẠNG TỪ XML
    // ==================================================================

    public static WordRunFormat ReadRunFormat(OpenXmlElement? runProperties)
    {
        var format = new WordRunFormat();
        if (runProperties is null) return format;

        format.Bold = WordValue.OnOff(WordValue.Child(runProperties, "b"));
        format.Italic = WordValue.OnOff(WordValue.Child(runProperties, "i"));
        format.AllCaps = WordValue.OnOff(WordValue.Child(runProperties, "caps"));

        // Gạch ngang một nét và hai nét đều hiện thành line-through, khác biệt không đáng để dựng riêng.
        format.Strike = WordValue.OnOff(WordValue.Child(runProperties, "strike"))
                        ?? WordValue.OnOff(WordValue.Child(runProperties, "dstrike"));

        var underline = WordValue.Child(runProperties, "u");
        if (underline is not null) format.Underline = WordValue.Attr(underline, "val") ?? "single";

        var color = WordValue.Attr(WordValue.Child(runProperties, "color"), "val");
        if (WordValue.IsRealColor(color)) format.Color = color;

        // Hai cách tô nền chữ khác nhau trong Word: bút bôi màu (highlight, ghi bằng tên màu) và nền ô chữ
        // (shd, ghi bằng mã màu). Người soạn dùng lẫn lộn cả hai nên phải đọc cả hai.
        var highlight = WordValue.ColorFromHighlightName(
            WordValue.Attr(WordValue.Child(runProperties, "highlight"), "val"));
        var shading = WordValue.Attr(WordValue.Child(runProperties, "shd"), "fill");
        if (highlight is not null) format.Background = highlight;
        else if (WordValue.IsRealColor(shading)) format.Background = shading;

        // Cỡ chữ trong Word tính bằng nửa point.
        var halfPoint = WordValue.Number(WordValue.Attr(WordValue.Child(runProperties, "sz"), "val"));
        if (halfPoint is > 0) format.SizePt = halfPoint.Value / 2;

        var fontName = WordValue.Attr(WordValue.Child(runProperties, "rFonts"), "ascii", "hAnsi", "cs");
        if (!string.IsNullOrWhiteSpace(fontName)) format.FontName = fontName;

        var vertical = WordValue.Attr(WordValue.Child(runProperties, "vertAlign"), "val");
        if (vertical is not null) format.VerticalAlign = vertical;

        return format;
    }

    public static WordParagraphFormat ReadParagraphFormat(OpenXmlElement? paragraphProperties)
    {
        var format = new WordParagraphFormat();
        if (paragraphProperties is null) return format;

        format.Alignment = WordValue.Attr(WordValue.Child(paragraphProperties, "jc"), "val")?.ToLowerInvariant() switch
        {
            "center" => "center",
            "right" or "end" => "right",
            "both" or "distribute" => "justify",
            "left" or "start" => "left",
            _ => null
        };

        var indent = WordValue.Child(paragraphProperties, "ind");
        if (indent is not null)
        {
            format.IndentLeftPt = WordValue.PointFromTwip(WordValue.Attr(indent, "left", "start"));
            format.IndentRightPt = WordValue.PointFromTwip(WordValue.Attr(indent, "right", "end"));

            // Thụt "treo" là dòng đầu nhô ngược ra ngoài lề — trong CSS chính là text-indent âm.
            var hanging = WordValue.PointFromTwip(WordValue.Attr(indent, "hanging"));
            var firstLine = WordValue.PointFromTwip(WordValue.Attr(indent, "firstLine"));
            if (hanging is not null) format.FirstLineIndentPt = -hanging.Value;
            else if (firstLine is not null) format.FirstLineIndentPt = firstLine.Value;
        }

        var spacing = WordValue.Child(paragraphProperties, "spacing");
        if (spacing is not null)
        {
            format.SpaceBeforePt = WordValue.PointFromTwip(WordValue.Attr(spacing, "before"));
            format.SpaceAfterPt = WordValue.PointFromTwip(WordValue.Attr(spacing, "after"));

            var line = WordValue.Number(WordValue.Attr(spacing, "line"));
            var rule = WordValue.Attr(spacing, "lineRule") ?? "auto";
            if (line is > 0)
                // lineRule "auto" đo giãn dòng theo bội số của 240; hai kiểu còn lại đo bằng twip.
                format.LineHeight = rule.Equals("auto", StringComparison.OrdinalIgnoreCase)
                    ? (line.Value / 240).ToString("0.##", CultureInfo.InvariantCulture)
                    : WordValue.Pt(line.Value / WordValue.TwipPerPoint);
        }

        var shading = WordValue.Attr(WordValue.Child(paragraphProperties, "shd"), "fill");
        if (WordValue.IsRealColor(shading)) format.Background = shading;

        format.BorderCss = WordValue.BoxBorderCss(WordValue.Child(paragraphProperties, "pBdr"));

        format.ContextualSpacing = WordValue.OnOff(WordValue.Child(paragraphProperties, "contextualSpacing"));

        var outline = WordValue.Integer(WordValue.Attr(WordValue.Child(paragraphProperties, "outlineLvl"), "val"));
        if (outline is not null) format.OutlineLevel = outline;

        var numbering = WordValue.Child(paragraphProperties, "numPr");
        if (numbering is not null)
        {
            format.NumberingId = WordValue.Integer(WordValue.Attr(WordValue.Child(numbering, "numId"), "val"));
            format.NumberingLevel = WordValue.Integer(WordValue.Attr(WordValue.Child(numbering, "ilvl"), "val"));
        }

        return format;
    }
}
