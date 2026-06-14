using System.ComponentModel.DataAnnotations;

namespace IdentityService.Features.Users;

public sealed class ChangePasswordRequest
{
    [Required(ErrorMessage = "La contrasena actual es obligatoria.")]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "La nueva contrasena es obligatoria.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "La nueva contrasena debe tener entre 8 y 128 caracteres.")]
    public string NewPassword { get; init; } = string.Empty;
}
