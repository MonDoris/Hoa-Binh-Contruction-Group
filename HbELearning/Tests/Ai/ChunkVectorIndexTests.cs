using HbELearning.Application.Services;
using HbELearning.Domain.Common;
using HbELearning.Domain.Entities;
using HbELearning.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HbELearning.Tests.Ai;

public class ChunkVectorIndexTests
{
    private static (SqliteConnection, DbContextOptions<AppDbContext>) NewDb()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using (var db = new AppDbContext(options)) db.Database.EnsureCreated();
        return (connection, options);
    }

    private static DocumentChunk Chunk(string text, float[] vector, int? courseId = null) => new()
    {
        SourceFileName = "a.pdf",
        ChunkIndex = 0,
        TextContent = text,
        VectorData = VectorHelper.ToBytes(vector),
        VectorDimension = vector.Length,
        EmbeddingModel = "m",
        CourseId = courseId
    };

    [Fact]
    public async Task Reuses_snapshot_and_refreshes_after_insert_and_delete()
    {
        var (connection, options) = NewDb();
        using var _ = connection;
        var index = new ChunkVectorIndex();

        using (var db = new AppDbContext(options))
        {
            db.DocumentChunk.Add(Chunk("tứ diện cháy", [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]));
            await db.SaveChangesAsync();
        }

        ChunkVectorIndex.Entry[] first;
        using (var db = new AppDbContext(options)) first = await index.GetAsync(db, "m", default);
        Assert.Single(first);

        using (var db = new AppDbContext(options))
            Assert.Same(first, await index.GetAsync(db, "m", default));

        using (var db = new AppDbContext(options))
        {
            db.DocumentChunk.Add(Chunk("khác", [0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]));
            await db.SaveChangesAsync();
            Assert.Equal(2, (await index.GetAsync(db, "m", default)).Length);
        }

        using (var db = new AppDbContext(options))
        {
            await db.DocumentChunk.ExecuteDeleteAsync();
            Assert.Empty(await index.GetAsync(db, "m", default));
        }
    }

    [Fact]
    public void Cosine_matches_reference_implementation()
    {
        float[] a = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        float[] b = [2, 1, 0, 4, 3, 7, 1, 0, 5, 2];
        var entry = new ChunkVectorIndex.Entry
        {
            Id = 1, SourceFileName = "x", ChunkIndex = 0, TextContent = "", CourseId = null, LessonId = null,
            Vector = b, Norm = ChunkVectorIndex.Norm(b), NormalizedText = " "
        };

        Assert.Equal(VectorHelper.CosineSimilarity(a, b), ChunkVectorIndex.Cosine(a, ChunkVectorIndex.Norm(a), entry), 5);
    }
}
