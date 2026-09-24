// File: Infrastructure/Documents/PowerPointHtmlConverter.cs
using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using static HbELearning.Infrastructure.Documents.WordValue;

namespace HbELearning.Infrastructure.Documents;

/// <summary>
/// Dựng từng slide thành một khung có đúng tỉ lệ bản gốc (aspect-ratio), trong đó mọi hình đặt tuyệt đối
/// bằng phần trăm của khổ slide. Cỡ chữ, lề, độ dày viền tính bằng đơn vị cqw (phần trăm bề ngang của chính
/// khung slide — khung là một "container"), nên slide thu nhỏ trên điện thoại hay phóng to trên màn hình rộng
/// thì chữ vẫn co giãn đúng tỉ lệ với hình, không bao giờ tràn khỏi ô như khi dùng cỡ chữ cố định.
///
/// Đọc thẳng cấu trúc XML theo tên thẻ (LocalName) giống bộ chuyển Word, thay vì dựa vào lớp định kiểu của
/// thư viện: file do Google Slides, Keynote, WPS xuất ra hay lệch lớp định kiểu nhưng tên thẻ thì luôn đúng.
/// </summary>
public class PowerPointHtmlConverter : IPowerPointHtmlConverter
{
    /// <summary>Trần số slide dựng ra — một file dựng cố ý có hàng chục nghìn slide không được làm treo máy chủ.</summary>
    private const int MaxSlide = 500;

    /// <summary>Trần số ảnh lưu ra đĩa cho một lần nhập.</summary>
    private const int MaxImage = 500;

    /// <summary>Trần độ sâu nhóm hình lồng nhau — chặn file dựng cố ý lồng vài nghìn cấp làm tràn ngăn xếp.</summary>
    private const int MaxGroupDepth = 12;

    private const double EmuPerPoint = 12700;

    /// <summary>Khổ 16:9 mặc định của PowerPoint (13,333 x 7,5 inch), dùng khi file không khai báo khổ slide.</summary>
    private const double DefaultSlideWidth = 12192000;
    private const double DefaultSlideHeight = 6858000;

    /// <summary>Cỡ chữ PowerPoint dùng khi không tầng kế thừa nào khai báo.</summary>
    private const double DefaultFontSizePt = 18;

    /// <summary>"Giãn dòng đơn" (100%) của PowerPoint cao khoảng 1,2 lần cỡ chữ, không phải đúng bằng cỡ chữ.</summary>
    private const double SingleLineHeight = 1.2;

    public async Task<PowerPointHtmlResult> ConvertAsync(Stream pptxStream, Func<byte[], string, Task<string>> saveImageAsync)
    {
        // OpenXml cần đọc tới lui trên stream; stream của IFormFile qua proxy đôi khi chỉ đọc tiến được.
        using var memory = new MemoryStream();
        await pptxStream.CopyToAsync(memory);
        memory.Position = 0;

        using var document = PresentationDocument.Open(memory, false);
        var presentationPart = document.PresentationPart;
        var presentation = presentationPart?.Presentation;
        if (presentationPart is null || presentation is null)
            return new PowerPointHtmlResult(string.Empty, null, 0, 0, 0);

        var slideSize = Child(presentation, "sldSz");
        var deck = new DeckContext(saveImageAsync)
        {
            Width = Number(Attr(slideSize, "cx")) is double width and > 0 ? width : DefaultSlideWidth,
            Height = Number(Attr(slideSize, "cy")) is double height and > 0 ? height : DefaultSlideHeight,
            DefaultTextStyle = Child(presentation, "defaultTextStyle")
        };

        // Thứ tự slide là thứ tự trong sldIdLst, không phải thứ tự file slideN.xml trong gói — kéo thả đổi
        // chỗ slide trong PowerPoint chỉ sửa danh sách này chứ không đổi tên file.
        var slidePartList = new List<SlidePart>();
        foreach (var slideId in Child(presentation, "sldIdLst")?.Elements().Where(x => x.LocalName == "sldId")
                                ?? Enumerable.Empty<OpenXmlElement>())
        {
            if (RelationshipId(slideId, "id") is not string relationshipId) continue;
            if (TryGetPart(presentationPart, relationshipId) is not SlidePart slidePart) continue;

            // Slide người soạn đã ẩn khi trình chiếu thì học viên cũng không thấy.
            if (Attr(slidePart.RootElement, "show") is "0" or "false") continue;

            slidePartList.Add(slidePart);
            if (slidePartList.Count >= MaxSlide) break;
        }

        if (slidePartList.Count == 0)
            return new PowerPointHtmlResult(string.Empty, null, 0, 0, 0);

        var builder = new StringBuilder("<div class=\"ppt-deck\">");
        for (var index = 0; index < slidePartList.Count; index++)
            await WriteSlideAsync(builder, slidePartList[index], index + 1, slidePartList.Count, deck);
        builder.Append("</div>");

        return new PowerPointHtmlResult(builder.ToString(), deck.Title, slidePartList.Count, deck.ImageCount, deck.SkippedCount);
    }

    // ==================================================================
    //  TRẠNG THÁI
    // ==================================================================

    /// <summary>Trạng thái dùng chung cho cả bài giảng.</summary>
    private sealed class DeckContext(Func<byte[], string, Task<string>> saveImageAsync)
    {
        public Func<byte[], string, Task<string>> SaveImageAsync { get; } = saveImageAsync;
        public double Width { get; init; }
        public double Height { get; init; }
        public OpenXmlElement? DefaultTextStyle { get; init; }

        /// <summary>Ảnh đã lưu, tra theo đường dẫn phần ảnh trong gói. Logo trên slide master xuất hiện ở mọi
        /// slide nhưng chỉ được lưu ra đĩa đúng một lần.</summary>
        public Dictionary<string, string?> ImageCache { get; } = new();

        public int ImageCount { get; set; }
        public int SkippedCount { get; set; }
        public string? Title { get; set; }

        /// <summary>Độ dài EMU quy ra phần trăm bề ngang slide, dạng cqw.</summary>
        public string Cq(double emu) => Format(emu / Width * 100) + "cqw";

        public string PercentX(double emu) => Format(emu / Width * 100) + "%";
        public string PercentY(double emu) => Format(emu / Height * 100) + "%";
    }

    /// <summary>Bối cảnh của MỘT slide: layout, master và bảng màu / phông của theme mà slide đó dùng.</summary>
    private sealed class SlideScope
    {
        public required DeckContext Deck { get; init; }
        public SlideLayoutPart? Layout { get; init; }
        public SlideMasterPart? Master { get; init; }

        /// <summary>Màu của theme theo tên gốc (dk1, lt1, accent1...).</summary>
        public required Dictionary<string, string> SchemeColor { get; init; }

        /// <summary>Bảng quy đổi tên màu dùng trong slide (bg1, tx1...) sang tên màu gốc của theme — lấy từ
        /// clrMap của slide master. Nhờ nó mà một theme nền tối chỉ cần đảo bg1 và tx1.</summary>
        public required Dictionary<string, string> ColorMap { get; init; }

        public string? MajorFont { get; init; }
        public string? MinorFont { get; init; }
        public int Depth { get; set; }

        public string? Scheme(string? name)
        {
            if (name is null or "phClr") return null;
            var key = ColorMap.TryGetValue(name, out var mapped) ? mapped : name;
            return SchemeColor.TryGetValue(key, out var hex) ? hex : null;
        }
    }

    /// <summary>Những gì cần biết để dựng chữ của một hộp chữ.</summary>
    private sealed class TextContext
    {
        public required OpenXmlPart Owner { get; init; }
        public required SlideScope Scope { get; init; }

        /// <summary>Các khối lstStyle theo thứ tự ưu tiên giảm dần: của chính hộp chữ, của placeholder bên
        /// layout, của placeholder bên master, kiểu chữ chung của master, kiểu chữ mặc định của bài giảng.
        /// Mỗi thuộc tính lấy ở tầng ĐẦU TIÊN có khai báo — đúng cách PowerPoint kế thừa định dạng.</summary>
        public required List<OpenXmlElement?> SourceList { get; init; }

        /// <summary>Tỉ lệ thu nhỏ chữ khi hộp chữ bật "Shrink text on overflow".</summary>
        public double FontScale { get; init; } = 1;

        /// <summary>Phần giãn dòng bị bớt đi cùng với việc thu nhỏ chữ (0..1).</summary>
        public double LineReduction { get; init; }

        /// <summary>Màu chữ theo "kiểu hình" (p:style/fontRef). Đứng SAU định dạng của chính hộp chữ nhưng
        /// TRƯỚC mọi tầng kế thừa bên ngoài — không thì màu đen mặc định của bài giảng đè lên chữ trắng của
        /// hình chèn từ menu Shapes.</summary>
        public string? ShapeFontColor { get; init; }
    }

    private sealed record Placeholder(string Type, string? Index);

    private readonly record struct Frame(double X, double Y, double Width, double Height, double Rotation, bool FlipH, bool FlipV);

    /// <summary>Phép quy đổi toạ độ của nhóm hình: hình trong nhóm ghi toạ độ theo hệ riêng của nhóm, phải
    /// đổi về hệ toạ độ của slide.</summary>
    private readonly record struct Mapper(double ScaleX, double ScaleY, double OffsetX, double OffsetY)
    {
        public static Mapper Identity => new(1, 1, 0, 0);
        public double MapX(double x) => OffsetX + x * ScaleX;
        public double MapY(double y) => OffsetY + y * ScaleY;
    }

    // ==================================================================
    //  SLIDE
    // ==================================================================

