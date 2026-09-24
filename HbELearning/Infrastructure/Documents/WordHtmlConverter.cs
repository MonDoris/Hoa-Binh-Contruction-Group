// File: Infrastructure/Documents/WordHtmlConverter.cs
using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace HbELearning.Infrastructure.Documents;

public class WordHtmlConverter : IWordHtmlConverter
{
    /// <summary>Trần số ảnh trích ra từ một tài liệu. Một file Word chèn ảnh nền lặp lại có thể chứa hàng
    /// trăm ảnh giống nhau; không chặn thì một lần nhập ghi đầy đĩa.</summary>
    private const int MaxImage = 200;

    /// <summary>Trần độ sâu lồng nhau của bảng và hộp văn bản. Một file .docx dựng cố ý có thể lồng vài
    /// nghìn cấp; không chặn thì bộ chuyển đổi tự gọi mình đến tràn ngăn xếp và sập cả tiến trình.</summary>
    private const int MaxNestingDepth = 12;

    /// <summary>Bề ngang phần chữ của trang A4 lề 2,54cm, tính bằng point. Dùng khi tài liệu không khai báo
    /// khổ giấy — mọi độ rộng bảng ghi bằng đơn vị tuyệt đối đều phải quy ra phần trăm của con số này.</summary>
    private const double DefaultPageContentWidthPt = 451.3;

    public async Task<WordHtmlResult> ConvertAsync(Stream docxStream, Func<byte[], string, Task<string>> saveImageAsync)
    {
        using var document = await OpenAsync(docxStream);
        var mainPart = document.MainDocumentPart;
        var body = mainPart?.Document?.Body;
        if (mainPart is null || body is null)
            return new WordHtmlResult(string.Empty, null, 0);

        var context = CreateContext(mainPart, body, saveImageAsync);
        var builder = new StringBuilder();

        builder.Append(DocumentOpenTag(context));
        await WriteBlockListAsync(builder, body.Elements<OpenXmlElement>(), context);
        CloseAllList(builder, context);
        builder.Append("</div>");

        return new WordHtmlResult(builder.ToString(), context.Title, context.ImageCount);
    }

    public async Task<WordSectionResult> ConvertSectionsAsync(Stream docxStream, Func<byte[], string, Task<string>> saveImageAsync)
    {
        using var document = await OpenAsync(docxStream);
        var mainPart = document.MainDocumentPart;
        var body = mainPart?.Document?.Body;
        if (mainPart is null || body is null)
            return new WordSectionResult(Array.Empty<WordSection>(), 0, "none");

        var context = CreateContext(mainPart, body, saveImageAsync);
        var (splitParagraphSet, rule) = WordSectionDetector.Detect(body, context.Styles);
        context.SplitAt = splitParagraphSet;

        var builder = new StringBuilder(DocumentOpenTag(context));
        await WriteBlockListAsync(builder, body.Elements<OpenXmlElement>(), context);
        CloseAllList(builder, context);
        builder.Append("</div>");

        // Phần cuối cùng (hoặc cả tài liệu, khi không tách được) chưa ai đóng — đóng ở đây.
        CloseSection(builder, context);

        // Phần rỗng sinh ra khi tài liệu mở đầu ngay bằng một dòng tiêu đề chia bài: bỏ đi, không thì bài
        // học đầu tiên là một bài trống.
        var sectionList = context.SectionList.Where(x => x.CharacterCount > 0 || x.ImageCount > 0).ToList();

        // Không tách được: cả tài liệu là một phần, tên lấy theo dòng tiêu đề đầu tiên như lượt nhập thường.
        if (sectionList.Count == 1 && sectionList[0].Title is null)
            sectionList[0] = sectionList[0] with { Title = context.Title };

        return new WordSectionResult(sectionList, context.ImageCount, sectionList.Count > 1 ? rule : "none");
    }

    private static async Task<WordprocessingDocument> OpenAsync(Stream docxStream)
    {
        // OpenXml cần đọc tới lui trên stream; stream của IFormFile qua proxy đôi khi chỉ đọc tiến được.
        var memory = new MemoryStream();
        await docxStream.CopyToAsync(memory);
        memory.Position = 0;
        return WordprocessingDocument.Open(memory, false);
    }

    private static ConvertContext CreateContext(
        MainDocumentPart mainPart, Body body, Func<byte[], string, Task<string>> saveImageAsync) =>
        new(mainPart, saveImageAsync)
        {
            Styles = WordStyleSheet.Load(mainPart),
            Numbering = WordNumbering.Load(mainPart),
            PageContentWidthPt = PageContentWidthOf(body)
        };

    /// <summary>Bọc cả tài liệu trong một khung mang phông chữ và cỡ chữ mặc định của chính file Word đó.
    /// Không có khung này thì mọi đoạn không tự khai báo phông sẽ mượn phông của giao diện web và tài liệu đọc
    /// ra khác hẳn bản gốc ngay từ dòng đầu. Tách bài thì MỖI phần đều phải có khung riêng của nó.</summary>
    private static string DocumentOpenTag(ConvertContext context) =>
        "<div class=\"word-doc\"" + StyleAttribute(DocumentCss(context)) + ">";

    /// <summary>Chốt lại phần đang dựng dở thành một mục trong danh sách, rồi mở khung cho phần kế tiếp.</summary>
    private static void CloseSection(StringBuilder builder, ConvertContext context)
    {
        var html = builder.ToString();
        builder.Clear();
        builder.Append(DocumentOpenTag(context));

        context.SectionList.Add(new WordSection(
            context.SectionTitle, html, StripTag(html).Length, context.SectionImageCount));
        context.SectionImageCount = 0;
    }

    /// <summary>Trạng thái dùng chung trong một lần chuyển đổi.</summary>
    private sealed class ConvertContext
    {
        public MainDocumentPart MainPart { get; }
        public Func<byte[], string, Task<string>> SaveImageAsync { get; }
        public WordStyleSheet Styles { get; init; } = null!;
        public WordNumbering Numbering { get; init; } = null!;
        public double PageContentWidthPt { get; init; }

        public string? Title { get; set; }
        public int ImageCount { get; set; }
        public int Depth { get; set; }

        /// <summary>Các đoạn mở đầu một bài học mới (chỉ có khi đang tách bài). null = không tách.</summary>
        public HashSet<OpenXmlElement>? SplitAt { get; set; }

        /// <summary>Các phần đã chốt xong, theo thứ tự trong tài liệu.</summary>
        public List<WordSection> SectionList { get; } = new();

