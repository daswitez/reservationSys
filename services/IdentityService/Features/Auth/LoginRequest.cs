using System.ComponentModel.DataAnnotations;

namespace IdentityService.Features.Auth;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "El email es obligatorio.")]
    [EmailAddress(ErrorMessage = "El email no es valido.")]
    [StringLength(180, ErrorMessage = "El email no puede superar 180 caracteres.")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "La contrasena es obligatoria.")]
    [StringLength(128, ErrorMessage = "La contrasena no puede superar 128 caracteres.")]
    public string Password { get; init; } = string.Empty;
}
