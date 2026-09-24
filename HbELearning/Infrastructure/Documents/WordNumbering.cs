// File: Infrastructure/Documents/WordNumbering.cs
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace HbELearning.Infrastructure.Documents;

/// <param name="IsOrdered">true thì dựng bằng ol (danh sách đánh số), false thì ul (gạch đầu dòng).</param>
/// <param name="CssListStyle">Giá trị đưa thẳng vào list-style-type.</param>
/// <param name="Start">Số bắt đầu của cấp này.</param>
public sealed record WordListLevel(bool IsOrdered, string CssListStyle, int Start);

/// <summary>
/// Đọc phần đánh số của tài liệu (numbering.xml).
///
/// Đoạn văn trong Word KHÔNG tự nói nó là gạch đầu dòng hay đánh số — nó chỉ ghi hai con số: numId (dùng bộ
/// đánh số nào) và ilvl (cấp mấy). Muốn biết ra dấu chấm tròn hay số La Mã, và biết đoạn nào là mục con của
/// đoạn nào, bắt buộc phải tra sang đây. Không tra thì mọi danh sách nhiều cấp bị bẹp thành một cấp và
/// danh sách đánh số thành gạch đầu dòng — đúng hai lỗi làm tài liệu nhập vào trông khác hẳn bản gốc.
/// </summary>
public sealed class WordNumbering
{
    /// <summary>numId -> abstractNumId. Một tài liệu có thể có nhiều numId cùng trỏ về một bộ đánh số gốc
    /// (mỗi lần người soạn bấm "bắt đầu đánh số lại" là thêm một numId mới).</summary>
    private readonly Dictionary<int, int> _abstractIdByNumId = new();

    /// <summary>(abstractNumId, ilvl) -> mô tả cấp.</summary>
    private readonly Dictionary<(int Abstract, int Level), WordListLevel> _levelMap = new();

    /// <summary>Số bắt đầu do riêng một numId ghi đè (w:lvlOverride).</summary>
    private readonly Dictionary<(int NumId, int Level), int> _startOverride = new();

    public static WordNumbering Load(MainDocumentPart mainPart)
    {
        var numbering = new WordNumbering();

        var root = mainPart.NumberingDefinitionsPart?.Numbering;
        if (root is null) return numbering;

        foreach (var abstractNum in root.Elements().Where(x => x.LocalName == "abstractNum"))
        {
            var abstractId = WordValue.Integer(WordValue.Attr(abstractNum, "abstractNumId"));
            if (abstractId is null) continue;

            foreach (var level in abstractNum.Elements().Where(x => x.LocalName == "lvl"))
            {
                var index = WordValue.Integer(WordValue.Attr(level, "ilvl")) ?? 0;
                numbering._levelMap[(abstractId.Value, index)] = ReadLevel(level, index);
            }
        }

        foreach (var num in root.Elements().Where(x => x.LocalName == "num"))
        {
            var numId = WordValue.Integer(WordValue.Attr(num, "numId"));
            var abstractId = WordValue.Integer(WordValue.Attr(WordValue.Child(num, "abstractNumId"), "val"));
            if (numId is null || abstractId is null) continue;

            numbering._abstractIdByNumId[numId.Value] = abstractId.Value;

            foreach (var over in num.Elements().Where(x => x.LocalName == "lvlOverride"))
            {
                var index = WordValue.Integer(WordValue.Attr(over, "ilvl")) ?? 0;
                var start = WordValue.Integer(WordValue.Attr(WordValue.Child(over, "startOverride"), "val"));
                if (start is not null) numbering._startOverride[(numId.Value, index)] = start.Value;
            }
        }

        return numbering;
    }

    /// <summary>Mô tả cấp danh sách của một đoạn văn. Trả null khi bộ đánh số không tồn tại — khi đó đoạn
    /// văn được dựng như đoạn thường, thà mất cái chấm đầu dòng còn hơn dựng ra một danh sách sai.</summary>
    public WordListLevel? LevelOf(int numId, int levelIndex)
    {
        // numId = 0 là quy ước "gỡ đánh số khỏi đoạn này" của Word, không phải một bộ đánh số thật.
        if (numId <= 0) return null;
        if (!_abstractIdByNumId.TryGetValue(numId, out var abstractId)) return null;

        if (!_levelMap.TryGetValue((abstractId, levelIndex), out var level))
        {
            // Cấp sâu hơn phần được định nghĩa: lùi về cấp 0 để vẫn còn ra một danh sách, thay vì mất hẳn.
            if (!_levelMap.TryGetValue((abstractId, 0), out level)) return null;
        }

        return _startOverride.TryGetValue((numId, levelIndex), out var start)
            ? level with { Start = start }
            : level;
    }

    // Mã ký tự đầu dòng của Word, nằm trong vùng dùng riêng của phông Symbol/Wingdings. Viết bằng mã
    // hex chứ không dán ký tự thật vào mã nguồn: máy không có hai phông đó thì cả hai đều hiện ra một ô
    // vuông rỗng y hệt nhau, nhìn bằng mắt không phân biệt được và rất dễ bị đổi nhầm khi sửa file.
    private const char SymbolBulletChar = (char)0xF0B7;    // chấm tròn đặc
    private const char WingdingsSquareChar = (char)0xF0A7; // ô vuông đặc

    private static WordListLevel ReadLevel(OpenXmlElement levelElement, int index)
    {
        var format = WordValue.Attr(WordValue.Child(levelElement, "numFmt"), "val")?.ToLowerInvariant() ?? "decimal";
        var start = WordValue.Integer(WordValue.Attr(WordValue.Child(levelElement, "start"), "val")) ?? 1;

        // "none" là danh sách không có ký hiệu nào — vẫn phải giữ dạng danh sách để đúng mức thụt lề.
        if (format is "none") return new WordListLevel(false, "none", start);

        if (format is "bullet")
        {
            // Word ghi ký tự đầu dòng bằng mã nằm trong vùng dùng riêng của phông Symbol/Wingdings, mà
            // trình duyệt thì không có mấy phông đó. Đổi sang ba ký hiệu chuẩn của CSS theo đúng mã Word
            // dùng: F0B7 là chấm tròn đặc, F0A7 là ô vuông đặc, chữ "o" của phông Courier là vòng tròn rỗng.
            var bulletText = (WordValue.Attr(WordValue.Child(levelElement, "lvlText"), "val") ?? string.Empty).Trim();
            var shape = (bulletText.Length > 0 ? bulletText[0] : ' ') switch
            {
                SymbolBulletChar or '•' or '·' => "disc",
                'o' or 'O' or '○' or '◦' => "circle",
                WingdingsSquareChar or '§' or '▪' or '■' => "square",
                // Ký hiệu lạ (mũi tên, bàn tay, ô đánh dấu...): không có ký hiệu CSS tương đương, lấy theo
                // vòng xoay mặc định của Word để các cấp vẫn phân biệt được với nhau.
                _ => (index % 3) switch { 0 => "disc", 1 => "circle", _ => "square" }
            };
            return new WordListLevel(false, shape, start);
        }

        var cssStyle = format switch
        {
            "decimalzero" => "decimal-leading-zero",
            "lowerletter" => "lower-alpha",
            "upperletter" => "upper-alpha",
            "lowerroman" => "lower-roman",
            "upperroman" => "upper-roman",
            "vietnamesecounting" => "decimal",
            _ => "decimal"
        };

        return new WordListLevel(true, cssStyle, start);
    }
}