        /// <summary>Tên của phần đang dựng dở — null với phần nằm trước dòng tiêu đề chia bài đầu tiên.</summary>
        public string? SectionTitle { get; set; }

        /// <summary>Số ảnh GẶP trong phần đang dựng dở — đếm cả ảnh không lưu được, xem WordSection.ImageCount.</summary>
        public int SectionImageCount { get; set; }

        /// <summary>Các danh sách đang mở, từ cấp ngoài cùng vào trong.</summary>
        public List<OpenList> ListStack { get; } = new();

        /// <summary>Khối phải đặt SAU đoạn văn đang dựng (hộp văn bản). Hộp văn bản trong Word nằm bên
        /// trong một đoạn, nhưng nội dung nó lại là cả đoạn văn và bảng — nhét div vào giữa thẻ p là HTML
        /// sai và trình duyệt sẽ tự cắt đoạn văn làm đôi, nên gom lại đổ ra sau khi đóng thẻ p.</summary>
        public List<string> PendingBlockList { get; } = new();

        public ConvertContext(MainDocumentPart mainPart, Func<byte[], string, Task<string>> saveImageAsync)
        {
            MainPart = mainPart;
            SaveImageAsync = saveImageAsync;
        }
    }

    private sealed record OpenList(string Tag, int Level, int NumberingId);

    // ==================================================================
    //  KHỐI: ĐOẠN VĂN / BẢNG
    // ==================================================================

    private async Task WriteBlockListAsync(
        StringBuilder builder, IEnumerable<OpenXmlElement> elementList, ConvertContext context)
    {
        foreach (var element in elementList)
        {
            switch (element.LocalName)
            {
                case "p":
                    if (element is Paragraph paragraph) await WriteParagraphAsync(builder, paragraph, context);
                    break;

                case "tbl":
                    if (element is Table table)
                    {
                        CloseAllList(builder, context);
                        await WriteTableAsync(builder, table, context);
                    }
                    break;

                // Nội dung nằm trong "điều khiển nội dung" (mục lục tự động, trang bìa, ô chọn ngày...).
                // Bản thân cái vỏ không hiện gì; phần hiện ra nằm trong sdtContent nên phải bóc vỏ đi.
                case "sdt":
                    var sdtContent = WordValue.Child(element, "sdtContent");
                    if (sdtContent is not null)
                        await WriteBlockListAsync(builder, sdtContent.Elements<OpenXmlElement>(), context);
                    break;

                // Đoạn được thêm vào khi bật theo dõi thay đổi: hiện như đã đồng ý sửa. Phần bị xoá (w:del)
                // cố ý không rơi vào nhánh nào — tài liệu nhập vào là bản sạch, không phải bản đang sửa.
                case "ins":
                    await WriteBlockListAsync(builder, element.Elements<OpenXmlElement>(), context);
                    break;
            }
        }
    }

    private async Task WriteParagraphAsync(StringBuilder builder, Paragraph paragraph, ConvertContext context)
    {
        // Đoạn mở đầu một bài học mới: chốt phần đang dựng dở lại rồi mở phần mới, chính đoạn này là dòng
        // tiêu đề đầu tiên của phần mới. Chỉ cắt ở cấp ngoài cùng (Depth == 0): một dòng tiêu đề nằm trong ô
        // bảng hay hộp văn bản mà cắt ngang thì phần trước hở thẻ, phần sau thừa thẻ đóng.
        if (context.Depth == 0 && context.SplitAt?.Contains(paragraph) == true)
        {
            CloseAllList(builder, context);
            builder.Append("</div>");
            CloseSection(builder, context);
            context.SectionTitle = WordSectionDetector.NormalizeTitle(paragraph.InnerText);
        }

        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

        // Ba tầng định dạng của Word, gộp từ dưới lên: mặc định tài liệu -> style của đoạn -> định dạng gán
        // thẳng vào đoạn.
        var paragraphFormat = context.Styles.DefaultParagraph
            .Override(context.Styles.ParagraphFormatOfStyle(styleId))
            .Override(WordStyleSheet.ReadParagraphFormat(paragraph.ParagraphProperties));

        // Định dạng chữ "nền" của đoạn: đặt ở thẻ p, để từng đoạn chữ bên trong chỉ phải ghi phần KHÁC nó.
        // Nhờ vậy một đoạn 50 chữ cùng phông không sinh ra 50 thẻ span giống hệt nhau.
        var baseRun = context.Styles.DefaultRun.Override(context.Styles.RunFormatOfStyle(styleId));

        var inner = await BuildInlineAsync(paragraph, baseRun, context);
        var isEmpty = inner.Trim().Length == 0;

        var listLevel = paragraphFormat.NumberingId is int numberingId
            ? context.Numbering.LevelOf(numberingId, paragraphFormat.NumberingLevel ?? 0)
            : null;

        if (isEmpty)
        {
            // Đoạn rỗng vẫn phải giữ: người soạn dùng dòng trắng để giãn cách, bỏ đi thì cả tài liệu dồn cục.
            // Nhưng đoạn rỗng NGAY TRONG danh sách thì bỏ, không thì sinh ra một chấm đầu dòng trống.
            if (context.ListStack.Count == 0 && listLevel is null)
                builder.Append("<p").Append(StyleAttribute(ParagraphCss(paragraphFormat, baseRun, false)))
                       .Append(">&nbsp;</p>");
            FlushPendingBlock(builder, context);
            return;
        }

        if (listLevel is not null)
        {
            OpenListTo(builder, context, listLevel, paragraphFormat.NumberingId!.Value,
                paragraphFormat.NumberingLevel ?? 0);

            builder.Append("<li").Append(StyleAttribute(ParagraphCss(paragraphFormat, baseRun, true))).Append('>')
                   .Append(inner);
            // Cố ý CHƯA đóng thẻ li: mục con của nó (nếu có) phải nằm bên trong, đóng sớm là danh sách con
            // rơi ra ngoài thành một danh sách riêng.
            FlushPendingBlock(builder, context);
            return;
        }

        CloseAllList(builder, context);

        var headingLevel = context.Styles.HeadingLevelOf(paragraph);
        var css = ParagraphCss(paragraphFormat, baseRun, false);

        if (headingLevel is int level)
        {
            // Dồn về h2..h4: bài học đã có tiêu đề riêng ở khung ngoài, để h1 bên trong nội dung sẽ phá thứ
            // bậc tiêu đề của cả trang.
            var tag = "h" + Math.Min(level + 1, 4);

            // Tiêu đề cấp cao nhất gặp đầu tiên được dùng làm tên bài học — tài liệu đào tạo hầu như luôn
            // mở đầu bằng đúng tên bài, người nhập khỏi phải gõ lại.
            context.Title ??= StripTag(inner);

            builder.Append('<').Append(tag).Append(StyleAttribute(css)).Append('>')
                   .Append(inner).Append("</").Append(tag).Append('>');
            FlushPendingBlock(builder, context);
            return;
        }

        builder.Append("<p").Append(StyleAttribute(css)).Append('>').Append(inner).Append("</p>");
        FlushPendingBlock(builder, context);
    }