    private async Task WriteSlideAsync(StringBuilder builder, SlidePart slidePart, int number, int total, DeckContext deck)
    {
        var layout = slidePart.SlideLayoutPart;
        var master = layout?.SlideMasterPart;
        var scope = BuildScope(deck, layout, master);
        var slideRoot = slidePart.RootElement;

        if (number == 1) deck.Title = TitleOf(slideRoot);

        var cssList = new List<string>
        {
            "aspect-ratio:" + Format(deck.Width) + " / " + Format(deck.Height),
            await BackgroundCssAsync(slidePart, scope)
        };
        if (scope.Scheme("tx1") is string textColor) cssList.Add("color:#" + textColor);
        if (scope.MinorFont is string minor && WordHtmlConverter.FontFamilyCss(minor) is string family)
            cssList.Add("font-family:" + family);

        builder.Append("<figure class=\"ppt-slide-wrap\">")
               .Append("<figcaption class=\"ppt-slide-no\">Slide ").Append(number).Append(" / ").Append(total)
               .Append("</figcaption>")
               .Append("<section class=\"ppt-slide\" style=\"").Append(string.Join(';', cssList)).Append("\">");

        // Hình trang trí của master (logo, dải màu...) và của layout nằm DƯỚI nội dung slide. Slide hoặc
        // layout tắt "Hide background graphics" thì bỏ lớp tương ứng.
        var showMaster = Attr(slideRoot, "showMasterSp") is not ("0" or "false");
        var layoutShowMaster = Attr(layout?.RootElement, "showMasterSp") is not ("0" or "false");

        if (showMaster && layoutShowMaster && master is not null)
            await WriteTreeAsync(builder, SpTree(master.RootElement), master, scope, Mapper.Identity, false);
        if (showMaster && layout is not null)
            await WriteTreeAsync(builder, SpTree(layout.RootElement), layout, scope, Mapper.Identity, false);

        await WriteTreeAsync(builder, SpTree(slideRoot), slidePart, scope, Mapper.Identity, true);

        builder.Append("</section>").Append(NotesHtml(slidePart)).Append("</figure>");
    }

    private static SlideScope BuildScope(DeckContext deck, SlideLayoutPart? layout, SlideMasterPart? master)
    {
        // Bảng màu Office mặc định — dùng khi file không kèm theme (hiếm, nhưng file do công cụ khác sinh ra
        // vẫn gặp).
        var schemeColor = new Dictionary<string, string>
        {
            ["dk1"] = "000000", ["lt1"] = "FFFFFF", ["dk2"] = "44546A", ["lt2"] = "E7E6E6",
            ["accent1"] = "4472C4", ["accent2"] = "ED7D31", ["accent3"] = "A5A5A5",
            ["accent4"] = "FFC000", ["accent5"] = "5B9BD5", ["accent6"] = "70AD47",
            ["hlink"] = "0563C1", ["folHlink"] = "954F72"
        };

        var theme = master?.ThemePart?.RootElement;
        var colorScheme = theme?.Descendants().FirstOrDefault(x => x.LocalName == "clrScheme");
        foreach (var entry in colorScheme?.Elements() ?? Enumerable.Empty<OpenXmlElement>())
        {
            var colorElement = entry.Elements().FirstOrDefault();
            var hex = colorElement?.LocalName switch
            {
                "srgbClr" => Attr(colorElement, "val"),
                "sysClr" => Attr(colorElement, "lastClr"),
                _ => null
            };
            if (IsHex6(hex)) schemeColor[entry.LocalName] = hex!;
        }

        var fontScheme = theme?.Descendants().FirstOrDefault(x => x.LocalName == "fontScheme");

        var colorMap = new Dictionary<string, string>
        {
            ["bg1"] = "lt1", ["tx1"] = "dk1", ["bg2"] = "lt2", ["tx2"] = "dk2"
        };
        var colorMapElement = Child(master?.RootElement, "clrMap");
        foreach (var attribute in colorMapElement?.GetAttributes() ?? Enumerable.Empty<OpenXmlAttribute>())
            if (!string.IsNullOrEmpty(attribute.Value)) colorMap[attribute.LocalName] = attribute.Value;

        return new SlideScope
        {
            Deck = deck,
            Layout = layout,
            Master = master,
            SchemeColor = schemeColor,
            ColorMap = colorMap,
            MajorFont = Attr(Child(Child(fontScheme, "majorFont"), "latin"), "typeface"),
            MinorFont = Attr(Child(Child(fontScheme, "minorFont"), "latin"), "typeface")
        };
    }

    /// <summary>Nền slide: lấy ở slide, không có thì ở layout, rồi tới master — như PowerPoint.</summary>
    private async Task<string> BackgroundCssAsync(SlidePart slidePart, SlideScope scope)
    {
        var sourceList = new List<(OpenXmlElement? Root, OpenXmlPart? Part)>
        {
            (slidePart.RootElement, slidePart),
            (scope.Layout?.RootElement, scope.Layout),
            (scope.Master?.RootElement, scope.Master)
        };

        foreach (var (root, part) in sourceList)
        {
            var background = Child(Child(root, "cSld"), "bg");
            if (background is null || part is null) continue;

            if (Child(background, "bgPr") is { } backgroundProperty
                && await FillCssAsync(backgroundProperty, part, scope) is string fill && fill.Length > 0)
                return fill;

            if (ColorOf(Child(background, "bgRef"), scope) is string referenceColor)
                return "background:" + referenceColor;
        }

        return "background:#" + (scope.Scheme("bg1") ?? "FFFFFF");
    }

    // ==================================================================
    //  CÂY HÌNH
    // ==================================================================

    private async Task WriteTreeAsync(
        StringBuilder builder, OpenXmlElement? tree, OpenXmlPart owner, SlideScope scope, Mapper mapper, bool placeholderAllowed)
    {
        if (tree is null) return;
        foreach (var element in tree.Elements())
            await WriteElementAsync(builder, element, owner, scope, mapper, placeholderAllowed);
    }

    /// <param name="placeholderAllowed">false khi dựng lớp master / layout: placeholder ở đó chỉ là ô mẫu
    /// ("Click to add title"), nội dung thật nằm ở placeholder cùng loại trên slide.</param>
    private async Task WriteElementAsync(
        StringBuilder builder, OpenXmlElement element, OpenXmlPart owner, SlideScope scope, Mapper mapper, bool placeholderAllowed)
    {
        switch (element.LocalName)
        {
            case "sp":
                await WriteShapeAsync(builder, element, owner, scope, mapper, placeholderAllowed);
                break;

            case "pic":
                await WritePictureAsync(builder, element, owner, scope, mapper, placeholderAllowed);
                break;

            case "cxnSp":
                if (ReadFrame(Child(Child(element, "spPr"), "xfrm")) is Frame lineFrame)
                    WriteLine(builder, element, lineFrame, scope, mapper);
                break;

            case "grpSp":
                await WriteGroupAsync(builder, element, owner, scope, mapper, placeholderAllowed);
                break;

            case "graphicFrame":
                await WriteGraphicFrameAsync(builder, element, owner, scope, mapper);
                break;

            // Hình dùng tính năng mới có kèm bản dự phòng cho PowerPoint đời cũ. Chỉ lấy MỘT bản, lấy cả hai
            // là hình hiện ra hai lần.
            case "AlternateContent":
                var choice = element.Elements().FirstOrDefault(x => x.LocalName == "Choice")
                             ?? element.Elements().FirstOrDefault(x => x.LocalName == "Fallback");
                if (choice is not null)
                    await WriteTreeAsync(builder, choice, owner, scope, mapper, placeholderAllowed);
                break;
        }
    }

    private async Task WriteGroupAsync(
        StringBuilder builder, OpenXmlElement group, OpenXmlPart owner, SlideScope scope, Mapper mapper, bool placeholderAllowed)
    {
        if (scope.Depth >= MaxGroupDepth) return;

        var transform = Child(Child(group, "grpSpPr"), "xfrm");
        var offset = Child(transform, "off");
        var extent = Child(transform, "ext");
        var childOffset = Child(transform, "chOff");
        var childExtent = Child(transform, "chExt");

        var groupX = Number(Attr(offset, "x")) ?? 0;
        var groupY = Number(Attr(offset, "y")) ?? 0;
        var childX = Number(Attr(childOffset, "x")) ?? groupX;
        var childY = Number(Attr(childOffset, "y")) ?? groupY;
        var childWidth = Number(Attr(childExtent, "cx")) ?? 0;
        var childHeight = Number(Attr(childExtent, "cy")) ?? 0;
        var scaleX = childWidth > 0 ? (Number(Attr(extent, "cx")) ?? childWidth) / childWidth : 1;
        var scaleY = childHeight > 0 ? (Number(Attr(extent, "cy")) ?? childHeight) / childHeight : 1;

        var inner = new Mapper(
            mapper.ScaleX * scaleX,
            mapper.ScaleY * scaleY,
            mapper.OffsetX + mapper.ScaleX * (groupX - childX * scaleX),
            mapper.OffsetY + mapper.ScaleY * (groupY - childY * scaleY));

        scope.Depth++;
        await WriteTreeAsync(builder, group, owner, scope, inner, placeholderAllowed);
        scope.Depth--;
    }

    // ==================================================================
    //  HÌNH KHỐI VÀ HỘP CHỮ
    // ==================================================================

