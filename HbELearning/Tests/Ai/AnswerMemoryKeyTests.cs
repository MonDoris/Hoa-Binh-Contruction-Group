using HbELearning.Application.Services;
using Xunit;

namespace HbELearning.Tests.Ai;

/// <summary>Hai câu cùng khóa thì người hỏi sau nhận NGUYÊN câu trả lời của người hỏi trước, nên nhóm "khác nghĩa"
/// quan trọng hơn nhóm "cùng nghĩa": bỏ lỡ một lần ghi nhớ chỉ tốn thời gian, khớp nhầm là trả lời sai quy định.
/// Phần lớn các cặp khác nghĩa dưới đây là những cặp mà vector nomic-embed-text coi là gần như trùng nhau (0,89-0,96).</summary>
public class AnswerMemoryKeyTests
{
    [Theory]
    [InlineData("Mấy giờ vào làm?", "mấy giờ vào làm")]
    [InlineData("Mấy giờ vào làm?", "  MẤY GIỜ VÀO LÀM ạ ")]
    [InlineData("Giờ vào làm là mấy giờ vậy?", "Mấy giờ vào làm?")]
    [InlineData("Tứ diện cháy gồm những yếu tố nào?", "Tứ diện cháy gồm những yếu tố gì?")]
    [InlineData("Tứ diện cháy gồm những yếu tố nào?", "tứ diện cháy gồm yếu tố gì")]
    [InlineData("Quy trình xin nghỉ phép như thế nào?", "Quy trình xin nghỉ phép ra sao?")]
    [InlineData("Làm thế nào để xin nghỉ phép?", "Làm sao để xin nghỉ phép?")]
    [InlineData("Cho tôi hỏi nghỉ phép năm được bao nhiêu ngày?", "Nghỉ phép năm được mấy ngày?")]
    [InlineData("Hãy tóm tắt giúp tôi khóa học An toàn lao động", "Tóm tắt khóa học an toàn lao động")]
    // Cùng chữ nhưng hai kiểu bỏ dấu cũ/mới, và dạng dựng sẵn / tổ hợp của bộ gõ.
    [InlineData("Quy định về hoà giải", "Quy định về hòa giải")]
    [InlineData("Chế độ khi nhân viên bị ốm", "Chế độ khi nhân viên bị ốm")]
    public void Same_meaning_gives_same_key(string first, string second)
    {
        var firstKey = AnswerMemoryKey.Build(first);
        Assert.NotNull(firstKey);
        Assert.Equal(firstKey, AnswerMemoryKey.Build(second));
    }

    [Theory]
    [InlineData("Mấy giờ vào làm?", "Mấy giờ tan làm?")]
    [InlineData("Mấy giờ vào làm buổi sáng?", "Mấy giờ vào làm buổi chiều?")]
    [InlineData("Quy trình xin nghỉ phép như thế nào?", "Quy trình xin nghỉ việc như thế nào?")]
    [InlineData("Nghỉ phép năm được bao nhiêu ngày?", "Nghỉ ốm được bao nhiêu ngày?")]
    [InlineData("Tứ diện cháy gồm những yếu tố nào?", "Tam giác cháy gồm những yếu tố nào?")]
    [InlineData("Nhân viên chính thức được nghỉ phép bao nhiêu ngày?", "Nhân viên thử việc được nghỉ phép bao nhiêu ngày?")]
    [InlineData("Có được mang laptop ra khỏi văn phòng không?", "Có được mang laptop vào văn phòng không?")]
    [InlineData("Đi trễ bị phạt bao nhiêu?", "Về sớm bị phạt bao nhiêu?")]
    [InlineData("Đi trễ 15 phút bị phạt bao nhiêu?", "Đi trễ 30 phút bị phạt bao nhiêu?")]
    [InlineData("Tóm tắt nội dung chính khóa học An toàn lao động", "Tóm tắt nội dung chính khóa học Phòng cháy chữa cháy")]
    // Cùng chủ đề, khác loại câu hỏi: ai / thế nào / ở đâu / khi nào / vì sao / bao nhiêu.
    [InlineData("Ai phê duyệt đơn nghỉ phép?", "Phê duyệt đơn nghỉ phép như thế nào?")]
    [InlineData("Nộp đơn nghỉ phép ở đâu?", "Nộp đơn nghỉ phép khi nào?")]
    [InlineData("Nộp đơn nghỉ phép ở đâu?", "Nộp đơn nghỉ phép như thế nào?")]
    [InlineData("Sao phải nộp đơn nghỉ phép?", "Nộp đơn nghỉ phép ra sao?")]
    [InlineData("Nghỉ phép được bao nhiêu ngày?", "Nghỉ phép được không?")]
    // Dấu thanh là chữ khác.
    [InlineData("Giá bán thiết bị", "Giá bàn thiết bị")]
    public void Different_meaning_gives_different_key(string first, string second)
    {
        Assert.NotEqual(AnswerMemoryKey.Build(first), AnswerMemoryKey.Build(second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("???")]
    [InlineData("là gì vậy ạ?")]
    [InlineData("Cái đó?")]
    [InlineData("Quy định đó áp dụng cho những ai?")]
    [InlineData("Vậy còn trường hợp ấy thì xử lý thế nào ạ?")]
    public void Too_vague_to_remember(string question)
    {
        Assert.Null(AnswerMemoryKey.Build(question));
    }
}
