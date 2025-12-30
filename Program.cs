using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using ApiGateway.DTOs;

var builder = WebApplication.CreateBuilder(args);

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

app.MapGet("/todos", async (HttpContext context, IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("TodoService");
    
    if (context.Request.Headers.ContainsKey("Authorization"))
    {
        client.DefaultRequestHeaders.Authorization = 
            AuthenticationHeaderValue.Parse(context.Request.Headers["Authorization"]!);
    }
    
    var response = await client.GetAsync("/api/todos");
    var result = await response.Content.ReadAsStringAsync();
    
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

app.MapPut("/todos/{id}", async (int id, UpdateTodoRequest request, HttpContext context, IHttpClientFactory clientFactory) =>
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

app.MapDelete("/todos/{id}", async (int id, HttpContext context, IHttpClientFactory clientFactory) =>
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