    private async Task WriteShapeAsync(
        StringBuilder builder, OpenXmlElement shape, OpenXmlPart owner, SlideScope scope, Mapper mapper, bool placeholderAllowed)
    {
        var placeholder = PlaceholderOf(shape);
        if (placeholder is not null && !placeholderAllowed) return;

        // Placeholder trên slide thường KHÔNG ghi vị trí, cỡ, kiểu chữ — tất cả kế thừa từ ô cùng loại bên
        // layout, rồi bên master.
        var (layoutPlaceholder, masterPlaceholder) = FindInheritedPlaceholder(placeholder, scope);

        var shapeProperty = Child(shape, "spPr");
        var frame = ReadFrame(Child(shapeProperty, "xfrm"))
                    ?? ReadFrame(Child(Child(layoutPlaceholder, "spPr"), "xfrm"))
                    ?? ReadFrame(Child(Child(masterPlaceholder, "spPr"), "xfrm"));
        if (frame is not Frame box) return;

        var geometry = Attr(Child(shapeProperty, "prstGeom"), "prst") ?? "rect";
        if (geometry is "line" or "straightConnector1")
        {
            WriteLine(builder, shape, box, scope, mapper);
            return;
        }

        var style = Child(shape, "style");

        // Hình không tự khai báo màu nền / viền thì lấy theo "kiểu hình" (p:style) — hình chèn từ menu
        // Insert > Shapes có màu accent1 chính là nhờ đây.
        var fill = await FillCssAsync(shapeProperty, owner, scope)
                   ?? (StyleColor(style, "fillRef", scope) is string fillColor ? "background:" + fillColor : null);
        var border = LineValue(Child(shapeProperty, "ln"), scope)
                     ?? (StyleColor(style, "lnRef", scope) is string lineColor ? scope.Deck.Cq(9525) + " solid " + lineColor : null);

        var textBody = Child(shape, "txBody");
        var (textHtml, hasText) = textBody is null
            ? (string.Empty, false)
            : await TextBodyHtmlAsync(textBody, new TextContext
            {
                Owner = owner,
                Scope = scope,
                SourceList = new List<OpenXmlElement?>
                {
                    Child(textBody, "lstStyle"),
                    Child(Child(layoutPlaceholder, "txBody"), "lstStyle"),
                    Child(Child(masterPlaceholder, "txBody"), "lstStyle"),
                    MasterTextStyle(scope, placeholder),
                    scope.Deck.DefaultTextStyle
                },
                FontScale = (Number(Attr(Child(Child(textBody, "bodyPr"), "normAutofit"), "fontScale")) ?? 100000) / 100000,
                LineReduction = (Number(Attr(Child(Child(textBody, "bodyPr"), "normAutofit"), "lnSpcReduction")) ?? 0) / 100000,
                ShapeFontColor = ColorOf(Child(style, "fontRef"), scope)
            });

        var hasFill = !string.IsNullOrEmpty(fill);
        var hasBorder = !string.IsNullOrEmpty(border);
        if (!hasText && !hasFill && !hasBorder) return;

        builder.Append("<div class=\"ppt-shape\" style=\"").Append(BoxCss(box, mapper, scope.Deck, false)).Append("\">");

        // Nền và viền nằm ở một lớp riêng dưới chữ: hình tam giác / mũi tên cắt bằng clip-path, cắt thẳng
        // trên hộp chữ là cắt cụt luôn chữ. Lật hình (flip) cũng chỉ áp cho lớp này — PowerPoint lật hình
        // chứ không lật ngược chữ.
        if (hasFill || hasBorder)
        {
            var geometryCssList = new List<string>();
            if (hasFill) geometryCssList.Add(fill!);
            if (hasBorder) geometryCssList.Add("border:" + border);
            if (GeometryCss(geometry, Child(shapeProperty, "prstGeom"), box, mapper, scope.Deck) is string geometryCss)
                geometryCssList.Add(geometryCss);
            if (FlipCss(box) is string flip) geometryCssList.Add("transform:" + flip);

            builder.Append("<div class=\"ppt-geom\" style=\"").Append(string.Join(';', geometryCssList)).Append("\"></div>");
        }

        if (hasText)
            builder.Append(TextLayerOpen(textBody!, layoutPlaceholder, masterPlaceholder, style, scope))
                   .Append(textHtml).Append("</div>");

        builder.Append("</div>");
    }

    /// <summary>Thẻ mở của lớp chữ: canh dọc, lề trong, xoay chữ, ngắt dòng.</summary>
    private static string TextLayerOpen(
        OpenXmlElement textBody, OpenXmlElement? layoutPlaceholder, OpenXmlElement? masterPlaceholder,
        OpenXmlElement? style, SlideScope scope)
    {
        var bodyPropertyList = new[]
        {
            Child(textBody, "bodyPr"),
            Child(Child(layoutPlaceholder, "txBody"), "bodyPr"),
            Child(Child(masterPlaceholder, "txBody"), "bodyPr")
        };
        string? Body(string name) => bodyPropertyList.Select(x => Attr(x, name)).FirstOrDefault(x => x is not null);

        var deck = scope.Deck;
        var cssList = new List<string>
        {
            "justify-content:" + (Body("anchor") switch { "ctr" => "center", "b" => "flex-end", _ => "flex-start" }),
            "padding:" + deck.Cq(Number(Body("tIns")) ?? 45720) + " " + deck.Cq(Number(Body("rIns")) ?? 91440) + " "
                + deck.Cq(Number(Body("bIns")) ?? 45720) + " " + deck.Cq(Number(Body("lIns")) ?? 91440)
        };

        switch (Body("vert"))
        {
            case "vert" or "eaVert" or "wordArtVert" or "mongolianVert" or "wordArtVertRtl":
                cssList.Add("writing-mode:vertical-rl");
                break;
            case "vert270":
                cssList.Add("writing-mode:vertical-rl;transform:rotate(180deg)");
                break;
        }

        // Màu chữ theo "kiểu hình": chữ trên hình chèn từ menu Shapes mặc định màu trắng (lt1) là nhờ đây.
        if (ColorOf(Child(style, "fontRef"), scope) is string fontColor) cssList.Add("color:" + fontColor);

        var className = Body("wrap") is "none" ? "ppt-text ppt-nowrap" : "ppt-text";
        return "<div class=\"" + className + "\" style=\"" + string.Join(';', cssList) + "\">";
    }

    // ==================================================================
    //  CHỮ
    // ==================================================================

    private async Task<(string Html, bool HasText)> TextBodyHtmlAsync(OpenXmlElement textBody, TextContext context)
    {
        var builder = new StringBuilder();
        var hasText = false;

        // Bộ đếm của danh sách đánh số, mỗi cấp một bộ. Đánh số trong PowerPoint nối tiếp qua các đoạn cùng
        // cấp và bắt đầu lại khi gặp một đoạn cùng cấp không đánh số.
        var counterList = new int[9];
        var numberTypeList = new string?[9];

        foreach (var paragraph in textBody.Elements().Where(x => x.LocalName == "p"))
        {
            var paragraphProperty = Child(paragraph, "pPr");
            var level = Math.Clamp(Integer(Attr(paragraphProperty, "lvl")) ?? 0, 0, 8);
            var levelName = "lvl" + (level + 1) + "pPr";

            var levelList = new List<OpenXmlElement>();
            if (paragraphProperty is not null) levelList.Add(paragraphProperty);
            foreach (var source in context.SourceList)
                if (Child(source, levelName) is { } levelProperty) levelList.Add(levelProperty);

            // Tầng "của chính hộp chữ" (đoạn văn + lstStyle riêng của hộp) tách khỏi các tầng kế thừa bên
            // ngoài — màu theo kiểu hình chen vào đúng giữa hai nhóm này.
            var ownLevelCount = (paragraphProperty is null ? 0 : 1)
                                + (context.SourceList.Count > 0 && Child(context.SourceList[0], levelName) is not null ? 1 : 0);
            var defaultRunList = new RunDefaultList(
                levelList.Take(ownLevelCount).Select(x => Child(x, "defRPr")).OfType<OpenXmlElement>().ToList(),
                levelList.Skip(ownLevelCount).Select(x => Child(x, "defRPr")).OfType<OpenXmlElement>().ToList());

            // Chữ của đoạn.
            var runBuilder = new StringBuilder();
            double? firstSize = null;
            string? firstColor = null;
            foreach (var child in paragraph.Elements())
            {
                switch (child.LocalName)
                {
                    case "r":
                    case "fld":
                    {
                        var text = Child(child, "t")?.InnerText ?? string.Empty;
                        if (text.Length == 0) break;

                        var run = ResolveRun(Child(child, "rPr"), defaultRunList, context);
                        firstSize ??= run.SizePt;
                        firstColor ??= run.Color;
                        runBuilder.Append(RunHtml(text, run, Child(child, "rPr"), context));
                        break;
                    }

                    case "br":
                        runBuilder.Append("<br>");
                        break;
                }
            }

            var isEmpty = runBuilder.Length == 0;
            if (!isEmpty) hasText = true;

            // Cỡ chữ của đoạn: đặt lên thẻ p để chiều cao dòng tính đúng theo chữ của đoạn chứ không theo cỡ
            // chữ của trang web — đoạn rỗng (dòng trắng để giãn cách) cũng cao đúng như bản gốc.
            var paragraphSize = firstSize
                                ?? ResolveRun(Child(paragraph, "endParaRPr"), defaultRunList, context).SizePt;

            string? Paragraph(string name) => levelList.Select(x => Attr(x, name)).FirstOrDefault(x => x is not null);
            var cssList = new List<string> { "font-size:" + context.Scope.Deck.Cq(paragraphSize * EmuPerPoint) };

            switch (Paragraph("algn"))
            {
                case "ctr": cssList.Add("text-align:center"); break;
                case "r": cssList.Add("text-align:right"); break;
                case "just" or "dist" or "thaiDist": cssList.Add("text-align:justify"); break;
            }

            var marginLeft = Number(Paragraph("marL")) ?? 0;
            var indent = Number(Paragraph("indent")) ?? 0;
            if (marginLeft != 0) cssList.Add("padding-left:" + context.Scope.Deck.Cq(marginLeft));
            if (indent != 0) cssList.Add("text-indent:" + context.Scope.Deck.Cq(indent));

            if (LineHeightCss(levelList, context) is string lineHeight) cssList.Add("line-height:" + lineHeight);
            if (SpacingCss(levelList, "spcBef", context) is string before) cssList.Add("margin-top:" + before);
            if (SpacingCss(levelList, "spcAft", context) is string after) cssList.Add("margin-bottom:" + after);

            // Gạch đầu dòng: PowerPoint không vẽ dấu đầu dòng cho đoạn rỗng.
            var bulletHtml = string.Empty;
            var bulletHolder = levelList.FirstOrDefault(x => x.Elements().Any(IsBulletElement));
            var bullet = bulletHolder?.Elements().First(IsBulletElement);

            if (!isEmpty)
            {
                if (bullet?.LocalName == "buAutoNum")
                {
                    var type = Attr(bullet, "type") ?? "arabicPeriod";
                    if (numberTypeList[level] != type)
                    {
                        numberTypeList[level] = type;
                        counterList[level] = (Integer(Attr(bullet, "startAt")) ?? 1) - 1;
                    }
                    counterList[level]++;
                    ResetDeeper(level + 1);
                    bulletHtml = BulletHtml(NumberText(type, counterList[level]), null, levelList, paragraphSize, firstColor, indent, context);
                }
                else
                {
                    ResetDeeper(level);
                    if (bullet?.LocalName == "buChar" && Attr(bullet, "char") is string character)
                    {
                        var font = levelList.Select(x => Attr(Child(x, "buFont"), "typeface")).FirstOrDefault(x => x is not null);
                        bulletHtml = BulletHtml(BulletCharacter(character, font), font, levelList, paragraphSize, firstColor, indent, context);
                    }
                }
            }

            builder.Append("<p style=\"").Append(string.Join(';', cssList)).Append("\">")
                   .Append(bulletHtml)
                   .Append(isEmpty ? "&nbsp;" : runBuilder.ToString())
                   .Append("</p>");
        }

        return (builder.ToString(), hasText);

        void ResetDeeper(int from)
        {
            for (var index = from; index < counterList.Length; index++)
            {
                counterList[index] = 0;
                numberTypeList[index] = null;
            }
        }
    }

