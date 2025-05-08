using System.ComponentModel.DataAnnotations;

namespace APBD7.Models;

public class ClientModel
{
    [Required]
    public string FirstName { get; set; } = null!;

    [Required]
    public string LastName { get; set; } = null!;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;

    [Required]
    [Phone]
    public string Telephone { get; set; } = null!;

    [Required]
    [RegularExpression(@"^\d{11}$", ErrorMessage = "Niepoprawna długość PESEL")]
    public string Pesel { get; set; } = null!;
}