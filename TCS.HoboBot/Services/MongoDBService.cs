using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
namespace TCS.HoboBot.Services;

/*// Example: Registering for other consumers in your application
services.AddSingleton<IMongoClient>( sp => {
        var config = sp.GetRequiredService<IConfiguration>();
        var mongoUri = config["MONGODB_URI"];
        if ( string.IsNullOrEmpty( mongoUri ) ) {
            throw new InvalidOperationException( "MONGODB_URI is not configured. Please set it in appsettings.json or environment variables." );
        }

        var clientSettings = MongoClientSettings.FromConnectionString( mongoUri );
        clientSettings.ServerApi = new ServerApi( ServerApiVersion.V1 );
        return new MongoClient( clientSettings );
    }
);

services.AddSingleton<IDataContext, DataContext>(); // Or Scoped, Transient as needed
//services.AddScoped<IHobbyRepository, HobbyRepository>(); // Or Scoped, Transient as needed
services.AddHostedService<SampleService>();*/


public interface IDataContext {
    IMongoCollection<HobbyDocument> Hobbies { get; }
}

public sealed class DataContext : IDataContext {
    public DataContext(IMongoClient client) {
        Hobbies = client.GetDatabase( "hobobot" ).GetCollection<HobbyDocument>( "hobbies" );
    }

    public IMongoCollection<HobbyDocument> Hobbies { get; }
}

public class MongoDBService : IHostedService {
    readonly IDataContext m_dataContext; // Injected IDataContext
    readonly IConfiguration m_config;

    public MongoDBService(
        IConfiguration config,
        IDataContext dataContext // If you want to inject a DataContext instead of creating it here
    ) {
        m_config = config ?? throw new ArgumentNullException( nameof(config) );
        m_dataContext = dataContext ?? throw new ArgumentNullException( nameof(dataContext) );
    }

