using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using TCS.HoboBot.Services;

public class HobbyDocument {
    [BsonId, BsonRepresentation( BsonType.ObjectId )]
    public string Id { get; init; } = string.Empty;

    public required string Name { get; init; }
    public int SkillLevel { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public record HobbyDto(string Id, string Name, int SkillLevel);

public interface IHobbyRepository {
    Task<HobbyDto> CreateAsync(HobbyDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<HobbyDto>> GetAllAsync(int page, int size, CancellationToken ct = default);
    Task<HobbyDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<bool> UpdateAsync(string id, HobbyDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

public sealed class HobbyRepository : IHobbyRepository {
    readonly IMongoCollection<HobbyDocument> m_col;

    public HobbyRepository(IDataContext ctx) {
        m_col = ctx.Hobbies;
    }

    public async Task<HobbyDto> CreateAsync(HobbyDto dto, CancellationToken ct = default) {
        var doc = new HobbyDocument { Name = dto.Name, SkillLevel = dto.SkillLevel };
        await m_col.InsertOneAsync( doc, cancellationToken: ct );
        return dto with { Id = doc.Id };
    }

    public async Task<IReadOnlyList<HobbyDto>> GetAllAsync(int page, int size, CancellationToken ct = default) {
        return await m_col.Find( FilterDefinition<HobbyDocument>.Empty )
            .Project( Builders<HobbyDocument>.Projection.Expression( d => new HobbyDto( d.Id, d.Name, d.SkillLevel ) ) )
            .Skip( page * size )
            .Limit( size )
            .SortByDescending( d => d.CreatedAt )
            .ToListAsync( ct );
    }

    public async Task<HobbyDto?> GetByIdAsync(string id, CancellationToken ct = default) {
        return await m_col.Find( d => d.Id == id )
            .Project( Builders<HobbyDocument>.Projection.Expression( d => new HobbyDto( d.Id, d.Name, d.SkillLevel ) ) )
            .FirstOrDefaultAsync( ct );
    }

    public async Task<bool> UpdateAsync(string id, HobbyDto dto, CancellationToken ct = default) {
        UpdateDefinition<HobbyDocument>? update = Builders<HobbyDocument>.Update
            .Set( d => d.Name, dto.Name )
            .Set( d => d.SkillLevel, dto.SkillLevel );

        var result = await m_col.UpdateOneAsync( d => d.Id == id, update, cancellationToken: ct );
        return result.ModifiedCount == 1;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
        return (await m_col.DeleteOneAsync( d => d.Id == id, ct )).DeletedCount == 1;
    }
}