    private static bool IsBulletElement(OpenXmlElement element) =>
        element.LocalName is "buNone" or "buChar" or "buAutoNum" or "buBlip";

    private string BulletHtml(
        string text, string? font, List<OpenXmlElement> levelList, double paragraphSize, string? firstColor,
        double indent, TextContext context)
    {
        var deck = context.Scope.Deck;
        var cssList = new List<string> { "display:inline-block", "text-indent:0" };

        // Dấu đầu dòng chiếm đúng phần thụt âm (hanging indent) để chữ dòng đầu thẳng hàng với các dòng sau.
        if (indent < 0) cssList.Add("min-width:" + deck.Cq(-indent));
        else cssList.Add("margin-right:0.4em");

        var size = paragraphSize;
        var sizePercent = levelList.Select(x => Number(Attr(Child(x, "buSzPct"), "val"))).FirstOrDefault(x => x is not null);
        var sizePoint = levelList.Select(x => Number(Attr(Child(x, "buSzPts"), "val"))).FirstOrDefault(x => x is not null);
        if (sizePoint is double point) size = point / 100 * context.FontScale;
        else if (sizePercent is double percent) size = paragraphSize * percent / 100000;
        cssList.Add("font-size:" + deck.Cq(size * EmuPerPoint));

        var color = ColorOf(levelList.Select(x => Child(x, "buClr")).FirstOrDefault(x => x is not null), context.Scope) ?? firstColor;
        if (color is not null) cssList.Add("color:" + color);

        // Phông Symbol / Wingdings đã được quy sang ký tự Unicode tương đương, không ghi tên phông đó ra nữa.
        if (font is not null && !IsSymbolFont(font) && WordHtmlConverter.FontFamilyCss(ResolveFont(font, context.Scope)) is string family)
            cssList.Add("font-family:" + family);

        return "<span class=\"ppt-bullet\" style=\"" + string.Join(';', cssList) + "\">"
               + WordHtmlConverter.HtmlEncode(text) + "</span>";
    }

    /// <summary>Định dạng một đoạn chữ sau khi đã đi hết chuỗi kế thừa.</summary>
    private sealed record RunFormat(
        double SizePt, bool Bold, bool Italic, bool Underline, bool Strike, double Baseline, string? Cap,
        string? Color, string? FontName);

    /// <summary>defRPr của các tầng: của chính hộp chữ (Own) và kế thừa từ bên ngoài (Inherited).</summary>
    private sealed record RunDefaultList(List<OpenXmlElement> Own, List<OpenXmlElement> Inherited);

    private static RunFormat ResolveRun(OpenXmlElement? runProperty, RunDefaultList defaultRunList, TextContext context)
    {
        var ownChain = new List<OpenXmlElement>();
        if (runProperty is not null) ownChain.Add(runProperty);
        ownChain.AddRange(defaultRunList.Own);
        var chain = ownChain.Concat(defaultRunList.Inherited).ToList();

        string? Value(string name) => chain.Select(x => Attr(x, name)).FirstOrDefault(x => x is not null);

        // Màu chữ: tầng của chính hộp chữ, rồi màu theo kiểu hình, rồi mới tới các tầng kế thừa.
        var color = FillColor(ownChain) ?? context.ShapeFontColor ?? FillColor(defaultRunList.Inherited);

        string? FillColor(List<OpenXmlElement> list)
        {
            // Lấy ở tầng đầu tiên có khai báo kiểu tô (solidFill / gradFill / noFill).
            var fillHolder = list.FirstOrDefault(x => x.Elements().Any(c => c.LocalName is "solidFill" or "gradFill" or "noFill"));
            var fill = fillHolder?.Elements().First(c => c.LocalName is "solidFill" or "gradFill" or "noFill");
            return fill?.LocalName switch
            {
                "solidFill" => ColorOf(fill, context.Scope),
                // Chữ tô dải màu: lấy màu của điểm dừng đầu tiên, gần bản gốc nhất mà vẫn đọc được.
                "gradFill" => ColorOf(Child(fill, "gsLst")?.Elements().FirstOrDefault(), context.Scope),
                "noFill" => "transparent",
                _ => null
            };
        }

        var font = chain.Select(x => Attr(Child(x, "latin"), "typeface")).FirstOrDefault(x => x is not null);

        return new RunFormat(
            (Number(Value("sz")) is double size ? size / 100 : DefaultFontSizePt) * context.FontScale,
            Value("b") is "1" or "true",
            Value("i") is "1" or "true",
            Value("u") is string underline && underline != "none",
            Value("strike") is string strike && strike != "noStrike",
            Number(Value("baseline")) ?? 0,
            Value("cap"),
            color,
            font is null ? null : ResolveFont(font, context.Scope));
    }

    private string RunHtml(string text, RunFormat run, OpenXmlElement? runProperty, TextContext context)
    {
        var cssList = new List<string> { "font-size:" + context.Scope.Deck.Cq(run.SizePt * EmuPerPoint) };
        if (run.Bold) cssList.Add("font-weight:bold");
        if (run.Italic) cssList.Add("font-style:italic");

        var decorationList = new List<string>();
        if (run.Underline) decorationList.Add("underline");
        if (run.Strike) decorationList.Add("line-through");
        if (decorationList.Count > 0) cssList.Add("text-decoration:" + string.Join(' ', decorationList));

        if (run.Color is not null) cssList.Add("color:" + run.Color);
        if (run.FontName is not null && WordHtmlConverter.FontFamilyCss(run.FontName) is string family)
            cssList.Add("font-family:" + family);
        if (run.Cap is "all") cssList.Add("text-transform:uppercase");
        else if (run.Cap is "small") cssList.Add("font-variant:small-caps");

        var html = "<span style=\"" + string.Join(';', cssList) + "\">" + WordHtmlConverter.HtmlEncode(text) + "</span>";

        // Chỉ số trên / dưới: baseline tính bằng 1/1000 phần trăm, dương là lên trên.
        if (run.Baseline > 0) html = "<sup>" + html + "</sup>";
        else if (run.Baseline < 0) html = "<sub>" + html + "</sub>";

        var hyperlinkId = RelationshipId(Child(runProperty, "hlinkClick"), "id");
        if (hyperlinkId is not null)
        {
            var relationship = context.Owner.HyperlinkRelationships.FirstOrDefault(x => x.Id == hyperlinkId);
            if (WordHtmlConverter.SafeUrl(relationship?.Uri?.ToString()) is string url)
                html = "<a href=\"" + WordHtmlConverter.HtmlEncode(url) + "\" target=\"_blank\" rel=\"noopener\">" + html + "</a>";
        }

        return html;
    }

