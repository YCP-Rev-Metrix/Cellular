using System.Reflection;
using System.Text;
using System.Text.Json;
using Cellular.Cloud_API.Endpoints;
using Cellular.Cloud_API.Models;

namespace Cellular.Cloud_API;

public enum EntityType
{
    Ball,
    Establishment,
    Event,
    Frames,
    Game,
    Session,
    Shot
}

public enum OperationType
{
    Get,
    Post
}

public class ApiController
{
    public async Task ExecuteRequest(
        EntityType entityType, 
        OperationType operationType, 
        List<Object>? data = null,
        int id = -1
    ) {
        using var client = new HttpClient();
        ApiExecutor executor = new ApiExecutor(entityType, operationType);
        
        string requestUrl = executor.GetUrl();
        

        if (data != null)
        {
            var requestBody = JsonSerializer.Serialize(data);

            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            
            var response = await client.PostAsync(requestUrl, content);

            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();
            
            // TODO: Handle response body appropriately 
            Console.WriteLine(responseBody);
        }
    }
}