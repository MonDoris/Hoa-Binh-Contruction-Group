// File: Infrastructure/Files/WebmDurationFixer.cs
namespace HbELearning.Infrastructure.Files;

/// <summary>
/// Ghi trường Duration vào file webm do trình duyệt (MediaRecorder) tạo ra. Vì hệ thống ghi từng đoạn video
/// nhỏ nối tiếp nhau ngay trong lúc quay (xem OnlineClassRoomService.UploadRecordingChunk) thay vì ghi 1
/// lần khi đã biết trước tổng thời lượng, khối Segment/Info ở đầu file không có Duration — khiến trình duyệt
/// phát lại báo thời lượng "Infinity" và thanh tua chạy bất thường (2 thanh chồng lên nhau). Một số trình
/// duyệt tự chừa sẵn chỗ trống cho Duration (chỉ cần đè 8 byte); trường hợp không có sẵn (đã xác nhận với
/// Chrome bản dùng để build tính năng này) thì phải chèn thêm hẳn 1 phần tử Duration mới vào cuối khối Info
/// và ghi lại phần đầu file — không đụng tới phần dữ liệu video/audio (Cluster) phía sau.
/// </summary>
public static class WebmDurationFixer
{
    private static readonly byte[] SegmentId = { 0x18, 0x53, 0x80, 0x67 };
    private static readonly byte[] InfoId = { 0x15, 0x49, 0xA9, 0x66 };
    private static readonly byte[] ClusterId = { 0x1F, 0x43, 0xB6, 0x75 };
    private static readonly byte[] DurationId = { 0x44, 0x89 };

    private readonly record struct InfoLocation(long SizeFieldPos, long SizeFieldLength, long BodyStart, long BodySize, long? ExistingDurationValuePos);

    /// <summary>Trả về true nếu ghi được. False (không ném lỗi) nếu file không đúng cấu trúc mong đợi —
    /// khi đó video vẫn phát bình thường, chỉ là không có thời lượng/thanh tua chính xác.</summary>
    public static bool Patch(string filePath, double durationMs)
    {
        var info = Locate(filePath);
        if (info is null) return false;

        if (info.Value.ExistingDurationValuePos.HasValue)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.Position = info.Value.ExistingDurationValuePos.Value;
            stream.Write(EncodeDoubleBigEndian(durationMs), 0, 8);
            return true;
        }

