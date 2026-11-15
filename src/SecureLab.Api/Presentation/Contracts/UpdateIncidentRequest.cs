namespace SecureLab.Api.Presentation.Contracts;

// Меж довіри: клієнт може змінити лише зміст (title, description).
// OwnerUserId, role, status та інші server-managed поля в контракті відсутні,
// тому надіслані клієнтом зайві поля ігноруються (сценарій A-08a).
public sealed record UpdateIncidentRequest(string? Title, string? Description);