    private static void FlushPendingBlock(StringBuilder builder, ConvertContext context)
    {
        if (context.PendingBlockList.Count == 0) return;

        foreach (var block in context.PendingBlockList) builder.Append(block);
        context.PendingBlockList.Clear();
    }

    // ==================================================================
    //  DANH SÁCH NHIỀU CẤP
    // ==================================================================

    /// <summary>
    /// Đưa ngăn xếp danh sách về đúng cấp của mục sắp ghi rồi mở thẻ li mới.
    ///
    /// Word không lồng danh sách vào nhau — nó xếp mọi mục thành một dãy đoạn văn phẳng, mỗi đoạn chỉ mang
    /// một con số "cấp mấy". Muốn ra được ul/ol lồng nhau đúng như bản gốc thì phải tự dựng lại cấu trúc
    /// cây từ dãy phẳng đó, và đó là việc của hàm này.
    /// </summary>
    private static void OpenListTo(
        StringBuilder builder, ConvertContext context, WordListLevel level, int numberingId, int levelIndex)
    {
        var tag = level.IsOrdered ? "ol" : "ul";

        // Đóng mọi cấp sâu hơn cấp cần tới. Đóng theo thứ tự </li></ul> vì thẻ li của cấp CHA vẫn đang mở và
        // đang bọc lấy danh sách con này.
        while (context.ListStack.Count > 0 && context.ListStack[^1].Level > levelIndex)
        {
            builder.Append("</li></").Append(context.ListStack[^1].Tag).Append('>');
            context.ListStack.RemoveAt(context.ListStack.Count - 1);
        }

        var top = context.ListStack.Count > 0 ? context.ListStack[^1] : null;

        // Mở một cấp sâu hơn: KHÔNG đóng thẻ li của cấp cha, danh sách con nằm gọn trong nó.
        if (top is null || top.Level < levelIndex)
        {
            AppendOpenTag();
            return;
        }

        // Cùng cấp nhưng đổi kiểu danh sách hoặc đổi sang bộ đánh số khác: phải đóng hẳn danh sách cũ, không
        // thì một danh sách đánh số lại chui vào bụng danh sách gạch đầu dòng phía trên.
        if (top.Tag != tag || top.NumberingId != numberingId)
        {
            builder.Append("</li></").Append(top.Tag).Append('>');
            context.ListStack.RemoveAt(context.ListStack.Count - 1);
            AppendOpenTag();
            return;
        }

        builder.Append("</li>");

        void AppendOpenTag()
        {
            builder.Append('<').Append(tag);

            // Người soạn bấm "đánh số lại từ..." thì danh sách không bắt đầu từ 1; bỏ mất chỗ này là bản
            // nhập vào đánh số khác hẳn bản gốc.
            if (level.IsOrdered && level.Start != 1) builder.Append(" start=\"").Append(level.Start).Append('"');

            builder.Append(" style=\"list-style-type:").Append(level.CssListStyle).Append("\">");
            context.ListStack.Add(new OpenList(tag, levelIndex, numberingId));
        }
    }

    private static void CloseAllList(StringBuilder builder, ConvertContext context)
    {
        for (var index = context.ListStack.Count - 1; index >= 0; index--)
            builder.Append("</li></").Append(context.ListStack[index].Tag).Append('>');

        context.ListStack.Clear();
    }

    // ==================================================================
    //  BẢNG
    // ==================================================================

    private async Task WriteTableAsync(StringBuilder builder, Table table, ConvertContext context)
    {
        if (context.Depth >= MaxNestingDepth) return;
        context.Depth++;

        var tablePropertyElement = WordValue.Child(table, "tblPr");
        var styleId = WordValue.Attr(WordValue.Child(tablePropertyElement, "tblStyle"), "val");

        // Đường kẻ có thể nằm ở chính cái bảng, hoặc chỉ nằm trong style bảng mà nó dùng ("Table Grid" của
        // Word là trường hợp phổ biến nhất) — thiếu nhánh style thì bảng nhập vào mất sạch đường kẻ.
        var borderElement = WordValue.Child(tablePropertyElement, "tblBorders")
                            ?? context.Styles.TableStyleChildOf(styleId, "tblBorders");
        var border = new TableBorderSet(borderElement);

        var cellMargin = WordValue.Child(tablePropertyElement, "tblCellMar")
                         ?? context.Styles.TableStyleChildOf(styleId, "tblCellMar");
        var cellPadding = CellPaddingCss(cellMargin);

        var columnWidthList = ColumnWidthListOf(table);
        var columnCount = columnWidthList.Count;

        var cssList = new List<string> { "border-collapse:collapse" };
        if (TableWidthCss(tablePropertyElement, context) is string width) cssList.Add("width:" + width);

        var justification = WordValue.Attr(WordValue.Child(tablePropertyElement, "jc"), "val");
        if (justification is "center") cssList.Add("margin-left:auto;margin-right:auto");
        else if (justification is "right" or "end") cssList.Add("margin-left:auto");

        builder.Append("<table class=\"doc-table\"").Append(StyleAttribute(string.Join(';', cssList))).Append('>');
        AppendColumnGroup(builder, columnWidthList);

        var rowList = table.Elements<TableRow>().ToList();
        var layout = BuildCellLayout(rowList);

        for (var rowIndex = 0; rowIndex < rowList.Count; rowIndex++)
        {
            var row = rowList[rowIndex];
            var isHeaderRow = WordValue.Child(WordValue.Child(row, "trPr"), "tblHeader") is not null;

            builder.Append("<tr>");

            foreach (var cell in layout[rowIndex])
            {
                if (cell.IsCoveredByMergeAbove) continue;

                var tag = isHeaderRow ? "th" : "td";
                builder.Append('<').Append(tag);
                if (cell.ColumnSpan > 1) builder.Append(" colspan=\"").Append(cell.ColumnSpan).Append('"');
                if (cell.RowSpan > 1) builder.Append(" rowspan=\"").Append(cell.RowSpan).Append('"');
                builder.Append(StyleAttribute(CellCss(
                    cell, rowIndex, rowList.Count, columnCount, border, cellPadding))).Append('>');

                // Ô bảng chứa được cả đoạn văn lẫn bảng lồng — dùng lại chính bộ chuyển đổi khối. Danh sách
                // đang mở ở ngoài phải được cất đi: một gạch đầu dòng bắt đầu trước bảng không được phép
                // "nuốt" nội dung trong ô.
                var outerList = context.ListStack.ToList();
                context.ListStack.Clear();

                await WriteBlockListAsync(builder, cell.Element.Elements<OpenXmlElement>(), context);
                CloseAllList(builder, context);

                context.ListStack.AddRange(outerList);
                builder.Append("</").Append(tag).Append('>');
            }

            builder.Append("</tr>");
        }

        builder.Append("</table>");
        context.Depth--;
    }