    private static string? LineHeightCss(List<OpenXmlElement> levelList, TextContext context)
    {
        var spacing = levelList.Select(x => Child(x, "lnSpc")).FirstOrDefault(x => x is not null);
        if (spacing is null)
            return context.LineReduction > 0 ? Format(SingleLineHeight * (1 - context.LineReduction)) : null;

        if (Number(Attr(Child(spacing, "spcPct"), "val")) is double percent)
            return Format(SingleLineHeight * Math.Max(0.5, percent / 100000 - context.LineReduction));

        if (Number(Attr(Child(spacing, "spcPts"), "val")) is double point)
            return context.Scope.Deck.Cq(point / 100 * EmuPerPoint);

        return null;
    }

    private static string? SpacingCss(List<OpenXmlElement> levelList, string name, TextContext context)
    {
        var spacing = levelList.Select(x => Child(x, name)).FirstOrDefault(x => x is not null);
        if (Number(Attr(Child(spacing, "spcPts"), "val")) is double point)
            return context.Scope.Deck.Cq(point / 100 * EmuPerPoint * context.FontScale);
        if (Number(Attr(Child(spacing, "spcPct"), "val")) is double percent)
            return Format(percent / 100000 * SingleLineHeight) + "em";
        return null;
    }

    /// <summary>Tên phông tham chiếu theme (+mj-lt = phông tiêu đề, +mn-lt = phông nội dung) đổi ra tên thật.</summary>
    private static string ResolveFont(string typeface, SlideScope scope) => typeface switch
    {
        "+mj-lt" or "+mj-ea" or "+mj-cs" => scope.MajorFont ?? typeface,
        "+mn-lt" or "+mn-ea" or "+mn-cs" => scope.MinorFont ?? typeface,
        _ => typeface
    };

