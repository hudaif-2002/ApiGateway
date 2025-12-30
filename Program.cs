using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using ApiGateway.DTOs;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Redis Configuration - What: Try to connect to Redis, but work without it if unavailable
var redisConnection = builder.Configuration["Redis:ConnectionString"] ?? builder.Configuration["REDIS_URL"] ?? "localhost:6379";
try {
    var redis = ConnectionMultiplexer.Connect(redisConnection + ",abortConnect=false");
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    Console.WriteLine("[REDIS] Connected to: " + redisConnection);
}
catch (Exception ex) {
    Console.WriteLine("[REDIS] Failed to connect: " + ex.Message + ". Gateway will work without caching.");
    builder.Services.AddSingleton<IConnectionMultiplexer>((IConnectionMultiplexer)null);
}
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "API Gateway", 
        Version = "v1",
        Description = "Gateway for TodoApp Microservices"
    });
    
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Get token from /auth/login first.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

builder.Services.AddHttpClient("AuthService", client =>
{
    var authUrl = builder.Configuration["Services:AuthService"] ?? "http://localhost:5184";
    client.BaseAddress = new Uri(authUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("TodoService", client =>
{
    var todoUrl = builder.Configuration["Services:TodoService"] ?? "http://localhost:5289";
    client.BaseAddress = new Uri(todoUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowFrontend");

app.MapGet("/", () => new
{
    service = "API Gateway",
    version = "1.0",
    description = "Gateway for TodoApp microservices",
    backends = new
    {
        authService = app.Configuration["Services:AuthService"],
        todoService = app.Configuration["Services:TodoService"]
    },
    endpoints = new[]
    {
        "POST /auth/register - Register new user",
        "POST /auth/login - Login and get JWT token",
        "GET /todos - Get user todos (requires auth)",
        "POST /todos - Create todo (requires auth)",
        "PUT /todos/{id} - Update todo (requires auth)",
        "DELETE /todos/{id} - Delete todo (requires auth)"
    }
});

// ========== AUTH ENDPOINTS ==========

app.MapPost("/auth/register", async (RegisterRequest request, IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("AuthService");
    var json = JsonSerializer.Serialize(request);
    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    
    var response = await client.PostAsync("/auth/register", content);
    var result = await response.Content.ReadAsStringAsync();
    
    return Results.Content(result, "application/json", statusCode: (int)response.StatusCode);
})
.WithName("Register")
.WithOpenApi();

app.MapPost("/auth/login", async (LoginRequest request, IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("AuthService");
    var json = JsonSerializer.Serialize(request);
    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    
    var response = await client.PostAsync("/auth/login", content);
    var result = await response.Content.ReadAsStringAsync();
    
    return Results.Content(result, "application/json", statusCode: (int)response.StatusCode);
})
.WithName("Login")
.WithOpenApi();

// ========== TODO ENDPOINTS ==========

app.MapGet("/todos", async (HttpContext context, IHttpClientFactory clientFactory, IConnectionMultiplexer redis) =>
{
    // What: Get user ID from Authorization header to create unique cache key
    var userId = "unknown";
    if (context.Request.Headers.ContainsKey("Authorization"))
    {
        var token = context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        userId = token.Split('.')[1].Substring(0, 10);
    }

    var cacheKey = $"todos:user:{userId}";
    // What: Skip caching if Redis is not available
    if (redis == null)
    if (redis == null)
    {
        Console.WriteLine("[NO REDIS] Fetching directly from TodoApi");
        var directClient = clientFactory.CreateClient("TodoService");
        if (context.Request.Headers.ContainsKey("Authorization"))
        {
            directClient.DefaultRequestHeaders.Authorization =
                AuthenticationHeaderValue.Parse(context.Request.Headers["Authorization"]!);
        }
        var directResponse = await directClient.GetAsync("/api/todos");
        var directResult = await directResponse.Content.ReadAsStringAsync();
        return Results.Content(directResult, "application/json", statusCode: (int)directResponse.StatusCode);
    }
    var db = redis.GetDatabase();

    // What: Check if we have cached data for this user
    var cachedData = await db.StringGetAsync(cacheKey);
    if (!cachedData.IsNullOrEmpty)
    {
        Console.WriteLine($"[CACHE HIT] Returning cached todos for {userId}");
        return Results.Content(cachedData!, "application/json");
    }

    // What: Cache miss - fetch from TodoApi
    Console.WriteLine($"[CACHE MISS] Fetching from TodoApi for {userId}");
    var client = clientFactory.CreateClient("TodoService");

    if (context.Request.Headers.ContainsKey("Authorization"))
    {
        client.DefaultRequestHeaders.Authorization =
            AuthenticationHeaderValue.Parse(context.Request.Headers["Authorization"]!);
    }

    var response = await client.GetAsync("/api/todos");
    var result = await response.Content.ReadAsStringAsync();

    // What: Store in cache for 5 minutes
    if (response.IsSuccessStatusCode)
    {
        await db.StringSetAsync(cacheKey, result, TimeSpan.FromMinutes(5));
        Console.WriteLine($"[CACHED] Stored todos for {userId} (expires in 5 min)");
    }

    return Results.Content(result, "application/json", statusCode: (int)response.StatusCode);
})
.WithName("GetTodos")
.WithOpenApi()
;
app.MapGet("/todos/{id}", async (int id, HttpContext context, IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("TodoService");
    
    if (context.Request.Headers.ContainsKey("Authorization"))
    {
        client.DefaultRequestHeaders.Authorization = 
            AuthenticationHeaderValue.Parse(context.Request.Headers["Authorization"]!);
    }
    
    var response = await client.GetAsync($"/api/todos/{id}");
    var result = await response.Content.ReadAsStringAsync();
    
    return Results.Content(result, "application/json", statusCode: (int)response.StatusCode);
})
.WithName("GetTodoById")
.WithOpenApi()
;

app.MapPost("/todos", async (CreateTodoRequest request, HttpContext context, IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("TodoService");
    
    if (context.Request.Headers.ContainsKey("Authorization"))
    {
        client.DefaultRequestHeaders.Authorization = 
            AuthenticationHeaderValue.Parse(context.Request.Headers["Authorization"]!);
    }
    
    var json = JsonSerializer.Serialize(request);
    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    
    var response = await client.PostAsync("/api/todos", content);
    var result = await response.Content.ReadAsStringAsync();
    
    return Results.Content(result, "application/json", statusCode: (int)response.StatusCode);
})
.WithName("CreateTodo")
.WithOpenApi()
;

app.MapPut("/todos/{id}", async (int id, UpdateTodoRequest request, HttpContext context, IHttpClientFactory clientFactory, IConnectionMultiplexer redis) =>
{
    var client = clientFactory.CreateClient("TodoService");
    
    if (context.Request.Headers.ContainsKey("Authorization"))
    {
        client.DefaultRequestHeaders.Authorization = 
            AuthenticationHeaderValue.Parse(context.Request.Headers["Authorization"]!);
    }
    
    var json = JsonSerializer.Serialize(request);
    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    
    var response = await client.PutAsync($"/api/todos/{id}", content);
    var result = response.Content.Headers.ContentLength > 0 
        ? await response.Content.ReadAsStringAsync() 
        : "";
    
    return Results.Content(result, "application/json", statusCode: (int)response.StatusCode);
})
.WithName("UpdateTodo")
.WithOpenApi()
;

app.MapDelete("/todos/{id}", async (int id, HttpContext context, IHttpClientFactory clientFactory, IConnectionMultiplexer redis) =>
{
    var client = clientFactory.CreateClient("TodoService");
    
    if (context.Request.Headers.ContainsKey("Authorization"))
    {
        client.DefaultRequestHeaders.Authorization = 
            AuthenticationHeaderValue.Parse(context.Request.Headers["Authorization"]!);
    }
    
    var response = await client.DeleteAsync($"/api/todos/{id}");
    var result = response.Content.Headers.ContentLength > 0 
        ? await response.Content.ReadAsStringAsync() 
        : "";
    
    return Results.Content(result, "application/json", statusCode: (int)response.StatusCode);
})
.WithName("DeleteTodo")
.WithOpenApi()
;

app.Run();