    /// <summary>Một ô sau khi đã tính xong vị trí thật của nó trên lưới bảng.</summary>
    private sealed class CellPlacement
    {
        public required TableCell Element { get; init; }
        public required int GridStart { get; init; }
        public required int ColumnSpan { get; init; }
        public int RowSpan { get; set; } = 1;
        public bool IsCoveredByMergeAbove { get; set; }
    }

    /// <summary>
    /// Tính chỗ đứng thật của từng ô trên lưới bảng, và quy đổi ô gộp DỌC của Word thành rowspan của HTML.
    ///
    /// Word không ghi "ô này cao 3 dòng". Nó ghi ở dòng đầu một dấu "bắt đầu gộp" rồi ở các dòng sau những ô
    /// rỗng mang dấu "gộp tiếp". Cứ dựng thẳng ra HTML thì bảng thừa hàng loạt ô trống và lệch hết cột — đây
    /// chính là chỗ hầu hết bộ chuyển đổi đơn giản làm hỏng bảng.
    /// </summary>
    private static List<List<CellPlacement>> BuildCellLayout(List<TableRow> rowList)
    {
        var result = new List<List<CellPlacement>>();

        // Ô gộp dọc đang mở, tra theo cột bắt đầu của nó.
        var openMerge = new Dictionary<int, CellPlacement>();

        foreach (var row in rowList)
        {
            var placementList = new List<CellPlacement>();

            // Word cho phép một dòng bắt đầu lệch sang phải (bảng cắt góc) bằng cách khai báo số ô bỏ trống.
            var column = WordValue.Integer(
                WordValue.Attr(WordValue.Child(WordValue.Child(row, "trPr"), "gridBefore"), "val")) ?? 0;

            foreach (var cell in row.Elements<TableCell>())
            {
                var cellProperty = WordValue.Child(cell, "tcPr");
                var span = Math.Max(1, WordValue.Integer(WordValue.Attr(WordValue.Child(cellProperty, "gridSpan"), "val")) ?? 1);

                var verticalMerge = WordValue.Child(cellProperty, "vMerge");
                // Có thẻ vMerge mà không ghi val thì mặc định là "gộp tiếp", theo đúng đặc tả của Word.
                var mergeKind = verticalMerge is null ? null : WordValue.Attr(verticalMerge, "val") ?? "continue";

                var placement = new CellPlacement { Element = cell, GridStart = column, ColumnSpan = span };

                if (mergeKind is "continue" && openMerge.TryGetValue(column, out var opened))
                {
                    opened.RowSpan++;
                    placement.IsCoveredByMergeAbove = true;
                }
                else if (mergeKind is "restart")
                {
                    openMerge[column] = placement;
                }
                else
                {
                    // Ô thường: chấm dứt chuỗi gộp dọc đang mở ở cột này (nếu có).
                    openMerge.Remove(column);
                }

                placementList.Add(placement);
                column += span;
            }

            result.Add(placementList);
        }

        return result;
    }

    /// <summary>Độ rộng từng cột theo lưới bảng, tính bằng twip.</summary>
    private static List<double> ColumnWidthListOf(Table table) =>
        WordValue.Child(table, "tblGrid")?.Elements()
            .Where(x => x.LocalName == "gridCol")
            .Select(x => WordValue.Number(WordValue.Attr(x, "w")) ?? 0)
            .ToList()
        ?? new List<double>();

    /// <summary>Khai báo độ rộng cột bằng colgroup. Dùng phần trăm chứ không dùng số đo tuyệt đối vì khung
    /// đọc bài học co giãn theo màn hình, còn bảng Word thì dựng theo bề ngang cố định của trang A4.</summary>
    private static void AppendColumnGroup(StringBuilder builder, List<double> columnWidthList)
    {
        var total = columnWidthList.Sum();
        if (columnWidthList.Count == 0 || total <= 0) return;

        builder.Append("<colgroup>");
        foreach (var width in columnWidthList)
            builder.Append("<col style=\"width:")
                   .Append((width / total * 100).ToString("0.##", CultureInfo.InvariantCulture))
                   .Append("%\">");
        builder.Append("</colgroup>");
    }