        return InsertDurationElement(filePath, info.Value, durationMs);
    }

    private static InfoLocation? Locate(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Bỏ qua khối EBML header ở đầu file (định danh 0x1A45DFA3) để tới khối Segment.
        if (ReadId(stream) is null) return null;
        SkipBySize(stream, ReadSize(stream));

        var segmentId = ReadId(stream);
        if (segmentId is null || !segmentId.SequenceEqual(SegmentId)) return null;
        ReadSize(stream); // kích thước Segment lúc ghi dạng streaming luôn "không xác định" -> không dùng để nhảy

        // Đọc tuần tự các phần tử con của Segment cho tới khi gặp Info (luôn nằm trước mọi Cluster trong file hợp lệ).
        while (stream.Position < stream.Length)
        {
            var childId = ReadId(stream);
            if (childId is null) return null;
            var sizeFieldPos = stream.Position;
            var childSize = ReadSize(stream);
            var sizeFieldLength = stream.Position - sizeFieldPos;

            if (childId.SequenceEqual(InfoId))
            {
                if (childSize is null) return null; // Info luôn có kích thước xác định trong thực tế
                var bodyStart = stream.Position;
                var bodyEnd = bodyStart + childSize.Value;
                long? durationValuePos = null;

                while (stream.Position < bodyEnd)
                {
                    var gId = ReadId(stream);
                    if (gId is null) return null;
                    var gSize = ReadSize(stream);
                    if (gSize is null) return null;

                    if (gId.SequenceEqual(DurationId) && gSize.Value == 8)
                    {
                        durationValuePos = stream.Position;
                        break;
                    }
                    stream.Position += gSize.Value;
                }

                return new InfoLocation(sizeFieldPos, sizeFieldLength, bodyStart, childSize.Value, durationValuePos);
            }

            if (childId.SequenceEqual(ClusterId)) return null; // gặp Cluster trước khi thấy Info -> chịu
            if (childSize is null) return null; // kích thước không xác định mà chưa tìm thấy Info -> không nhảy qua được

            stream.Position += childSize.Value;
        }
        return null;
    }

    /// <summary>Chèn 1 phần tử Duration (2 byte id + 1 byte size + 8 byte giá trị = 11 byte) vào cuối khối
    /// Info, đồng thời tăng kích thước khai báo của Info thêm 11. Ghi ra file tạm rồi thay thế nguyên tử —
    /// phần Cluster (dữ liệu video/audio thật) phía sau được sao chép nguyên vẹn, không xử lý gì thêm.</summary>
    private static bool InsertDurationElement(string filePath, InfoLocation info, double durationMs)
    {
        var newInfoSize = info.BodySize + 11;
        var newSizeField = EncodeSizeVInt(newInfoSize, (int)info.SizeFieldLength);
        if (newSizeField is null) return false; // kích thước mới không vừa cùng độ dài VINT cũ -> bỏ qua, an toàn

        var tempPath = filePath + ".tmp";
        using (var source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            CopyRange(source, dest, 0, info.SizeFieldPos);

            dest.Write(newSizeField, 0, newSizeField.Length);
            source.Position = info.SizeFieldPos + info.SizeFieldLength; // bỏ qua size field cũ trên nguồn (đã ghi bản mới)

            CopyRange(source, dest, source.Position, info.BodyStart + info.BodySize);

            dest.Write(DurationId, 0, DurationId.Length);
            dest.WriteByte(0x88); // size VINT 1 byte cho giá trị 8 (marker 0x80 | 8)
            dest.Write(EncodeDoubleBigEndian(durationMs), 0, 8);

            CopyRange(source, dest, source.Position, source.Length);
        }

        System.IO.File.Delete(filePath);
        System.IO.File.Move(tempPath, filePath);
        return true;
    }

    private static void CopyRange(FileStream source, FileStream dest, long from, long to)
    {
        source.Position = from;
        var remaining = to - from;
        var buffer = new byte[81920];
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0) break;
            dest.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static byte[] EncodeDoubleBigEndian(double value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes); // EBML float lưu big-endian
        return bytes;
    }

    /// <summary>Mã hoá 1 số nguyên thành VINT đúng bằng "byteLength" byte cho trước (giữ nguyên độ dài VINT
    /// gốc để không phải dịch chuyển các phần tử khác). Trả về null nếu giá trị không vừa độ dài đó.</summary>
    private static byte[]? EncodeSizeVInt(long value, int byteLength)
    {
        if (byteLength is < 1 or > 8) return null;
        var maxValue = (1L << (7 * byteLength)) - 2; // trừ 1 để tránh trùng giá trị "kích thước không xác định" (toàn bit 1)
        if (value < 0 || value > maxValue) return null;

        var bytes = new byte[byteLength];
        var v = value;
        for (var i = byteLength - 1; i >= 0; i--)
        {
            bytes[i] = (byte)(v & 0xFF);
            v >>= 8;
        }
        bytes[0] |= (byte)(0x80 >> (byteLength - 1));
        return bytes;
    }

    private static byte[]? ReadId(Stream stream)
    {
        var first = stream.ReadByte();
        if (first < 0) return null;
        var length = VIntLength((byte)first);
        var bytes = new byte[length];
        bytes[0] = (byte)first;
        for (var i = 1; i < length; i++)
        {
            var b = stream.ReadByte();
            if (b < 0) return null;
            bytes[i] = (byte)b;
        }
        return bytes;
    }

    /// <summary>Đọc kích thước dạng VINT. Null nghĩa là "không xác định" (toàn bộ bit giá trị đều là 1) —
    /// thường gặp ở Segment khi ghi kiểu streaming, không dùng được để nhảy qua phần tử.</summary>
    private static long? ReadSize(Stream stream)
    {
        var first = stream.ReadByte();
        if (first < 0) return null;
        var length = VIntLength((byte)first);
        var marker = (byte)(0x80 >> (length - 1));
        long value = (byte)first & (marker - 1);
        var allOnes = value == marker - 1;
        for (var i = 1; i < length; i++)
        {
            var b = stream.ReadByte();
            if (b < 0) return null;
            value = (value << 8) | (byte)b;
            if ((byte)b != 0xFF) allOnes = false;
        }
        return allOnes ? null : value;
    }

    /// <summary>Số byte của 1 VINT = vị trí bit 1 đầu tiên trong byte đầu (tính từ trái, 1-indexed).</summary>
    private static int VIntLength(byte firstByte)
    {
        for (var i = 0; i < 8; i++)
            if ((firstByte & (0x80 >> i)) != 0) return i + 1;
        return 8;
    }

    private static void SkipBySize(Stream stream, long? size)
    {
        if (size.HasValue) stream.Position += size.Value;
    }
}
