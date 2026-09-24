// File: Domain/Common/VectorHelper.cs
using System.Runtime.InteropServices;

namespace HbELearning.Domain.Common;

/// <summary>Chuyển đổi vector nhúng (embedding) giữa float[] và mảng byte lưu trong database,
/// và tính độ tương đồng cosine giữa hai vector.</summary>
public static class VectorHelper
{
    /// <summary>float[] -> byte[] (mỗi float 4 byte) để lưu xuống cột BLOB.</summary>
    public static byte[] ToBytes(float[] vector) => MemoryMarshal.AsBytes<float>(vector).ToArray();

    /// <summary>byte[] đọc từ database -> float[].</summary>
    public static float[] ToFloatArray(byte[] data)
    {
        if (data.Length == 0 || data.Length % sizeof(float) != 0) return [];

        var result = new float[data.Length / sizeof(float)];
        Buffer.BlockCopy(data, 0, result, 0, data.Length);
        return result;
    }

    /// <summary>Độ tương đồng cosine: 1 = trùng ý nghĩa, 0 = không liên quan, âm = trái nghĩa.
    /// Trả về 0 khi hai vector khác số chiều (khác model embedding) hoặc có vector rỗng.</summary>
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;

        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            normA += a[i] * (double)a[i];
            normB += b[i] * (double)b[i];
        }

        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