    private static string? TableWidthCss(OpenXmlElement? tablePropertyElement, ConvertContext context)
    {
        var widthElement = WordValue.Child(tablePropertyElement, "tblW");
        var value = WordValue.Number(WordValue.Attr(widthElement, "w"));
        var type = WordValue.Attr(widthElement, "type");

        if (value is null or <= 0) return "100%";

        // Word đo độ rộng bảng theo hai kiểu: phần trăm ghi bằng đơn vị 1/50 %, hoặc twip tuyệt đối.
        if (type is "pct") return Math.Min(100, value.Value / 50).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        if (type is "dxa")
        {
            var percent = value.Value / WordValue.TwipPerPoint / context.PageContentWidthPt * 100;
            return Math.Clamp(percent, 5, 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        }

        return "100%";
    }

    /// <summary>Bốn cạnh ngoài và hai đường kẻ trong của một bảng, đã quy sang CSS.</summary>
    private sealed class TableBorderSet
    {
        public string? Top { get; }
        public string? Left { get; }
        public string? Bottom { get; }
        public string? Right { get; }
        public string? InsideHorizontal { get; }
        public string? InsideVertical { get; }

        public TableBorderSet(OpenXmlElement? bordersElement)
        {
            Top = Edge(bordersElement, "top");
            Left = Edge(bordersElement, "left", "start");
            Bottom = Edge(bordersElement, "bottom");
            Right = Edge(bordersElement, "right", "end");
            InsideHorizontal = Edge(bordersElement, "insideH");
            InsideVertical = Edge(bordersElement, "insideV");
        }

        private static string? Edge(OpenXmlElement? bordersElement, params string[] nameList) =>
            WordValue.BorderCss(bordersElement?.Elements().FirstOrDefault(x => nameList.Contains(x.LocalName)));
    }

    /// <summary>
    /// CSS của một ô. Đường kẻ của ô suy ra từ VỊ TRÍ của nó trong bảng: nằm ở rìa thì lấy cạnh ngoài, nằm
    /// giữa thì lấy đường kẻ trong — đúng cách Word hiểu khai báo đường kẻ ở cấp bảng. Ô nào tự khai báo
    /// đường kẻ riêng thì phần khai báo riêng đè lên.
    /// </summary>
    private static string CellCss(
        CellPlacement cell, int rowIndex, int rowCount, int columnCount,
        TableBorderSet tableBorder, string cellPadding)
    {
        var cellProperty = WordValue.Child(cell.Element, "tcPr");
        var ownBorder = WordValue.Child(cellProperty, "tcBorders");

        var isLastRow = rowIndex + cell.RowSpan >= rowCount;
        var isLastColumn = columnCount == 0 || cell.GridStart + cell.ColumnSpan >= columnCount;

        var cssList = new List<string>();
        AppendEdge("top", rowIndex == 0 ? tableBorder.Top : tableBorder.InsideHorizontal, "top");
        AppendEdge("left", cell.GridStart == 0 ? tableBorder.Left : tableBorder.InsideVertical, "left", "start");
        AppendEdge("bottom", isLastRow ? tableBorder.Bottom : tableBorder.InsideHorizontal, "bottom");
        AppendEdge("right", isLastColumn ? tableBorder.Right : tableBorder.InsideVertical, "right", "end");

        var fill = WordValue.Attr(WordValue.Child(cellProperty, "shd"), "fill");
        if (WordValue.IsRealColor(fill)) cssList.Add("background:#" + fill);

        var verticalAlign = WordValue.Attr(WordValue.Child(cellProperty, "vAlign"), "val");
        cssList.Add("vertical-align:" + verticalAlign switch
        {
            "center" => "middle",
            "bottom" => "bottom",
            _ => "top"
        });

        cssList.Add(cellPadding);

        return string.Join(';', cssList);

        void AppendEdge(string cssSide, string? inherited, params string[] wordNameList)
        {
            var own = WordValue.BorderCss(ownBorder?.Elements().FirstOrDefault(x => wordNameList.Contains(x.LocalName)));
            var value = own ?? inherited;
            if (value is not null) cssList.Add("border-" + cssSide + ":" + value);
        }
    }

    /// <summary>Lề trong ô. Luôn trả về một giá trị: tài liệu không khai báo thì lấy đúng mức mặc định của
    /// Word (0,19cm hai bên), vì phía giao diện đã bỏ hết đệm mặc định để bảng bám sát bản gốc — không phát
    /// ra gì ở đây là chữ dính sát đường kẻ.</summary>
    private static string CellPaddingCss(OpenXmlElement? cellMarginElement)
    {
        var top = Edge("top") ?? 0;
        var right = Edge("right", "end") ?? 5.4;
        var bottom = Edge("bottom") ?? 0;
        var left = Edge("left", "start") ?? 5.4;

        return "padding:" + WordValue.Pt(top) + " " + WordValue.Pt(right) + " "
               + WordValue.Pt(bottom) + " " + WordValue.Pt(left);

        double? Edge(params string[] nameList)
        {
            var element = cellMarginElement?.Elements().FirstOrDefault(x => nameList.Contains(x.LocalName));
            return WordValue.PointFromTwip(WordValue.Attr(element, "w"));
        }
    }

    // ==================================================================
    //  NỘI DUNG TRONG DÒNG: CHỮ, LIÊN KẾT, ẢNH, HỘP VĂN BẢN
    // ==================================================================

    private async Task<string> BuildInlineAsync(
        OpenXmlElement container, WordRunFormat baseRun, ConvertContext context)
    {
        var builder = new StringBuilder();

        foreach (var child in container.Elements<OpenXmlElement>())
        {
            switch (child.LocalName)
            {
                case "r":
                    if (child is Run run) await WriteRunAsync(builder, run, baseRun, context);
                    break;

                case "hyperlink":
                {
                    var inner = await BuildInlineAsync(child, baseRun, context);
                    if (inner.Length == 0) break;

                    var href = HyperlinkTargetOf(child, context);
                    if (href is null) { builder.Append(inner); break; }

                    builder.Append("<a href=\"").Append(HtmlEncode(href))
                           .Append("\" target=\"_blank\" rel=\"noopener\">").Append(inner).Append("</a>");
                    break;
                }

                // Trường đơn giản: mục lục tự động và tham chiếu chéo nằm ở dạng này. Phần chữ đã dựng sẵn
                // bên trong nên chỉ cần lấy ra, riêng trường HYPERLINK thì bọc lại thành liên kết thật.
                case "fldSimple":
                {
                    var inner = await BuildInlineAsync(child, baseRun, context);
                    if (inner.Length == 0) break;

                    var target = HyperlinkFromInstruction(WordValue.Attr(child, "instr"));
                    if (target is null) { builder.Append(inner); break; }

                    builder.Append("<a href=\"").Append(HtmlEncode(target))
                           .Append("\" rel=\"noopener\">").Append(inner).Append("</a>");
                    break;
                }

                // Dấu trang: giữ lại làm mốc nhảy để liên kết trong mục lục còn chỗ để tới.
                case "bookmarkStart":
                {
                    var name = WordValue.Attr(child, "name");
                    if (name is not null && !name.StartsWith('_'))
                        builder.Append("<span id=\"doc-").Append(HtmlEncode(name)).Append("\"></span>");
                    break;
                }

                case "ins":
                    builder.Append(await BuildInlineAsync(child, baseRun, context));
                    break;

                case "sdt":
                    var sdtContent = WordValue.Child(child, "sdtContent");
                    if (sdtContent is not null) builder.Append(await BuildInlineAsync(sdtContent, baseRun, context));
                    break;
            }
        }

        return builder.ToString();
    }

    private async Task WriteRunAsync(StringBuilder builder, Run run, WordRunFormat baseRun, ConvertContext context)
    {
        var format = baseRun
            .Override(context.Styles.RunFormatOfStyle(WordValue.Attr(WordValue.Child(run.RunProperties, "rStyle"), "val")))
            .Override(WordStyleSheet.ReadRunFormat(run.RunProperties));

        var content = new StringBuilder();

        foreach (var child in run.Elements<OpenXmlElement>())
        {
            switch (child.LocalName)
            {
                case "t":
                    content.Append(HtmlEncode(child.InnerText));
                    break;

                case "tab":
                    content.Append("<span class=\"doc-tab\"></span>");
                    break;

                case "br":
                    // Ngắt trang không dựng được bằng thẻ hr vì hr không hợp lệ bên trong đoạn văn — dùng
                    // một span kiểu block, trình duyệt vẫn xuống dòng và vẽ được đường ngăn.
                    content.Append(WordValue.Attr(child, "type") is "page"
                        ? "<span class=\"doc-page-break\"></span>"
                        : "<br>");
                    break;

                case "cr":
                    content.Append("<br>");
                    break;

                case "noBreakHyphen":
                    content.Append("&#8209;");
                    break;

                case "softHyphen":
                    content.Append("&shy;");
                    break;

                case "sym":
                    content.Append(SymbolTextOf(child));
                    break;

                case "drawing":
                case "pict":
                case "object":
                    content.Append(await BuildGraphicAsync(child, baseRun, context));
                    break;

                // Nội dung có bản dựng mới và bản dự phòng cho Word đời cũ. Chỉ lấy bản mới, lấy cả hai là
                // mọi hộp văn bản và hình vẽ hiện ra hai lần.
                case "AlternateContent":
                {
                    var choice = child.Elements().FirstOrDefault(x => x.LocalName == "Choice")
                                 ?? child.Elements().FirstOrDefault(x => x.LocalName == "Fallback");
                    foreach (var graphic in choice?.Elements() ?? Enumerable.Empty<OpenXmlElement>())
                        content.Append(await BuildGraphicAsync(graphic, baseRun, context));
                    break;
                }
            }
        }

        if (content.Length == 0) return;

        var (open, close) = RunWrap(baseRun, format);
        builder.Append(open).Append(content).Append(close);
    }

    /// <summary>Chỉ ghi phần định dạng KHÁC với định dạng nền của đoạn. Phải xử lý được cả chiều tắt: một
    /// đoạn tiêu đề in đậm sẵn mà có mấy chữ cố ý bỏ đậm thì phải ghi ra "font-weight:normal", chứ không
    /// phải bỏ qua vì "không có thẻ b".</summary>
    private static (string Open, string Close) RunWrap(WordRunFormat baseFormat, WordRunFormat format)
    {
        var open = new StringBuilder();
        var close = new StringBuilder();
        var cssList = new List<string>();

        if (format.Bold == true && baseFormat.Bold != true) Wrap("b");
        else if (format.Bold != true && baseFormat.Bold == true) cssList.Add("font-weight:normal");

        if (format.Italic == true && baseFormat.Italic != true) Wrap("i");
        else if (format.Italic != true && baseFormat.Italic == true) cssList.Add("font-style:normal");

        var decoration = DecorationCss(format);
        if (decoration != DecorationCss(baseFormat)) cssList.Add("text-decoration:" + decoration);

        if (format.Color != baseFormat.Color && format.Color is not null) cssList.Add("color:#" + format.Color);
        if (format.Background != baseFormat.Background && format.Background is not null)
            cssList.Add("background:#" + format.Background);

        if (format.SizePt is not null && format.SizePt != baseFormat.SizePt)
            cssList.Add("font-size:" + WordValue.Pt(format.SizePt.Value));

        if (format.FontName is not null && format.FontName != baseFormat.FontName)
            if (FontFamilyCss(format.FontName) is string family) cssList.Add("font-family:" + family);

        if (format.AllCaps == true && baseFormat.AllCaps != true) cssList.Add("text-transform:uppercase");

        if (cssList.Count > 0)
        {
            open.Append("<span style=\"").Append(string.Join(';', cssList)).Append("\">");
            close.Insert(0, "</span>");
        }

        // Chỉ số trên/dưới bọc ngoài cùng để cỡ chữ thu nhỏ của sup/sub áp lên toàn bộ phần bên trong.
        if (format.VerticalAlign != baseFormat.VerticalAlign)
        {
            if (format.VerticalAlign is "superscript") Wrap("sup");
            else if (format.VerticalAlign is "subscript") Wrap("sub");
        }

        return (open.ToString(), close.ToString());

        void Wrap(string tag)
        {
            open.Insert(0, "<" + tag + ">");
            close.Append("</" + tag + ">");
        }
    }

    private static string DecorationCss(WordRunFormat format)
    {
        var lineList = new List<string>();
        var underlineStyle = string.Empty;

        if (format.Underline is string underline && !underline.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            lineList.Add("underline");
            underlineStyle = underline.ToLowerInvariant() switch
            {
                "double" or "doubleaccounting" => " double",
                "dotted" or "dottedheavy" => " dotted",
                "dash" or "dashed" or "dashlong" or "dashedheavy" => " dashed",
                "wave" or "wavyheavy" or "wavydouble" => " wavy",
                _ => string.Empty
            };
        }

        if (format.Strike == true) lineList.Add("line-through");

        return lineList.Count == 0 ? "none" : string.Join(' ', lineList) + underlineStyle;
    }

    // ==================================================================
    //  ẢNH VÀ HỘP VĂN BẢN
    // ==================================================================

    private async Task<string> BuildGraphicAsync(
        OpenXmlElement element, WordRunFormat baseRun, ConvertContext context)
    {
        // Hộp văn bản: nội dung bên trong là cả đoạn văn lẫn bảng nên phải dựng như một khối riêng, và khối
        // đó chỉ được đổ ra SAU khi đoạn văn chứa nó đã đóng thẻ.
        var textBox = element.Descendants().FirstOrDefault(x => x.LocalName == "txbxContent");
        if (textBox is not null)
        {
            if (context.Depth >= MaxNestingDepth) return string.Empty;
            context.Depth++;

            var boxBuilder = new StringBuilder("<div class=\"doc-textbox\">");
            var outerList = context.ListStack.ToList();
            context.ListStack.Clear();

            await WriteBlockListAsync(boxBuilder, textBox.Elements<OpenXmlElement>(), context);
            CloseAllList(boxBuilder, context);

            context.ListStack.AddRange(outerList);
            boxBuilder.Append("</div>");

            context.Depth--;
            context.PendingBlockList.Add(boxBuilder.ToString());
            return string.Empty;
        }

        return await BuildImageAsync(element, context) ?? string.Empty;
    }

    /// <summary>Ảnh nhúng trong tài liệu. Trả null nếu phần tử này không phải ảnh dựng được.</summary>
    private async Task<string?> BuildImageAsync(OpenXmlElement element, ConvertContext context)
    {
        // Ảnh chèn bằng Word đời mới nằm ở a:blip; ảnh của tài liệu đời cũ nằm ở v:imagedata.
        var relationshipId =
            WordValue.Attr(element.Descendants().FirstOrDefault(x => x.LocalName == "blip"), "embed")
            ?? WordValue.Attr(element.Descendants().FirstOrDefault(x => x.LocalName == "imagedata"), "id");
        if (relationshipId is null) return null;

        if (context.MainPart.GetPartById(relationshipId) is not ImagePart imagePart) return null;

        var extension = imagePart.ContentType switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/webp" => ".webp",
            "image/jpeg" => ".jpg",
            // Word nhúng hình vẽ dán vào dưới dạng emf/wmf — trình duyệt không hiển thị được, và đoán bừa
            // sang .jpg thì chỉ ra một ô ảnh vỡ. Bỏ qua, phần chữ của tài liệu vẫn giữ nguyên.
            _ => null
        };
        if (extension is null) return null;

        // Đếm trước MỌI lối thoát phía dưới, kể cả trần số ảnh: lượt xem trước cố ý không lưu ảnh nào, nếu
        // nó đếm khác lượt tạo thật thì hai danh sách phần lệch nhau và người soạn chọn phần này lại ra phần
        // khác. Vì cùng lý do đó, trần số ảnh phải chặn ở đây chứ không chặn ngay đầu hàm.
        context.SectionImageCount++;
        if (context.ImageCount >= MaxImage) return null;

        await using var partStream = imagePart.GetStream();
        using var memory = new MemoryStream();
        await partStream.CopyToAsync(memory);

        string url;
        try
        {
            url = await context.SaveImageAsync(memory.ToArray(), extension);
        }
        catch
        {
            // Một tấm ảnh không lưu được (định dạng không nằm trong danh sách cho phép, đĩa lỗi...) không
            // đáng để đánh hỏng cả lần nhập tài liệu — bỏ tấm đó, giữ lại toàn bộ phần còn lại.
            return null;
        }

        if (string.IsNullOrEmpty(url)) return null;
        context.ImageCount++;

        var cssList = new List<string> { "max-width:100%", "height:auto" };

        // Kích thước Word ghi bằng EMU. Giữ lại mới đúng bản gốc: không có nó thì một biểu tượng nhỏ và một
        // ảnh chụp màn hình khổ lớn hiện ra to bằng nhau.
        var width = WordValue.Number(
            WordValue.Attr(element.Descendants().FirstOrDefault(x => x.LocalName == "extent"), "cx"));
        if (width is > 0)
            cssList.Insert(0, "width:" + (width.Value / WordValue.EmuPerPixel).ToString("0", CultureInfo.InvariantCulture) + "px");

        // Ảnh thả trôi có chữ chạy vòng quanh: giữ lại kiểu trôi trái/phải cho gần bản gốc.
        var anchor = element.Descendants().FirstOrDefault(x => x.LocalName == "anchor");
        if (anchor is not null)
        {
            var align = WordValue.Child(WordValue.Child(anchor, "positionH"), "align")?.InnerText;
            if (align is "left") cssList.Add("float:left;margin:0 12px 8px 0");
            else if (align is "right") cssList.Add("float:right;margin:0 0 8px 12px");
        }

        var description = WordValue.Attr(
            element.Descendants().FirstOrDefault(x => x.LocalName == "docPr"), "descr", "name") ?? string.Empty;

        return "<img src=\"" + HtmlEncode(url) + "\" alt=\"" + HtmlEncode(description)
               + "\" style=\"" + string.Join(';', cssList) + "\">";
    }

