using AvitoAgent.Avito.Options;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Tests.Avito;

public sealed class AvitoOptionsValidatorTests
{
    private readonly AvitoOptionsValidator _validator = new();

    [Fact]
    public void Valid_defaults_succeed()
    {
        var options = new AvitoOptions
        {
            Auth = new AvitoAuthOptions { Enabled = false },
        };
        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? []));
    }

    [Fact]
    public void Invalid_base_url_sort_condition_seller()
    {
        var options = new AvitoOptions
        {
            BaseUrl = "not-a-url",
            Filters = new AvitoFiltersOptions
            {
                Sort = "zzz",
                Condition = "zzz",
                SellerType = "zzz",
            },
        };

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("BaseUrl", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("Sort", StringComparison.OrdinalIgnoreCase) || f.Contains("сортир", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MaxPages_must_be_positive()
    {
        var options = new AvitoOptions { MaxPages = 0 };
        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("MaxPages", StringComparison.Ordinal));
    }

    [Fact]
    public void Auth_enabled_requires_credentials()
    {
        var options = new AvitoOptions
        {
            Auth = new AvitoAuthOptions
            {
                Enabled = true,
                Phone = "",
                Email = "",
                Password = "",
                StorageStateFile = "session.json",
            },
        };

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Phone", StringComparison.Ordinal) || f.Contains("Email", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("Password", StringComparison.Ordinal));
    }

    [Fact]
    public void Auth_disabled_skips_credentials_but_needs_storage_file()
    {
        var options = new AvitoOptions
        {
            Auth = new AvitoAuthOptions
            {
                Enabled = false,
                StorageStateFile = "",
            },
        };

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("StorageStateFile", StringComparison.Ordinal));
    }

    [Fact]
    public void Auth_enabled_with_phone_and_password_ok()
    {
        var options = new AvitoOptions
        {
            Auth = new AvitoAuthOptions
            {
                Enabled = true,
                Phone = "+79990001122",
                Password = "secret",
            },
        };

        Assert.Equal(ValidateOptionsResult.Success, _validator.Validate(null, options));
    }
}