    private static bool IsSymbolFont(string font) =>
        font.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase)
        || font.Equals("Symbol", StringComparison.OrdinalIgnoreCase)
        || font.StartsWith("Webdings", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ký tự gạch đầu dòng. Dấu đầu dòng của PowerPoint hay dùng phông Wingdings / Symbol — trình
    /// duyệt không có các phông đó nên phải đổi sang ký tự Unicode trông giống; mã lạ thì dùng chấm tròn.</summary>
    private static string BulletCharacter(string character, string? font)
    {
        if (font is null || !IsSymbolFont(font)) return character;

        return character switch
        {
            "§" => "▪",
            "Ø" => "➢",
            "ü" => "✓",
            "q" => "❑",
            "v" => "❖",
            "l" => "●",
            "n" => "■",
            "u" => "◆",
            "Ÿ" or "·" => "•",
            "o" => "○",
            "à" or "è" => "➔",
            _ => "•"
        };
    }

    private static string NumberText(string type, int number)
    {
        var (value, prefix, suffix) = type switch
        {
            "alphaLcPeriod" => (Alpha(number, false), "", "."),
            "alphaUcPeriod" => (Alpha(number, true), "", "."),
            "alphaLcParenR" => (Alpha(number, false), "", ")"),
            "alphaUcParenR" => (Alpha(number, true), "", ")"),
            "alphaLcParenBoth" => (Alpha(number, false), "(", ")"),
            "alphaUcParenBoth" => (Alpha(number, true), "(", ")"),
            "romanLcPeriod" => (Roman(number).ToLowerInvariant(), "", "."),
            "romanUcPeriod" => (Roman(number), "", "."),
            "romanLcParenR" => (Roman(number).ToLowerInvariant(), "", ")"),
            "romanUcParenR" => (Roman(number), "", ")"),
            "arabicParenR" => (number.ToString(CultureInfo.InvariantCulture), "", ")"),
            "arabicParenBoth" => (number.ToString(CultureInfo.InvariantCulture), "(", ")"),
            "arabicPlain" => (number.ToString(CultureInfo.InvariantCulture), "", ""),
            _ => (number.ToString(CultureInfo.InvariantCulture), "", ".")
        };
        return prefix + value + suffix;

        static string Alpha(int value, bool upper)
        {
            var text = string.Empty;
            for (var n = Math.Max(1, value); n > 0; n = (n - 1) / 26)
                text = (char)('a' + (n - 1) % 26) + text;
            return upper ? text.ToUpperInvariant() : text;
        }

        static string Roman(int value)
        {
            var map = new (int Value, string Symbol)[]
            {
                (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
                (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
            };
            var builder = new StringBuilder();
            var rest = Math.Clamp(value, 1, 3999);
            foreach (var (number, symbol) in map)
                while (rest >= number) { builder.Append(symbol); rest -= number; }
            return builder.ToString();
        }
    }

    // ==================================================================
    //  ẢNH, ĐƯỜNG THẲNG, BẢNG
    // ==================================================================

    private async Task WritePictureAsync(
        StringBuilder builder, OpenXmlElement picture, OpenXmlPart owner, SlideScope scope, Mapper mapper, bool placeholderAllowed)
    {
        var placeholder = PlaceholderOf(picture);
        if (placeholder is not null && !placeholderAllowed) return;

        var (layoutPlaceholder, masterPlaceholder) = FindInheritedPlaceholder(placeholder, scope);
        var frame = ReadFrame(Child(Child(picture, "spPr"), "xfrm"))
                    ?? ReadFrame(Child(Child(layoutPlaceholder, "spPr"), "xfrm"))
                    ?? ReadFrame(Child(Child(masterPlaceholder, "spPr"), "xfrm"));
        if (frame is not Frame box) return;

        await WriteImageAsync(builder, picture, box, owner, scope, mapper);
    }

    private async Task WriteImageAsync(
        StringBuilder builder, OpenXmlElement picture, Frame box, OpenXmlPart owner, SlideScope scope, Mapper mapper)
    {
        var blipFill = Child(picture, "blipFill");
        var url = await ImageUrlAsync(owner, RelationshipId(Child(blipFill, "blip"), "embed"), scope.Deck);
        if (url is null)
        {
            scope.Deck.SkippedCount++;
            return;
        }

        // Cắt ảnh (Crop) của PowerPoint: srcRect ghi phần bị cắt ở mỗi cạnh, đơn vị 1/1000 phần trăm. Dựng
        // lại bằng cách phóng ảnh to ra rồi đẩy lệch trong một khung che phần thừa.
        var crop = Child(blipFill, "srcRect");
        var left = (Number(Attr(crop, "l")) ?? 0) / 100000;
        var top = (Number(Attr(crop, "t")) ?? 0) / 100000;
        var right = (Number(Attr(crop, "r")) ?? 0) / 100000;
        var bottom = (Number(Attr(crop, "b")) ?? 0) / 100000;
        var visibleWidth = 1 - left - right;
        var visibleHeight = 1 - top - bottom;

        var imageCss = visibleWidth > 0.01 && visibleHeight > 0.01
            ? "left:" + Format(-left / visibleWidth * 100) + "%;top:" + Format(-top / visibleHeight * 100)
              + "%;width:" + Format(100 / visibleWidth) + "%;height:" + Format(100 / visibleHeight) + "%"
            : "left:0;top:0;width:100%;height:100%";

        var cssList = new List<string> { BoxCss(box, mapper, scope.Deck, true), "overflow:hidden" };
        if (LineValue(Child(Child(picture, "spPr"), "ln"), scope) is string border && border.Length > 0)
            cssList.Add("border:" + border);

        var description = Attr(picture.Descendants().FirstOrDefault(x => x.LocalName == "cNvPr"), "descr") ?? string.Empty;

        builder.Append("<div class=\"ppt-shape\" style=\"").Append(string.Join(';', cssList)).Append("\">")
               .Append("<img src=\"").Append(WordHtmlConverter.HtmlEncode(url))
               .Append("\" alt=\"").Append(WordHtmlConverter.HtmlEncode(description))
               .Append("\" loading=\"lazy\" style=\"").Append(imageCss).Append("\"></div>");
    }

    /// <summary>Đường thẳng / đường nối. Đường ngang hoặc dọc dựng bằng viền cho sắc nét; đường chéo dựng bằng
    /// SVG, hướng chéo suy từ cờ lật của hình.</summary>
    private static void WriteLine(StringBuilder builder, OpenXmlElement shape, Frame box, SlideScope scope, Mapper mapper)
    {
        var line = Child(Child(shape, "spPr"), "ln");
        if (Child(line, "noFill") is not null) return;

        var color = ColorOf(Child(line, "solidFill"), scope)
                    ?? StyleColor(Child(shape, "style"), "lnRef", scope)
                    ?? "#" + (scope.Scheme("tx1") ?? "000000");
        var width = scope.Deck.Cq(Math.Max(Number(Attr(line, "w")) ?? 9525, 6350));
        var dash = DashCss(line);

        var boxCss = BoxCss(box, mapper, scope.Deck, false);

        if (box.Width <= 0 || box.Height <= 0)
        {
            var side = box.Height <= 0 ? "border-top" : "border-left";
            builder.Append("<div class=\"ppt-shape\" style=\"").Append(boxCss).Append(';').Append(side).Append(':')
                   .Append(width).Append(' ').Append(dash).Append(' ').Append(color).Append("\"></div>");
            return;
        }

        var (x1, y1, x2, y2) = (box.FlipH, box.FlipV) switch
        {
            (true, false) => (100, 0, 0, 100),
            (false, true) => (0, 100, 100, 0),
            _ => (0, 0, 100, 100)
        };
        var dashArray = dash == "solid" ? string.Empty : dash == "dotted" ? ";stroke-dasharray:1 3" : ";stroke-dasharray:6 4";

        builder.Append("<div class=\"ppt-shape\" style=\"").Append(boxCss).Append("\">")
               .Append("<svg class=\"ppt-line\" viewBox=\"0 0 100 100\" preserveAspectRatio=\"none\" aria-hidden=\"true\">")
               .Append("<line x1=\"").Append(x1).Append("\" y1=\"").Append(y1).Append("\" x2=\"").Append(x2)
               .Append("\" y2=\"").Append(y2).Append("\" vector-effect=\"non-scaling-stroke\" style=\"stroke:")
               .Append(color).Append(";stroke-width:").Append(width).Append(dashArray).Append("\"/></svg></div>");
    }

    private async Task WriteGraphicFrameAsync(
        StringBuilder builder, OpenXmlElement graphicFrame, OpenXmlPart owner, SlideScope scope, Mapper mapper)
    {
        var frame = ReadFrame(Child(graphicFrame, "xfrm"));
        var graphicData = graphicFrame.Descendants().FirstOrDefault(x => x.LocalName == "graphicData");

        if (frame is Frame box && Child(graphicData, "tbl") is { } table)
        {
            await WriteTableAsync(builder, table, box, owner, scope, mapper);
            return;
        }

        // Đối tượng nhúng (bảng Excel, tài liệu Word...) luôn kèm một ảnh chụp để hiển thị — dùng ảnh đó.
        var preview = graphicData?.Descendants().FirstOrDefault(x => x.LocalName == "pic");
        if (frame is Frame previewBox && preview is not null)
        {
            await WriteImageAsync(builder, preview, previewBox, owner, scope, mapper);
            return;
        }

        // Biểu đồ, SmartArt: cần một bộ vẽ riêng, không dựng được bằng HTML thuần.
        scope.Deck.SkippedCount++;
    }

    private async Task WriteTableAsync(
        StringBuilder builder, OpenXmlElement table, Frame box, OpenXmlPart owner, SlideScope scope, Mapper mapper)
    {
        var deck = scope.Deck;
        var tableProperty = Child(table, "tblPr");
        var firstRow = Attr(tableProperty, "firstRow") is "1" or "true";
        var bandRow = Attr(tableProperty, "bandRow") is "1" or "true";
        var accent = scope.Scheme("accent1") ?? "4472C4";
        var light = scope.Scheme("lt1") ?? "FFFFFF";

        var columnList = Child(table, "tblGrid")?.Elements().Where(x => x.LocalName == "gridCol")
                             .Select(x => Number(Attr(x, "w")) ?? 0).ToList()
                         ?? new List<double>();
        var totalWidth = columnList.Sum();

        // Chiều cao để tự nhiên: bảng trong PowerPoint tự cao lên theo chữ, khung ghi trong file chỉ là cỡ lúc lưu.
        builder.Append("<div class=\"ppt-shape\" style=\"").Append(BoxCss(box, mapper, deck, false)).Append(";height:auto\">")
               .Append("<table class=\"ppt-table\">");

        if (totalWidth > 0)
        {
            builder.Append("<colgroup>");
            foreach (var width in columnList)
                builder.Append("<col style=\"width:").Append(Format(width / totalWidth * 100)).Append("%\">");
            builder.Append("</colgroup>");
        }

        var rowIndex = 0;
        foreach (var row in table.Elements().Where(x => x.LocalName == "tr"))
        {
            var rowHeight = Number(Attr(row, "h")) ?? 0;
            builder.Append("<tr style=\"height:").Append(deck.Cq(rowHeight * mapper.ScaleY)).Append("\">");

            var isHeader = firstRow && rowIndex == 0;
            var bodyIndex = rowIndex - (firstRow ? 1 : 0);

            foreach (var cell in row.Elements().Where(x => x.LocalName == "tc"))
            {
                // Ô bị gộp vào ô bên trái / bên trên: đã nằm trong colspan / rowspan của ô đó.
                if (Attr(cell, "hMerge") is "1" or "true" || Attr(cell, "vMerge") is "1" or "true") continue;

                var cellProperty = Child(cell, "tcPr");
                var cssList = new List<string>();

                // Màu của kiểu bảng (table style) nằm ở một phần riêng và rất nhiều biến thể; ô nào không tự
                // khai báo màu thì dùng kiểu mặc định của PowerPoint (Medium Style 2) theo màu accent1 của theme.
                var fill = await FillCssAsync(cellProperty, owner, scope);
                if (fill is null)
                {
                    if (isHeader)
                        cssList.Add("background:#" + accent + ";color:#" + light + ";font-weight:bold");
                    else if (bandRow)
                        cssList.Add("background:" + Tint(accent, bodyIndex % 2 == 0 ? 0.4 : 0.2));
                    else
                        cssList.Add("background:" + Tint(accent, 0.2));
                }
                else if (fill.Length > 0)
                {
                    cssList.Add(fill);
                }

                foreach (var (side, name) in new[] { ("left", "lnL"), ("right", "lnR"), ("top", "lnT"), ("bottom", "lnB") })
                {
                    var value = LineValue(Child(cellProperty, name), scope);
                    if (value is null) cssList.Add("border-" + side + ":1px solid #" + light);
                    else if (value.Length > 0) cssList.Add("border-" + side + ":" + value);
                }

                cssList.Add("padding:" + deck.Cq(Number(Attr(cellProperty, "marT")) ?? 45720) + " "
                            + deck.Cq(Number(Attr(cellProperty, "marR")) ?? 91440) + " "
                            + deck.Cq(Number(Attr(cellProperty, "marB")) ?? 45720) + " "
                            + deck.Cq(Number(Attr(cellProperty, "marL")) ?? 91440));
                cssList.Add("vertical-align:" + (Attr(cellProperty, "anchor") switch
                {
                    "ctr" => "middle",
                    "b" => "bottom",
                    _ => "top"
                }));

                var textBody = Child(cell, "txBody");
                var (textHtml, _) = textBody is null
                    ? (string.Empty, false)
                    : await TextBodyHtmlAsync(textBody, new TextContext
                    {
                        Owner = owner,
                        Scope = scope,
                        SourceList = new List<OpenXmlElement?> { Child(textBody, "lstStyle"), deck.DefaultTextStyle }
                    });

                builder.Append("<td");
                if (Integer(Attr(cell, "gridSpan")) is int columnSpan and > 1)
                    builder.Append(" colspan=\"").Append(columnSpan).Append('"');
                if (Integer(Attr(cell, "rowSpan")) is int rowSpan and > 1)
                    builder.Append(" rowspan=\"").Append(rowSpan).Append('"');
                builder.Append(" style=\"").Append(string.Join(';', cssList)).Append("\">").Append(textHtml).Append("</td>");
            }

            builder.Append("</tr>");
            rowIndex++;
        }

        builder.Append("</table></div>");
    }

    /// <summary>Ghi chú của người trình bày — thường là lời giảng cho slide, đưa vào cho học viên tự đọc.
    /// Thu gọn mặc định để không chen giữa các slide.</summary>
    private static string NotesHtml(SlidePart slidePart)
    {
        var tree = SpTree(slidePart.NotesSlidePart?.RootElement);
        var body = tree?.Descendants().Where(x => x.LocalName == "sp").FirstOrDefault(x => PlaceholderOf(x)?.Type == "body");
        var paragraphList = Child(body, "txBody")?.Elements().Where(x => x.LocalName == "p")
            .Select(PlainText)
            .ToList();
        if (paragraphList is null || paragraphList.All(string.IsNullOrWhiteSpace)) return string.Empty;

        var builder = new StringBuilder("<details class=\"ppt-notes\"><summary>Ghi chú của slide</summary>");
        foreach (var paragraph in paragraphList)
            builder.Append("<p>").Append(paragraph.Length == 0 ? "&nbsp;" : WordHtmlConverter.HtmlEncode(paragraph)).Append("</p>");
        return builder.Append("</details>").ToString();
    }

    /// <summary>Tên bài học: dòng tiêu đề của slide đầu tiên — bài giảng hầu như luôn mở đầu bằng slide tên bài.</summary>
    private static string? TitleOf(OpenXmlElement? slideRoot)
    {
        var shape = SpTree(slideRoot)?.Descendants().Where(x => x.LocalName == "sp")
            .FirstOrDefault(x => PlaceholderOf(x)?.Type is "title" or "ctrTitle");
        var paragraphList = Child(shape, "txBody")?.Elements().Where(x => x.LocalName == "p")
            .Select(x => PlainText(x).Trim())
            .Where(x => x.Length > 0)
            .ToList();
        if (paragraphList is null || paragraphList.Count == 0) return null;

        var title = string.Join(" ", paragraphList);
        return title.Length > 250 ? title[..250].Trim() : title;
    }

    private static string PlainText(OpenXmlElement paragraph) =>
        string.Concat(paragraph.Descendants().Where(x => x.LocalName == "t").Select(x => x.InnerText));

    // ==================================================================
    //  PLACEHOLDER
    // ==================================================================

    private static Placeholder? PlaceholderOf(OpenXmlElement shape)
    {
        var nonVisual = shape.Elements().FirstOrDefault(x => x.LocalName.StartsWith("nv", StringComparison.Ordinal));
        var placeholder = nonVisual?.Descendants().FirstOrDefault(x => x.LocalName == "ph");
        // Theo đặc tả, placeholder không ghi type là loại "obj" (ô nội dung).
        return placeholder is null ? null : new Placeholder(Attr(placeholder, "type") ?? "obj", Attr(placeholder, "idx"));
    }

    /// <summary>Nhóm loại placeholder: tiêu đề chính và tiêu đề trang bìa cùng kế thừa kiểu tiêu đề; các ô
    /// nội dung cùng kế thừa kiểu thân bài.</summary>
    private static string Family(string type) => type switch
    {
        "title" or "ctrTitle" => "title",
        "body" or "subTitle" or "obj" or "pic" or "tbl" or "chart" or "dgm" or "media" or "clipArt" => "body",
        _ => type
    };

    private static (OpenXmlElement? Layout, OpenXmlElement? Master) FindInheritedPlaceholder(Placeholder? placeholder, SlideScope scope)
    {
        if (placeholder is null) return (null, null);

        var layout = FindPlaceholder(scope.Layout, placeholder, true);
        var master = FindPlaceholder(scope.Master, layout is null ? placeholder : PlaceholderOf(layout) ?? placeholder, false);
        return (layout, master);
    }

    /// <summary>Ô cùng loại bên layout / master. Bên layout ưu tiên khớp theo số hiệu (idx) — một layout hai
    /// cột có hai ô nội dung cùng loại, chỉ số hiệu mới phân biệt được; bên master thì khớp theo loại.</summary>
    private static OpenXmlElement? FindPlaceholder(OpenXmlPart? part, Placeholder key, bool matchIndex)
    {
        var tree = SpTree(part?.RootElement);
        if (tree is null) return null;

        var list = tree.Descendants()
            .Where(x => x.LocalName is "sp" or "pic")
            .Select(x => (Shape: x, Key: PlaceholderOf(x)))
            .Where(x => x.Key is not null)
            .ToList();

        if (matchIndex && key.Index is not null
            && list.FirstOrDefault(x => x.Key!.Index == key.Index).Shape is { } byIndex)
            return byIndex;

        var family = Family(key.Type);
        return list.FirstOrDefault(x => x.Key!.Type == key.Type).Shape
               ?? list.FirstOrDefault(x => Family(x.Key!.Type) == family).Shape;
    }

    /// <summary>Kiểu chữ chung của master (txStyles) ứng với loại placeholder. Hộp chữ thường (không phải
    /// placeholder) không dùng txStyles mà dùng kiểu chữ mặc định của bài giảng.</summary>
    private static OpenXmlElement? MasterTextStyle(SlideScope scope, Placeholder? placeholder)
    {
        if (placeholder is null) return null;

        var textStyleList = Child(scope.Master?.RootElement, "txStyles");
        return Family(placeholder.Type) switch
        {
            "title" => Child(textStyleList, "titleStyle"),
            "body" => Child(textStyleList, "bodyStyle"),
            _ => Child(textStyleList, "otherStyle")
        };
    }

    private static OpenXmlElement? SpTree(OpenXmlElement? root) => Child(Child(root, "cSld"), "spTree");

    // ==================================================================
    //  VỊ TRÍ VÀ HÌNH DẠNG
    // ==================================================================

    private static Frame? ReadFrame(OpenXmlElement? transform)
    {
        var offset = Child(transform, "off");
        var extent = Child(transform, "ext");
        if (offset is null || extent is null) return null;

        return new Frame(
            Number(Attr(offset, "x")) ?? 0,
            Number(Attr(offset, "y")) ?? 0,
            Number(Attr(extent, "cx")) ?? 0,
            Number(Attr(extent, "cy")) ?? 0,
            (Number(Attr(transform, "rot")) ?? 0) / 60000,
            Attr(transform, "flipH") is "1" or "true",
            Attr(transform, "flipV") is "1" or "true");
    }

    private static string BoxCss(Frame box, Mapper mapper, DeckContext deck, bool applyFlip)
    {
        var css = "left:" + deck.PercentX(mapper.MapX(box.X)) + ";top:" + deck.PercentY(mapper.MapY(box.Y))
                  + ";width:" + deck.PercentX(box.Width * mapper.ScaleX) + ";height:" + deck.PercentY(box.Height * mapper.ScaleY);

        var transformList = new List<string>();
        if (Math.Abs(box.Rotation) > 0.01) transformList.Add("rotate(" + Format(box.Rotation) + "deg)");
        if (applyFlip && FlipCss(box) is string flip) transformList.Add(flip);

        return transformList.Count == 0 ? css : css + ";transform:" + string.Join(' ', transformList);
    }

    private static string? FlipCss(Frame box) => (box.FlipH, box.FlipV) switch
    {
        (true, true) => "scale(-1,-1)",
        (true, false) => "scaleX(-1)",
        (false, true) => "scaleY(-1)",
        _ => null
    };

    /// <summary>Hình dạng của lớp nền. Các hình thông dụng trong bài giảng (bo góc, elip, tam giác, mũi tên,
    /// chevron...) dựng bằng border-radius / clip-path; hình lạ hơn giữ dạng chữ nhật — vẫn đúng vị trí, đúng
    /// màu, chữ bên trong vẫn đọc được.</summary>
    private static string? GeometryCss(string geometry, OpenXmlElement? presetGeometry, Frame box, Mapper mapper, DeckContext deck)
    {
        var shortSide = Math.Min(box.Width * mapper.ScaleX, box.Height * mapper.ScaleY);

        return geometry switch
        {
            "ellipse" or "flowChartConnector" => "border-radius:50%",
            "roundRect" or "flowChartAlternateProcess" => "border-radius:" + deck.Cq(shortSide * AdjustValue(presetGeometry, 16667)),
            "flowChartTerminator" => "border-radius:" + deck.Cq(shortSide / 2),
            "triangle" => "clip-path:polygon(50% 0,100% 100%,0 100%)",
            "rtTriangle" => "clip-path:polygon(0 0,100% 100%,0 100%)",
            "diamond" or "flowChartDecision" => "clip-path:polygon(50% 0,100% 50%,50% 100%,0 50%)",
            "parallelogram" or "flowChartInputOutput" => "clip-path:polygon(25% 0,100% 0,75% 100%,0 100%)",
            "trapezoid" => "clip-path:polygon(25% 0,75% 0,100% 100%,0 100%)",
            "hexagon" => "clip-path:polygon(25% 0,75% 0,100% 50%,75% 100%,25% 100%,0 50%)",
            "octagon" => "clip-path:polygon(29% 0,71% 0,100% 29%,100% 71%,71% 100%,29% 100%,0 71%,0 29%)",
            "pentagon" => "clip-path:polygon(50% 0,100% 38%,82% 100%,18% 100%,0 38%)",
            "homePlate" => "clip-path:polygon(0 0,80% 0,100% 50%,80% 100%,0 100%)",
            "chevron" => "clip-path:polygon(0 0,80% 0,100% 50%,80% 100%,0 100%,20% 50%)",
            "rightArrow" => "clip-path:polygon(0 25%,70% 25%,70% 0,100% 50%,70% 100%,70% 75%,0 75%)",
            "leftArrow" => "clip-path:polygon(100% 25%,30% 25%,30% 0,0 50%,30% 100%,30% 75%,100% 75%)",
            "upArrow" => "clip-path:polygon(25% 100%,25% 30%,0 30%,50% 0,100% 30%,75% 30%,75% 100%)",
            "downArrow" => "clip-path:polygon(25% 0,75% 0,75% 70%,100% 70%,50% 100%,0 70%,25% 70%)",
            "plus" or "mathPlus" => "clip-path:polygon(35% 0,65% 0,65% 35%,100% 35%,100% 65%,65% 65%,65% 100%,35% 100%,35% 65%,0 65%,0 35%,35% 35%)",
            _ => null
        };
    }

    /// <summary>Tham số điều chỉnh (avLst) của hình dựng sẵn, ví dụ độ bo góc của hình chữ nhật bo góc.</summary>
    private static double AdjustValue(OpenXmlElement? presetGeometry, double fallback)
    {
        var guide = Child(presetGeometry, "avLst")?.Elements().FirstOrDefault(x => x.LocalName == "gd");
        var formula = Attr(guide, "fmla");
        var value = formula is not null && formula.StartsWith("val ", StringComparison.Ordinal)
            ? Number(formula[4..])
            : null;
        return (value ?? fallback) / 100000;
    }

    // ==================================================================
    //  MÀU, NỀN, VIỀN
    // ==================================================================

    /// <summary>
    /// Khai báo nền dạng CSS ("background:..."). Trả về chuỗi rỗng khi file CỐ Ý không tô (noFill), null khi
    /// không khai báo gì — hai trường hợp phải tách: không khai báo thì còn lấy theo kiểu hình, cố ý không tô
    /// thì không.
    /// </summary>
    private async Task<string?> FillCssAsync(OpenXmlElement? holder, OpenXmlPart owner, SlideScope scope)
    {
        foreach (var child in holder?.Elements() ?? Enumerable.Empty<OpenXmlElement>())
        {
            switch (child.LocalName)
            {
                case "noFill":
                    return string.Empty;

                case "solidFill":
                    return ColorOf(child, scope) is string color ? "background:" + color : string.Empty;

                case "gradFill":
                    return GradientCss(child, scope) is string gradient ? "background:" + gradient : null;

                case "pattFill":
                    return ColorOf(Child(child, "fgClr"), scope) is string pattern ? "background:" + pattern : null;

                case "blipFill":
                    var url = await ImageUrlAsync(owner, RelationshipId(Child(child, "blip"), "embed"), scope.Deck);
                    if (url is null) return null;
                    var size = Child(child, "tile") is not null ? "auto" : "100% 100%";
                    var repeat = size == "auto" ? "repeat" : "no-repeat";
                    return "background:url('" + WordHtmlConverter.HtmlEncode(url) + "') center/" + size + " " + repeat;
            }
        }

        return null;
    }

    private static string? GradientCss(OpenXmlElement gradient, SlideScope scope)
    {
        var stopList = Child(gradient, "gsLst")?.Elements().Where(x => x.LocalName == "gs")
            .Select(x => (Position: Number(Attr(x, "pos")) ?? 0, Color: ColorOf(x, scope)))
            .Where(x => x.Color is not null)
            .OrderBy(x => x.Position)
            .ToList();
        if (stopList is null || stopList.Count == 0) return null;
        if (stopList.Count == 1) return stopList[0].Color;

        var stopCss = string.Join(',', stopList.Select(x => x.Color + " " + Format(x.Position / 1000) + "%"));
        if (Child(gradient, "path") is not null) return "radial-gradient(" + stopCss + ")";

        // Góc của PowerPoint: 0 = trái sang phải, đơn vị 1/60000 độ. CSS: 0deg = dưới lên trên.
        var angle = (Number(Attr(Child(gradient, "lin"), "ang")) ?? 0) / 60000 + 90;
        return "linear-gradient(" + Format(angle) + "deg," + stopCss + ")";
    }

    /// <summary>Viền dạng "độ dày kiểu màu". Rỗng = cố ý không viền, null = không khai báo.</summary>
    private static string? LineValue(OpenXmlElement? line, SlideScope scope)
    {
        if (line is null) return null;
        if (Child(line, "noFill") is not null) return string.Empty;

        var color = ColorOf(Child(line, "solidFill"), scope)
                    ?? ColorOf(Child(Child(line, "gradFill"), "gsLst")?.Elements().FirstOrDefault(), scope);
        if (color is null) return null;

        var width = Math.Max(Number(Attr(line, "w")) ?? 9525, 3175);
        return scope.Deck.Cq(width) + " " + DashCss(line) + " " + color;
    }

    private static string DashCss(OpenXmlElement? line) => Attr(Child(line, "prstDash"), "val") switch
    {
        null or "solid" => "solid",
        "dot" or "sysDot" => "dotted",
        _ => "dashed"
    };

    /// <summary>Màu tham chiếu từ "kiểu hình" (fillRef / lnRef). idx = 0 nghĩa là kiểu hình không tô.</summary>
    private static string? StyleColor(OpenXmlElement? style, string referenceName, SlideScope scope)
    {
        var reference = Child(style, referenceName);
        if (reference is null || (Integer(Attr(reference, "idx")) ?? 0) == 0) return null;
        return ColorOf(reference, scope);
    }

    /// <summary>
    /// Màu CSS từ phần tử con mô tả màu của <paramref name="holder"/>. Áp đủ các phép biến đổi màu PowerPoint
    /// hay dùng: lumMod / lumOff (mọi màu "Lighter 40%", "Darker 25%" trong bảng chọn màu đều là hai phép này),
    /// tint, shade, satMod, alpha.
    /// </summary>
    private static string? ColorOf(OpenXmlElement? holder, SlideScope scope)
    {
        var element = holder?.Elements().FirstOrDefault(x =>
            x.LocalName is "srgbClr" or "schemeClr" or "sysClr" or "prstClr" or "scrgbClr");
        if (element is null) return null;

        var hex = element.LocalName switch
        {
            "srgbClr" => Attr(element, "val"),
            "sysClr" => Attr(element, "lastClr") ?? (Attr(element, "val") == "window" ? "FFFFFF" : "000000"),
            "schemeClr" => scope.Scheme(Attr(element, "val")),
            "prstClr" => PresetColor(Attr(element, "val")),
            "scrgbClr" => ScRgbHex(element),
            _ => null
        };
        if (!IsHex6(hex)) return null;

        var red = Convert.ToInt32(hex![..2], 16) / 255.0;
        var green = Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0;
        var blue = Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0;
        var alpha = 1.0;

        foreach (var modifier in element.Elements())
        {
            var value = (Number(Attr(modifier, "val")) ?? 0) / 100000;
            switch (modifier.LocalName)
            {
                case "alpha":
                    alpha = Math.Clamp(value, 0, 1);
                    break;
                case "tint":
                    red = 1 - value * (1 - red);
                    green = 1 - value * (1 - green);
                    blue = 1 - value * (1 - blue);
                    break;
                case "shade":
                    red *= value;
                    green *= value;
                    blue *= value;
                    break;
                case "lumMod" or "lumOff" or "satMod":
                {
                    var (hue, saturation, lightness) = ToHsl(red, green, blue);
                    if (modifier.LocalName == "lumMod") lightness *= value;
                    else if (modifier.LocalName == "lumOff") lightness += value;
                    else saturation *= value;
                    (red, green, blue) = FromHsl(hue, Math.Clamp(saturation, 0, 1), Math.Clamp(lightness, 0, 1));
                    break;
                }
            }
        }

        var r = (int)Math.Round(Math.Clamp(red, 0, 1) * 255);
        var g = (int)Math.Round(Math.Clamp(green, 0, 1) * 255);
        var b = (int)Math.Round(Math.Clamp(blue, 0, 1) * 255);

        return alpha < 0.999
            ? "rgba(" + r + "," + g + "," + b + "," + Format(alpha) + ")"
            : "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
    }

    /// <summary>Màu hex pha với trắng — dùng cho các dải màu nhạt của bảng.</summary>
    private static string Tint(string hex, double amount)
    {
        if (!IsHex6(hex)) return "#FFFFFF";
        var channelList = new[] { hex[..2], hex.Substring(2, 2), hex.Substring(4, 2) }
            .Select(x => (int)Math.Round(255 - amount * (255 - Convert.ToInt32(x, 16))));
        return "#" + string.Concat(channelList.Select(x => x.ToString("X2")));
    }

    private static (double Hue, double Saturation, double Lightness) ToHsl(double red, double green, double blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var lightness = (max + min) / 2;
        if (max - min < 1e-9) return (0, 0, lightness);

        var delta = max - min;
        var saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        var hue = max == red ? (green - blue) / delta + (green < blue ? 6 : 0)
                : max == green ? (blue - red) / delta + 2
                : (red - green) / delta + 4;
        return (hue / 6, saturation, lightness);
    }

    private static (double Red, double Green, double Blue) FromHsl(double hue, double saturation, double lightness)
    {
        if (saturation < 1e-9) return (lightness, lightness, lightness);

        var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var p = 2 * lightness - q;
        return (Channel(hue + 1.0 / 3), Channel(hue), Channel(hue - 1.0 / 3));

        double Channel(double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }

    private static string? ScRgbHex(OpenXmlElement element)
    {
        string Channel(string name) =>
            ((int)Math.Round(Math.Clamp((Number(Attr(element, name)) ?? 0) / 100000, 0, 1) * 255)).ToString("X2");
        return Channel("r") + Channel("g") + Channel("b");
    }

    private static string? PresetColor(string? name) => name switch
    {
        "black" => "000000",
        "white" => "FFFFFF",
        "red" => "FF0000",
        "green" => "008000",
        "blue" => "0000FF",
        "yellow" => "FFFF00",
        "orange" => "FFA500",
        "gray" or "grey" => "808080",
        "darkGray" or "dkGray" => "A9A9A9",
        "lightGray" or "ltGray" => "D3D3D3",
        "navy" => "000080",
        "purple" => "800080",
        _ => null
    };

    private static bool IsHex6(string? value) => value is { Length: 6 } && value.All(Uri.IsHexDigit);

    // ==================================================================
    //  GÓI FILE
    // ==================================================================

    /// <summary>Ảnh trong gói: lưu ra đĩa một lần, trả về đường dẫn. null nếu không lưu được.</summary>
    private static async Task<string?> ImageUrlAsync(OpenXmlPart owner, string? relationshipId, DeckContext deck)
    {
        if (relationshipId is null || TryGetPart(owner, relationshipId) is not ImagePart imagePart) return null;

        var cacheKey = imagePart.Uri.ToString();
        if (deck.ImageCache.TryGetValue(cacheKey, out var cached)) return cached;

        var extension = imagePart.ContentType switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/webp" => ".webp",
            "image/jpeg" or "image/jpg" => ".jpg",
            // emf / wmf / tiff: trình duyệt không hiển thị được, lưu ra chỉ thành một ô ảnh vỡ.
            _ => null
        };

        string? url = null;
        if (extension is not null && deck.ImageCount < MaxImage)
        {
            await using var partStream = imagePart.GetStream();
            using var buffer = new MemoryStream();
            await partStream.CopyToAsync(buffer);

            try
            {
                url = await deck.SaveImageAsync(buffer.ToArray(), extension);
            }
            catch
            {
                // Một tấm ảnh không lưu được không đáng để đánh hỏng cả bài giảng — bỏ tấm đó.
                url = null;
            }

            if (string.IsNullOrEmpty(url)) url = null;
            else deck.ImageCount++;
        }

        deck.ImageCache[cacheKey] = url;
        return url;
    }

    private static OpenXmlPart? TryGetPart(OpenXmlPartContainer owner, string relationshipId)
    {
        try
        {
            return owner.GetPartById(relationshipId);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Quan hệ trỏ tới phần không tồn tại — file hỏng nhẹ, bỏ qua đối tượng đó.
            return null;
        }
    }

    /// <summary>Mã quan hệ (r:id, r:embed...). Phải lọc theo không gian tên: thẻ sldId có cả thuộc tính "id"
    /// thường (số thứ tự) lẫn "r:id" (mã quan hệ) trùng tên cục bộ.</summary>
    private static string? RelationshipId(OpenXmlElement? element, string localName) =>
        element?.GetAttributes()
            .FirstOrDefault(x => x.LocalName == localName
                                 && x.NamespaceUri.EndsWith("/relationships", StringComparison.Ordinal))
            .Value;

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
