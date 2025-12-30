namespace ApiGateway.DTOs;

// ========== AUTH DTOs ==========
public record RegisterRequest(string Email, string Password, string FullName);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, string Email, string FullName);

// ========== TODO DTOs ==========
public record CreateTodoRequest(string Title, string? Description, bool IsCompleted);
public record UpdateTodoRequest(int Id, string Title, string? Description, bool IsCompleted);

public record TodoItemResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int UserId { get; set; }
}
