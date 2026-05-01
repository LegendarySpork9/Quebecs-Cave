namespace QuebecsCave.Services.Seed;

public interface IDevDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}