    // ==================================================================
    //  QUY ĐỔI SANG CSS
    // ==================================================================

    private static string DocumentCss(ConvertContext context)
    {
        var cssList = new List<string>();

        if (context.Styles.DefaultRun.FontName is string font && FontFamilyCss(font) is string family)
            cssList.Add("font-family:" + family);

        if (context.Styles.DefaultRun.SizePt is double size)
            cssList.Add("font-size:" + WordValue.Pt(size));

        return string.Join(';', cssList);
    }

    private static string ParagraphCss(WordParagraphFormat format, WordRunFormat baseRun, bool isListItem)
    {
        var cssList = new List<string>();

        if (format.Alignment is not null) cssList.Add("text-align:" + format.Alignment);

        // Mục danh sách KHÔNG lấy thụt lề của Word: mức thụt đã do việc lồng ul/ol tạo ra rồi, cộng thêm
        // lần nữa là mỗi cấp bị đẩy xa gấp đôi.
        if (!isListItem)
        {
            if (format.IndentLeftPt is > 0) cssList.Add("margin-left:" + WordValue.Pt(format.IndentLeftPt.Value));
            if (format.IndentRightPt is > 0) cssList.Add("margin-right:" + WordValue.Pt(format.IndentRightPt.Value));
            if (format.FirstLineIndentPt is double firstLine && Math.Abs(firstLine) > 0.01)
                cssList.Add("text-indent:" + WordValue.Pt(firstLine));
        }

        // Mục danh sách bật "bỏ giãn cách cùng style" thì Word dán các mục sát nhau, không chừa khoảng.
        var keepSpacing = !(isListItem && format.ContextualSpacing == true);
        if (keepSpacing && format.SpaceBeforePt is not null)
            cssList.Add("margin-top:" + WordValue.Pt(format.SpaceBeforePt.Value));
        if (keepSpacing && format.SpaceAfterPt is not null)
            cssList.Add("margin-bottom:" + WordValue.Pt(format.SpaceAfterPt.Value));
        if (!keepSpacing) cssList.Add("margin-top:0;margin-bottom:0");
        if (format.LineHeight is not null) cssList.Add("line-height:" + format.LineHeight);
        if (format.Background is not null) cssList.Add("background:#" + format.Background);

        if (format.BorderCss is not null)
        {
            cssList.Add(format.BorderCss);
            cssList.Add("padding:4pt 6pt");
        }

        AppendRunCss(cssList, baseRun);
        return string.Join(';', cssList);
    }

