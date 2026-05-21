using System.ComponentModel.DataAnnotations;

namespace InventoryApi.Common;

/// <summary>
/// Minimal APIs do not auto-validate DataAnnotations on bound bodies. This
/// helper runs the annotations and produces an RFC 7807 ValidationProblem
/// result with per-field errors when invalid.
/// </summary>
public static class Validation
{
    public static (bool Ok, IDictionary<string, string[]> Errors) Validate<T>(T model) where T : notnull
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        if (ok)
        {
            return (true, new Dictionary<string, string[]>());
        }
        var errors = results
            .SelectMany(r => r.MemberNames.DefaultIfEmpty("").Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.ErrorMessage ?? "invalid").ToArray());
        return (false, errors);
    }
}