    public async Task StartAsync(CancellationToken cancellationToken) {
        string? uri = m_config["MONGODB_URI"];
        if ( string.IsNullOrEmpty( uri ) ) {
            Console.WriteLine( "MONGODB_URI is not configured. MongoDB operations will be skipped." );
            return; // Exit if no URI, as we can't create the client or repository
        }

        try {
            var settings = MongoClientSettings.FromConnectionString( uri );
            settings.ServerApi = new ServerApi( ServerApiVersion.V1 );
            Console.WriteLine( "MongoDB connection for SampleService's client established successfully." );

            IHobbyRepository hobbyRepository = new HobbyRepository( m_dataContext ); // Create Repository

            Console.WriteLine( "\n--- Starting Hobby Repository Tests (Repository created within SampleService) ---" );

            // 1. Save Test (Create)
            Console.WriteLine( "\n--- 1. Save Test (Create) ---" );
            var newHobbyDto = new HobbyDto( string.Empty, "Urban Sketching", 7 );
            var createdHobby = await hobbyRepository.CreateAsync( newHobbyDto, cancellationToken );
            Console.WriteLine( $"Successfully Created Hobby: Id='{createdHobby.Id}', Name='{createdHobby.Name}', SkillLevel={createdHobby.SkillLevel}" );

            if ( string.IsNullOrEmpty( createdHobby.Id ) ) {
                Console.WriteLine( "Critical error: Hobby created but ID is missing. Aborting further tests." );
                return;
            }

            // 2. Load Test (GetById)
            Console.WriteLine( "\n--- 2. Load Test (GetById) ---" );
            var loadedHobby = await hobbyRepository.GetByIdAsync( createdHobby.Id, cancellationToken );
            Console.WriteLine(
                loadedHobby != null
                    ? $"Successfully Loaded Hobby: Id='{loadedHobby.Id}', Name='{loadedHobby.Name}', SkillLevel={loadedHobby.SkillLevel}"
                    : $"Error: Hobby with Id='{createdHobby.Id}' not found after creation."
            );

            // 3. Update Example
            Console.WriteLine( "\n--- 3. Update Example ---" );
            if ( loadedHobby != null ) {
                var updatedHobbyDto = loadedHobby with { SkillLevel = 8, Name = "Advanced Urban Sketching" };
                bool updateResult = await hobbyRepository.UpdateAsync( createdHobby.Id, updatedHobbyDto, cancellationToken );
                Console.WriteLine( $"Update Result for Id='{createdHobby.Id}': {updateResult}" );

                var reloadedHobby = await hobbyRepository.GetByIdAsync( createdHobby.Id, cancellationToken );
                if ( reloadedHobby != null ) {
                    Console.WriteLine( $"Reloaded Hobby after update: Id='{reloadedHobby.Id}', Name='{reloadedHobby.Name}', SkillLevel={reloadedHobby.SkillLevel}" );
                }
            }
            else {
                Console.WriteLine( "Skipping update test as hobby was not loaded." );
            }

            // 4. Get All Example (Load Test - All)
            Console.WriteLine( "\n--- 4. Get All Example (Load Test - All) ---" );
            await hobbyRepository.CreateAsync( new HobbyDto( string.Empty, "Baking Bread", 6 ), cancellationToken );
            var anotherHobby = await hobbyRepository.CreateAsync( new HobbyDto( string.Empty, "Learning Spanish", 5 ), cancellationToken );

            IReadOnlyList<HobbyDto> allHobbies = await hobbyRepository.GetAllAsync( 0, 10, cancellationToken );
            Console.WriteLine( $"Total Hobbies Found (Page 1, Size 10): {allHobbies.Count}" );
            foreach (var hobby in allHobbies) {
                Console.WriteLine( $"- Hobby: Id='{hobby.Id}', Name='{hobby.Name}', SkillLevel={hobby.SkillLevel}" );
            }

            // 5. Delete Example
            Console.WriteLine( "\n--- 5. Delete Example ---" );
            bool deleteResult = await hobbyRepository.DeleteAsync( createdHobby.Id, cancellationToken );
            Console.WriteLine( $"Delete Result for Id='{createdHobby.Id}': {deleteResult}" );

            var deletedHobbyCheck = await hobbyRepository.GetByIdAsync( createdHobby.Id, cancellationToken );
            Console.WriteLine(
                deletedHobbyCheck == null
                    ? $"Hobby with Id='{createdHobby.Id}' successfully deleted (as expected, not found)."
                    : $"Error: Hobby with Id='{createdHobby.Id}' still found after attempting delete."
            );

            // Clean up other test data
            if ( !string.IsNullOrEmpty( anotherHobby.Id ) ) {
                await hobbyRepository.DeleteAsync( anotherHobby.Id, cancellationToken );
                Console.WriteLine( $"Cleaned up test hobby: '{anotherHobby.Name}'" );
            }

            IReadOnlyList<HobbyDto> bakingHobbies = await hobbyRepository.GetAllAsync( 0, 100, cancellationToken );
            foreach (var hobby in bakingHobbies) {
                if ( hobby.Name != "Baking Bread" ) continue;

                await hobbyRepository.DeleteAsync( hobby.Id, cancellationToken );
                Console.WriteLine( $"Cleaned up test hobby: '{hobby.Name}' with Id='{hobby.Id}'" );
            }
        }
        catch (TimeoutException tex) // MongoDB operations can time out
        {
            Console.WriteLine( $"A MongoDB timeout occurred: {tex.Message}. Check MongoDB server status and network." );
            Console.WriteLine( $"Details: {tex}" );
        }
        catch (MongoException mex) // Catch more specific MongoDB exceptions
        {
            Console.WriteLine( $"A MongoDB specific error occurred: {mex.Message}" );
            Console.WriteLine( $"Details: {mex}" );
        }
        catch (Exception ex) {
            Console.WriteLine( $"An unexpected error occurred during Hobby Repository tests: {ex}" );
        }

        Console.WriteLine( "\n--- Hobby Repository Tests Finished ---" );
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        Console.WriteLine( "MongoDB SampleService is stopping." );
        return Task.CompletedTask;
    }
}