    /// <summary>Định dạng chữ nền của đoạn, ghi thẳng vào thẻ khối.</summary>
    private static void AppendRunCss(List<string> cssList, WordRunFormat format)
    {
        if (format.FontName is string font && FontFamilyCss(font) is string family) cssList.Add("font-family:" + family);
        if (format.SizePt is not null) cssList.Add("font-size:" + WordValue.Pt(format.SizePt.Value));
        if (format.Color is not null) cssList.Add("color:#" + format.Color);
        if (format.Bold == true) cssList.Add("font-weight:bold");
        if (format.Italic == true) cssList.Add("font-style:italic");

        var decoration = DecorationCss(format);
        if (decoration != "none") cssList.Add("text-decoration:" + decoration);
    }

    /// <summary>
    /// Tên phông từ tài liệu sang khai báo font-family an toàn.
    ///
    /// Lọc bỏ mọi ký tự không phải chữ/số/khoảng trắng: tên phông là chuỗi tự do do file tải lên quyết định,
    /// mà nó lại được ghi thẳng vào thuộc tính style — để nguyên thì một tên phông chứa dấu ngoặc kép hoặc
    /// dấu chấm phẩy chèn thêm được khai báo CSS lạ vào trang.
    /// </summary>
    internal static string? FontFamilyCss(string name)
    {
        var clean = new string(name.Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-').ToArray()).Trim();
        if (clean.Length == 0) return null;

        var lower = clean.ToLowerInvariant();
        var fallback =
            lower.Contains("mono") || lower.Contains("courier") || lower.Contains("consol") ? "monospace"
            : lower.Contains("times") || lower.Contains("georgia") || lower.Contains("garamond")
              || lower.Contains("cambria") || lower.Contains("serif") || lower.Contains("book") ? "serif"
            : "sans-serif";

        return "'" + clean + "'," + fallback;
    }

    /// <summary>Bề ngang phần chữ của trang, lấy từ khai báo khổ giấy và lề của tài liệu.</summary>
    private static double PageContentWidthOf(Body body)
    {
        var section = body.Elements<SectionProperties>().LastOrDefault();
        var pageWidth = WordValue.Number(WordValue.Attr(WordValue.Child(section, "pgSz"), "w"));
        if (pageWidth is null or <= 0) return DefaultPageContentWidthPt;

        var margin = WordValue.Child(section, "pgMar");
        var left = WordValue.Number(WordValue.Attr(margin, "left", "start")) ?? 1440;
        var right = WordValue.Number(WordValue.Attr(margin, "right", "end")) ?? 1440;

        var content = (pageWidth.Value - left - right) / WordValue.TwipPerPoint;
        return content > 50 ? content : DefaultPageContentWidthPt;
    }

    private static string StyleAttribute(string? css) =>
        string.IsNullOrEmpty(css) ? string.Empty : " style=\"" + css + "\"";

    /// <summary>Ký tự đặc biệt chèn bằng bảng Symbol/Wingdings. Trình duyệt không có mấy phông đó nên phải
    /// đổi sang ký tự Unicode tương đương; mã lạ thì bỏ hẳn, thà thiếu một ký hiệu còn hơn hiện ra ô vuông.</summary>
    private static string SymbolTextOf(OpenXmlElement element) =>
        WordValue.Attr(element, "char")?.ToUpperInvariant() switch
        {
            "F0B7" or "F0A7" => "•",
            "F06C" => "●",
            "F0A8" or "F0FC" => "✔",
            "F0FE" => "☑",
            "F0E0" or "F0E8" => "➔",
            "F0D8" => "▪",
            _ => string.Empty
        };

    // ==================================================================
    //  AN TOÀN
    // ==================================================================

    private static string? HyperlinkTargetOf(OpenXmlElement hyperlink, ConvertContext context)
    {
        // Liên kết trong cùng tài liệu (mục lục) trỏ tới dấu trang chứ không có địa chỉ ngoài.
        var anchor = WordValue.Attr(hyperlink, "anchor");
        if (anchor is not null) return "#doc-" + anchor;

        var id = WordValue.Attr(hyperlink, "id");
        if (id is null) return null;

        var relationship = context.MainPart.HyperlinkRelationships.FirstOrDefault(x => x.Id == id);
        return SafeUrl(relationship?.Uri?.ToString());
    }

    private static string? HyperlinkFromInstruction(string? instruction)
    {
        if (instruction is null) return null;

        var index = instruction.IndexOf("HYPERLINK", StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;

        var quoteStart = instruction.IndexOf('"', index);
        if (quoteStart < 0) return null;

        var quoteEnd = instruction.IndexOf('"', quoteStart + 1);
        return quoteEnd < 0 ? null : SafeUrl(instruction[(quoteStart + 1)..quoteEnd]);
    }

    /// <summary>Chỉ cho qua những giao thức vô hại. Địa chỉ liên kết lấy nguyên từ file người dùng tải lên,
    /// mà một liên kết "javascript:" đặt trong tài liệu Word sẽ chạy được trên trình duyệt của mọi học viên
    /// khi họ bấm vào.</summary>
    internal static string? SafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var trimmed = url.Trim();
        if (trimmed.StartsWith('#') || trimmed.StartsWith('/')) return trimmed;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
               && parsed.Scheme is "http" or "https" or "mailto" or "tel"
            ? trimmed
            : null;
    }

    /// <summary>Mã hóa toàn bộ chữ lấy từ tài liệu. Nội dung Word là dữ liệu do người dùng đưa vào, mà HTML
    /// sinh ra ở đây được nhúng thẳng vào trang học — không mã hóa thì một tài liệu chứa thẻ script sẽ chạy
    /// được trên trình duyệt của mọi học viên.</summary>
    internal static string HtmlEncode(string value) => value
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&#39;");

    /// <summary>Bỏ thẻ HTML khỏi một đoạn đã dựng, dùng khi cần chuỗi thuần (tên bài học).</summary>
    private static string StripTag(string html)
    {
        var builder = new StringBuilder();
        var inside = false;
        foreach (var ch in html)
        {
            if (ch == '<') { inside = true; continue; }
            if (ch == '>') { inside = false; continue; }
            if (!inside) builder.Append(ch);
        }

        return builder.ToString()
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&#39;", "'").Replace(" ", " ")
            .Trim();
    }
}
